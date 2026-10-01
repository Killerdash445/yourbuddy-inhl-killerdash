using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A copy of a buddy's body and nothing else: no brain, no agent, no colliders, never saved. The
    /// shadow running through a station and the sleeper in its capsule. docs/anomalies.md#shadow
    /// </summary>
    internal static class BuddyDouble
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color_ = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int IsWalking = Animator.StringToHash("isWalking");
        private static readonly int IsGrounded = Animator.StringToHash("isGrounded");
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Velocity = Animator.StringToHash("Velocity");
        /// <summary>
        /// The shadow's colour: near black, a little blue, so it reads as a figure and not a hole.
        /// </summary>
        private static readonly Color ShadowTint = new(0.015f, 0.015f, 0.025f, 1f);

        /// <summary>
        /// A copy of `model`, the animated model under `buddy`, under `parent`, with its root where the
        /// buddy's would stand at `origin` facing `rotation`. `shadow` paints it black. Null without a model.
        /// </summary>
        internal static GameObject? Make(GameObject? model, Transform buddy, Transform? parent, Vector3 origin, Quaternion rotation,
            bool shadow, string name)
        {
            if (model == null) return null;

            // Built inactive, so nothing copied wakes up before it is stripped.
            GameObject root = new(name);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(origin, rotation);
            GameObject copy = Object.Instantiate(model, root.transform);
            copy.transform.localPosition = buddy.InverseTransformPoint(model.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(buddy.rotation) * model.transform.rotation;
            Vector3 scale = model.transform.lossyScale;
            Vector3 own = buddy.lossyScale;
            copy.transform.localScale = new Vector3(scale.x / own.x, scale.y / own.y, scale.z / own.z);
            Strip(copy);
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = 0;
                if (shadow) Darken(renderer);
            }
            foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;

            root.SetActive(true);
            return root;
        }

        /// <summary>
        /// Walking at `speed`, or standing still at 0: the parameters the player's animator reads.
        /// </summary>
        internal static void Walk(GameObject root, float speed)
        {
            foreach (Animator animator in root.GetComponentsInChildren<Animator>())
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.nameHash == IsWalking) animator.SetBool(IsWalking, speed > 0.1f);
                    else if (p.nameHash == IsGrounded) animator.SetBool(IsGrounded, true);
                    else if (p.nameHash == Speed) animator.SetFloat(Speed, speed);
                    else if (p.nameHash == Velocity) animator.SetFloat(Velocity, speed);
                }
            }
        }

        /// <summary>
        /// Every script, joint, body and collider out: it is a picture of the buddy, nothing more.
        /// Joints before bodies, which they require.
        /// </summary>
        private static void Strip(GameObject copy)
        {
            foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (Joint joint in copy.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
            foreach (Rigidbody rigidbody in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rigidbody);
            foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        }

        private static void Darken(Renderer renderer)
        {
            foreach (Material material in renderer.materials)
            {
                if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, ShadowTint);
                if (material.HasProperty(Color_)) material.SetColor(Color_, ShadowTint);
                if (material.HasProperty(EmissionColor)) material.SetColor(EmissionColor, Color.black);
                material.DisableKeyword("_EMISSION");
            }
        }
    }
}
