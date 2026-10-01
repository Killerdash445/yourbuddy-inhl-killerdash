using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A Blockbench model (.bbmodel, embedded in the dll) as a Unity mesh and its texture, built once.
    /// Cubes only, unrotated, one texture; units are centimetres. docs/anomalies.md#the-mess-and-the-meat-model
    /// </summary>
    internal static class BbModel
    {
        internal sealed class Model(Mesh mesh, Texture2D texture)
        {
            public readonly Mesh Mesh = mesh;
            public readonly Texture2D Texture = texture;
        }

        private const float UnitsPerMetre = 100f;
        private static readonly Dictionary<string, Model?> Cache = [];

        private static readonly string[] FaceNames = ["north", "south", "east", "west", "up", "down"];

        /// <summary>
        /// The model in an embedded resource, or null (logged once) when it is missing or unreadable.
        /// </summary>
        internal static Model? Load(string resource)
        {
            if (Cache.TryGetValue(resource, out Model? cached) && (cached == null || cached.Mesh != null)) return cached;

            Model? model = null;
            try
            {
                using Stream? stream = typeof(BbModel).Assembly.GetManifestResourceStream(resource);
                if (stream == null) throw new InvalidDataException("not in the dll");

                using StreamReader reader = new(stream);
                model = Build(JObject.Parse(reader.ReadToEnd()), resource);
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[anomaly] Model {resource} could not be built: {ex.Message}");
            }
            Cache[resource] = model;
            return model;
        }

        private static Model Build(JObject json, string resource)
        {
            float texW = (float?)json["resolution"]?["width"] ?? 16f;
            float texH = (float?)json["resolution"]?["height"] ?? 16f;
            List<Vector3> vertices = [];
            List<Vector3> normals = [];
            List<Vector2> uvs = [];
            List<int> triangles = [];
            foreach (JToken element in json["elements"] ?? new JArray())
            {
                if ((string?)element["type"] is { } type && type != "cube") continue;

                Vector3 from = Vec(element["from"]);
                Vector3 to = Vec(element["to"]);
                if (Vec(element["rotation"]) != Vector3.zero)
                {
                    YourBuddyPlugin.Log.LogWarning($"[anomaly] {resource}: cube '{element["name"]}' is rotated - drawn unrotated");
                }
                foreach (string face in FaceNames)
                {
                    JToken? data = element["faces"]?[face];
                    if (data == null || data["texture"] == null || data["texture"]!.Type == JTokenType.Null) continue;

                    AddFace(face, from, to, data["uv"], texW, texH, vertices, normals, uvs, triangles);
                }
            }
            Mesh mesh = new() { name = resource };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            string source = (string?)json["textures"]?[0]?["source"] ?? "";
            int comma = source.IndexOf(',');
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (comma < 0 || !texture.LoadImage(Convert.FromBase64String(source[(comma + 1)..])))
            {
                throw new InvalidDataException("its texture is not an embedded PNG");
            }
            texture.name = resource;
            return new Model(mesh, texture);
        }

        /// <summary>
        /// One face as Blockbench draws it: corners top-left, top-right, bottom-right, bottom-left seen
        /// from outside, matching the face's UV rectangle. Blockbench's z is mirrored into Unity's.
        /// </summary>
        private static void AddFace(string face, Vector3 a, Vector3 b, JToken? uv, float texW, float texH,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            (Vector3[] corners, Vector3 normal) = face switch
            {
                "north" => (new[] { V(b.x, b.y, a.z), V(a.x, b.y, a.z), V(a.x, a.y, a.z), V(b.x, a.y, a.z) }, Vector3.back),
                "south" => (new[] { V(a.x, b.y, b.z), V(b.x, b.y, b.z), V(b.x, a.y, b.z), V(a.x, a.y, b.z) }, Vector3.forward),
                "east" => (new[] { V(b.x, b.y, b.z), V(b.x, b.y, a.z), V(b.x, a.y, a.z), V(b.x, a.y, b.z) }, Vector3.right),
                "west" => (new[] { V(a.x, b.y, a.z), V(a.x, b.y, b.z), V(a.x, a.y, b.z), V(a.x, a.y, a.z) }, Vector3.left),
                "up" => (new[] { V(a.x, b.y, a.z), V(b.x, b.y, a.z), V(b.x, b.y, b.z), V(a.x, b.y, b.z) }, Vector3.up),
                _ => (new[] { V(a.x, a.y, b.z), V(b.x, a.y, b.z), V(b.x, a.y, a.z), V(a.x, a.y, a.z) }, Vector3.down),
            };
            Vector3 unityNormal = new(normal.x, normal.y, -normal.z);
            float u0 = (float?)uv?[0] ?? 0f, v0 = (float?)uv?[1] ?? 0f, u1 = (float?)uv?[2] ?? 0f, v1 = (float?)uv?[3] ?? 0f;
            Vector2[] faceUvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)];
            int first = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(new Vector3(corners[i].x, corners[i].y, -corners[i].z) / UnitsPerMetre);
                normals.Add(unityNormal);
                uvs.Add(new Vector2(faceUvs[i].x / texW, 1f - faceUvs[i].y / texH));
            }
            // The mirror flips the winding: put it back so the face points out.
            Vector3 drawn = Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]);
            if (Vector3.Dot(drawn, unityNormal) >= 0f) triangles.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            else triangles.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private static Vector3 Vec(JToken? token) =>
            token is JArray { Count: >= 3 } a ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
    }
}
