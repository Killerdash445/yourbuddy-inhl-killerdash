using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The meat squelches as you take it, let it go or throw it, and as it lands hard. The skull it is cloned
    /// from would clonk, so that sound is muted. docs/anomalies.md#the-mess-and-the-meat-model
    /// </summary>
    internal sealed class MeatSounds : MonoBehaviour
    {
        /// <summary>A landing slower than this (m/s) makes no sound.</summary>
        private const float LandSpeed = 1.5f;
        /// <summary>The least time between two squelches, so one throw is not three.</summary>
        private const float GapSeconds = 0.4f;

        private Grabbable? meat;
        private Renderer? look;
        private float quietUntil;

        private void Awake()
        {
            meat = GetComponent<Grabbable>();
            look = GetComponentInChildren<Renderer>();
            if (meat == null) return;

            meat.OnGrabbed.AddListener(Squelch);
            meat.OnDropped.AddListener(Squelch);
        }

        private void OnCollisionEnter(Collision hit)
        {
            if (meat == null || meat.IsGrabbed || hit.relativeVelocity.sqrMagnitude < LandSpeed * LandSpeed) return;

            Squelch();
        }

        private void Squelch()
        {
            if (Time.time < quietUntil) return;

            quietUntil = Time.time + GapSeconds;
            // From the middle of the meat, not its pivot, so the floor under it never muffles it.
            Vector3 at = look != null ? look.bounds.center : transform.position;
            ScareSounds.Play(ScareSound.Slime, at, transform, out _);
        }
    }
}
