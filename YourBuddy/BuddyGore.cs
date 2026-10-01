using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's suit spattered with blood: a copy of the texture it wears now, with dark red blots
    /// painted over it, put on and taken off through BuddySkin. docs/anomalies.md#bloody
    /// </summary>
    internal static class BuddyGore
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly Dictionary<(Texture, int), Texture2D> Made = [];

        /// <summary>
        /// Blots per 64x64 texels, and their radius in texels at that size.
        /// </summary>
        private const int Blots = 16;
        private const float BlotMinRadius = 1f;
        private const float BlotMaxRadius = 4f;
        private static readonly Color Blood = new(0.4f, 0.02f, 0.03f, 1f);

        /// <summary>
        /// Puts the bloody copy of what the body wears on it; `seed` keeps one buddy's stains the same
        /// each time. Returns the texture it was wearing, for Remove, or null when nothing took it.
        /// </summary>
        internal static Texture? Apply(Component body, int seed, out Texture2D? bloody)
        {
            bloody = null;
            Texture? wearing = Current(body);
            if (wearing == null) return null;

            bloody = Make(wearing, seed);
            if (bloody == null || BuddySkin.ApplyTexture(body, bloody) == 0) return null;

            return wearing;
        }

        /// <summary>
        /// Back to the look it had before Apply: its original, or the skin it wore then.
        /// </summary>
        internal static void Remove(Component body, Texture? wearing)
        {
            BuddySkin.RestoreAll(body);
            if (wearing is Texture2D before && Current(body) != before) BuddySkin.ApplyTexture(body, before);
        }

        /// <summary>
        /// The texture on the body's first skinned material that takes one.
        /// </summary>
        private static Texture? Current(Component body)
        {
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    if (material.HasProperty(BaseMap) && material.GetTexture(BaseMap) is { } baseMap) return baseMap;

                    if (material.HasProperty(MainTex) && material.GetTexture(MainTex) is { } mainTex) return mainTex;
                }
            }
            return null;
        }

        private static Texture2D? Make(Texture source, int seed)
        {
            if (Made.TryGetValue((source, seed), out Texture2D? cached) && cached != null) return cached;

            Texture2D? copy = Readable(source);
            if (copy == null) return null;

            Color[] pixels = copy.GetPixels();
            int w = copy.width;
            int h = copy.height;
            float scale = w / 64f;
            System.Random random = new(seed * 7919 + 17);
            int placed = 0;
            for (int tries = 0; placed < Blots && tries < Blots * 20; tries++)
            {
                int cx = random.Next(w);
                int cy = random.Next(h);
                // Only on the painted parts of the atlas: a blot on its empty margin shows nowhere.
                if (pixels[cy * w + cx].a < 0.5f) continue;

                placed++;
                float radius = (BlotMinRadius + (float)random.NextDouble() * (BlotMaxRadius - BlotMinRadius)) * scale;
                Blot(pixels, w, h, cx, cy, radius, random);
            }
            copy.SetPixels(pixels);
            copy.Apply(false);
            Made[(source, seed)] = copy;
            return copy;
        }

        private static void Blot(Color[] pixels, int w, int h, int cx, int cy, float radius, System.Random random)
        {
            int r = Mathf.CeilToInt(radius);
            float shade = 0.7f + (float)random.NextDouble() * 0.3f;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            {
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / Mathf.Max(0.5f, radius);
                    if (d > 1f) continue;

                    // Ragged edge: the rim is hit or miss.
                    if (d > 0.6f && random.NextDouble() < d - 0.3f) continue;

                    Color was = pixels[y * w + x];
                    Color blood = Blood * shade;
                    Color mixed = Color.Lerp(was, blood, d < 0.6f ? 0.92f : 0.7f);
                    mixed.a = was.a;
                    pixels[y * w + x] = mixed;
                }
            }
        }

        /// <summary>
        /// A CPU copy of any texture, readable or not, through a render target.
        /// </summary>
        private static Texture2D? Readable(Texture source)
        {
            int w = source.width;
            int h = source.height;
            if (w <= 0 || h <= 0) return null;

            RenderTexture target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                Texture2D copy = new(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = source.filterMode,
                    wrapMode = TextureWrapMode.Clamp,
                    name = source.name + "_bloody",
                };
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                copy.Apply(false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
