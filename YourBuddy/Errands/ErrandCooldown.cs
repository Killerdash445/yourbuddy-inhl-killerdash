using System;

namespace YourBuddy
{
    // Failed work has a hard retry limit, separate from ordinary readiness scoring.
    internal sealed class ErrandCooldown
    {
        private float until;
        internal bool Ready(float now) => now >= until;
        internal void Hold(float now, float seconds) => until = Math.Max(until, now + Math.Max(0, seconds));
    }
}
