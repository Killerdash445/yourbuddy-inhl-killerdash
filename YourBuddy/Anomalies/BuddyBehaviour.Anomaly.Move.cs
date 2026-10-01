using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Move: left on a station, it turns up where you are. docs/anomalies.md#move
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private const float MoveWaitSeconds = 300f;
        private const float MoveStartleDist = 4f;

        // ------------------------------------------------------------------
        // Turning up far from where you left it
        // ------------------------------------------------------------------

        /// <summary>
        /// Why it cannot turn up near you, or null: it must be parked on a station, its interior switched
        /// off. The only kind a parked buddy acts out. docs/anomalies.md#move
        /// </summary>
        internal string? MoveReady()
        {
            if (IsDead) return "dead";

            if (gameObject.activeInHierarchy) return "it is not left behind anywhere";

            if (anomaly.HasValue) return "already acting one out";

            if (Asleep) return "asleep";

            string? owner = agent.CurrentOwner;
            return owner == null || owner == NavGraph.ShipOwner ? "it is parked with the ship, not left on a station" : null;
        }

        /// <summary>
        /// Out of the switched-off interior and onto the station you are on, out of your sight; any order it had
        /// is gone. Never onto a ship: it came without one.
        /// </summary>
        private string? StartMove(Transform you)
        {
            if (NpcVessels.FloorOwner(you.position, out string? yours, out _) != FloorOwnership.Elsewhere || yours == null ||
                StationNamed(yours) == null)
            {
                return "you are not aboard a station - it joins you only on another station";
            }

            string from = agent.CurrentOwner ?? "?";
            if (yours == from) return "you are on its station";

            Vector3? spot = HiddenSpotNear(you);
            if (spot == null) return "no spot near you out of your sight";

            // Into a live frame: the ship's is the scene root, a station's MoveTo rides. It wakes there.
            transform.SetParent(null, true);
            MoveTo(spot.Value);
            agent.ClearMoveTarget();
            if (orderedMode.HasValue)
            {
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} forgets the order '{OrderName(orderedMode.Value)}' it had on '{from}'");
                orderedMode = null;
            }
            SetMode(BuddyMode.Follow);
            StartHold(AnomalyKind.Move, MoveWaitSeconds);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}, left on '{from}', turns up on '{yours}' {FlatDistance(spot.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// Stands where it turned up until you find it; then a line, and it follows you again.
        /// </summary>
        private void UpdateMove(float now, float dist)
        {
            if (anomalySeen)
            {
                if (dist < MoveStartleDist) Startle(20);

                SpeakNew(AnomalyLines.LeftBehind());
                EndAnomaly($"you found it {dist:0.0}m away");
                return;
            }
            if (now >= anomalyUntil) EndAnomaly("you never looked its way");
        }

        /// <summary>
        /// The station by its owner name (its GameObject's), or null for a ship or anything else.
        /// </summary>
        private static SpaceStation? StationNamed(string owner)
        {
            foreach (SpaceStation station in Object.FindObjectsOfType<SpaceStation>())
            {
                if (station != null && station.gameObject.name == owner) return station;
            }
            return null;
        }
    }
}
