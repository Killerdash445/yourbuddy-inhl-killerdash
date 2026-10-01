using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Shadow: a black double runs through a door in your view. docs/anomalies.md#shadow
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Shadow: the dark double, its route in its station's frame, the door it runs through. docs/anomalies.md#shadow
        private GameObject? shadow = null;
        private Transform? shadowFrame = null;
        private readonly List<Vector3> shadowRoute = [];
        private int shadowLeg = 0;
        private Vector3 shadowFeet;
        private Gate? shadowDoor = null;
        private Vector3 shadowDoorMid;
        private Vector3 shadowAhead;
        private bool shadowFromOutside = false;
        private static readonly List<Gate> ShadowDoors = [];
        private static readonly List<Vector3> ShadowStarts = [];
        private const float ShadowDoorMin = 6f;
        private const float ShadowDoorMax = 25f;
        private const float ShadowStartMin = 3f;
        private const float ShadowStartMax = 10f;
        private const float ShadowRouteMax = 25f;
        private const float ShadowPastDoor = 1.5f;
        private const float ShadowDoorwayHalfWidth = 1.2f;
        private const float ShadowOpenDist = 1.2f;
        private const float ShadowSeenSeconds = 0.8f;
        private const float ShadowNearDist = 4f;
        private const float ShadowRunSeconds = 30f;
        private const float ShadowWaitSeconds = 120f;

        // ------------------------------------------------------------------
        // The shadow double
        // ------------------------------------------------------------------

        /// <summary>
        /// A black copy of it, out of your sight on your side of a door you can see, runs through that door
        /// and is gone. Aboard the Fuel, Oxygen or Solar station, or outside the one you are docked at,
        /// looking in through its windows. docs/anomalies.md#shadow
        /// </summary>
        private string? StartShadow(Transform you, bool outside)
        {
            if (ShadowStation(you, outside) is not { } station) return "it plays only at the Fuel, Oxygen or Solar station: aboard, or outside the one you are docked at";

            if (Model == null) return "it has no body to copy";

            string yours = station.gameObject.name;
            Transform frame = NpcVessels.InteriorOf(station);
            if (!frame.gameObject.activeInHierarchy) return $"'{yours}' is switched off";

            shadowFromOutside = outside;
            int near = 0;

            ShadowDoors.Clear();
            foreach (Gate gate in SceneScan.ThisFrame<Gate>())
            {
                if (gate == null || gate.Locked || !gate.gameObject.activeInHierarchy) continue;

                if (NpcVessels.OwnerOfTransform(gate.transform) != yours) continue;

                if (NpcDoors.IsAirlockGate(gate) || NpcDoors.IsPasswordGate(gate)) continue;

                float far = FlatDistance(gate.transform.position, you.position);
                if (far < ShadowDoorMin || far > ShadowDoorMax) continue;

                near++;
                // In front of the door on your side: a sight line to the door itself crosses its own shut panel,
                // which NavProbe.CanSee counts as blocked whatever the target.
                if (FarSideOf(gate, you.position) is not { } farSide) continue;

                Vector3 middle = OnFloor(gate.transform.position);
                Vector3 back = middle - farSide;
                back.y = 0f;
                if (ShadowSees(middle + back.normalized * 0.6f + Vector3.up * 1.2f)) ShadowDoors.Add(gate);
            }
            if (ShadowDoors.Count == 0)
            {
                return $"no door in your view {ShadowDoorMin:0}-{ShadowDoorMax:0}m away ({near} door(s) of '{yours}' that far, none in view)";
            }

            shadowMisses.Clear();
            for (int i = 0; i < ShadowDoors.Count; i++)
            {
                int pick = Random.Range(i, ShadowDoors.Count);
                (ShadowDoors[i], ShadowDoors[pick]) = (ShadowDoors[pick], ShadowDoors[i]);
                if (PlanShadow(ShadowDoors[i], you.position, yours, frame)) return null;
            }
            return $"no way to {ShadowDoors.Count} door(s) in view from out of your sight ({shadowMisses})";
        }

        /// <summary>
        /// Why PlanShadow turned down each door it tried, for the refusal.
        /// </summary>
        private readonly System.Text.StringBuilder shadowMisses = new();

        private void ShadowMiss(Gate gate, string why)
        {
            if (shadowMisses.Length > 0) shadowMisses.Append("; ");

            shadowMisses.Append(gate.transform.parent != null ? gate.transform.parent.name : gate.name).Append(": ").Append(why);
        }

        /// <summary>
        /// A ground node out of your sight on your side of `gate`, and a path from it through the doorway;
        /// the double is made there and the run begins. False when there is none.
        /// </summary>
        private bool PlanShadow(Gate gate, Vector3 yourPos, string owner, Transform frame)
        {
            Vector3 middle = gate.transform.position;
            if (NavProbe.TryFloorHeight(middle + Vector3.up * 0.5f, out float floorY)) middle.y = floorY;

            if (FarSideOf(gate, yourPos) is not { } farSide)
            {
                ShadowMiss(gate, "no floor past it");
                return false;
            }

            // A node marker hovers over its deck: npc-core:docs/invariants.md#node-hover-not-height
            Vector3 beyond = OnFloor(farSide);
            Vector3 ahead = beyond - middle;
            ahead.y = 0f;
            ahead.Normalize();
            ShadowStarts.Clear();
            int placed = 0;
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground || NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                float d = FlatDistance(node, middle);
                if (d < ShadowStartMin || d > ShadowStartMax || Mathf.Abs(node.y - middle.y) > 1.5f) continue;

                // On your side, a step back from the doorway, and not on top of you.
                if (Vector3.Dot(node - middle, ahead) > -1f || FlatDistance(node, yourPos) < ShadowNearDist + 2f) continue;

                placed++;
                if (ShadowSees(node + Vector3.up * 1.1f) || ShadowSees(node + Vector3.up * 0.3f)) continue;

                ShadowStarts.Add(node);
            }
            if (ShadowStarts.Count == 0)
            {
                ShadowMiss(gate, placed == 0 ? "no node on your side 3-10m from it" : $"all {placed} start node(s) in your view");
                return false;
            }
            int noPath = 0, tooLong = 0, otherDoor = 0;
            for (int tries = 0; tries < 8 && ShadowStarts.Count > 0; tries++)
            {
                int index = Random.Range(0, ShadowStarts.Count);
                Vector3 start = OnFloor(ShadowStarts[index]);
                ShadowStarts.RemoveAt(index);
                NavPath? plan = NavGraph.FindPath(start, beyond);
                if (plan is not { Count: > 0 } path)
                {
                    noPath++;
                    continue;
                }
                // Through this doorway, not round by another, and not far. Nodes sit 1-2 m either side of a
                // door, so the crossing is measured on the legs between them, not at the waypoints.
                float length = 0f;
                bool throughIt = false;
                Vector3 last = start;
                for (int w = 0; w <= path.Count; w++)
                {
                    Vector3 next = w < path.Count ? path[w] : beyond;
                    length += Vector3.Distance(last, next);
                    throughIt |= CrossesDoorway(last, next, middle, ahead);
                    last = next;
                }
                if (length > ShadowRouteMax) tooLong++;
                else if (!throughIt) otherDoor++;
                else return MakeShadow(gate, middle, ahead, start, path, beyond, frame);
            }
            ShadowMiss(gate, $"no path {noPath}, longer than {ShadowRouteMax:0}m {tooLong}, by another door {otherDoor}");
            return false;
        }

        /// <summary>
        /// The leg a-b steps over the doorway's plane, from your side to the far one, within a door's
        /// width of its middle.
        /// </summary>
        private static bool CrossesDoorway(Vector3 a, Vector3 b, Vector3 middle, Vector3 ahead)
        {
            float da = Vector3.Dot(a - middle, ahead);
            float db = Vector3.Dot(b - middle, ahead);
            if (da > 0f || db <= 0f) return false;

            Vector3 cross = Vector3.Lerp(a, b, da / (da - db));
            return FlatDistance(cross, middle) <= ShadowDoorwayHalfWidth;
        }

        private static Vector3 OnFloor(Vector3 point)
        {
            if (NavProbe.TryFloorHeight(point + Vector3.up * 0.5f, out float y)) point.y = y;

            return point;
        }

        private bool MakeShadow(Gate gate, Vector3 middle, Vector3 ahead, Vector3 start, NavPath path, Vector3 beyond, Transform frame)
        {
            shadowRoute.Clear();
            for (int w = 0; w < path.Count; w++) shadowRoute.Add(frame.InverseTransformPoint(new Vector3(path[w].x, path.FloorY(w), path[w].z)));
            shadowRoute.Add(frame.InverseTransformPoint(beyond));
            Vector3 first = path[0] - start;
            first.y = 0f;
            Quaternion facing = first.sqrMagnitude > 0.01f ? Quaternion.LookRotation(first) : Quaternion.LookRotation(ahead);
            shadow = BuddyDouble.Make(Model, transform, frame, start - Vector3.up * agent.OriginToFeet, facing, shadow: true, "YB_Shadow");
            if (shadow == null) return false;

            shadowFrame = frame;
            shadowFeet = frame.InverseTransformPoint(start);
            shadowLeg = 0;
            shadowDoor = gate;
            shadowDoorMid = frame.InverseTransformPoint(middle);
            shadowAhead = frame.InverseTransformDirection(ahead);
            StartHold(AnomalyKind.Shadow, ShadowWaitSeconds, keepPlan: true);
            anomalyGiveUpAt = Time.time + ShadowRunSeconds;
            BuddyDouble.Walk(shadow, agent.WalkSpeed * FleeSpeedFactor);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] A dark double of {Name} runs for '{gate.name}', {FlatDistance(start, middle):0.0}m from it");
            return true;
        }

        /// <summary>
        /// The run: along the route, opening the door if it is shut and shutting it behind; then gone. Seen a
        /// moment, or come near, it is gone at once.
        /// </summary>
        private void UpdateShadow(float now, Transform you)
        {
            if (shadowFrame == null || shadowDoor == null)
            {
                EndAnomaly("the station went away");
                return;
            }
            if (anomalyStep == 1)
            {
                // The door shutting behind it: Opened stays true until it has.
                if (!shadowDoor.Opened) anomalyStep = 2;
                else if (now >= anomalyStepAt) EndAnomaly("the door stayed open");

                return;
            }
            if (anomalyStep == 2)
            {
                // Behind the shut door, until you open it: nothing there.
                if (shadowDoor.Opened)
                {
                    ScareSounds.Play(ScareSound.Click, shadowFrame.TransformPoint(shadowRoute[shadowRoute.Count - 1]) + Vector3.up);
                    EndAnomaly("you opened the door - nobody behind it");
                }
                else if (now >= anomalyUntil)
                {
                    EndAnomaly("it stays gone");
                }
                return;
            }
            if (shadow == null)
            {
                EndAnomaly("the double went away");
                return;
            }
            Vector3 feet = shadowFrame.TransformPoint(shadowFeet);
            if (anomalySeenFor >= ShadowSeenSeconds || (anomalySeen && FlatDistance(feet, you.position) < ShadowNearDist))
            {
                Startle(15);
                EndAnomaly($"you looked at it ({anomalySeenFor:0.0}s, {FlatDistance(feet, you.position):0.0}m) - gone");
                return;
            }
            if (now >= anomalyGiveUpAt && !anomalySeen)
            {
                EndAnomaly("its run took too long");
                return;
            }
            float along = Vector3.Dot(shadowFeet - shadowDoorMid, shadowAhead);
            if (along > ShadowPastDoor || shadowLeg >= shadowRoute.Count)
            {
                ThroughTheDoor(now, you, shadowDoor, shadowFrame);
                return;
            }
            if (!shadowDoor.Opened && along > -ShadowOpenDist) shadowDoor.Open();
            // Never through a door that is still opening.
            if (!shadowDoor.FullyOpened && along > -0.5f) return;

            Vector3 to = shadowRoute[shadowLeg] - shadowFeet;
            float step = agent.WalkSpeed * FleeSpeedFactor * Time.deltaTime;
            if (to.magnitude <= step)
            {
                shadowFeet = shadowRoute[shadowLeg];
                shadowLeg++;
            }
            else
            {
                shadowFeet += to.normalized * step;
            }
            Vector3 flat = new(to.x, 0f, to.z);
            Quaternion facing = flat.sqrMagnitude > 0.0001f ? shadowFrame.rotation * Quaternion.LookRotation(flat) : shadow.transform.rotation;
            shadow.transform.SetPositionAndRotation(shadowFrame.TransformPoint(shadowFeet) - Vector3.up * agent.OriginToFeet, facing);
        }

        /// <summary>
        /// Past the doorway: gone, and the door shuts behind it unless you stand in it.
        /// </summary>
        private void ThroughTheDoor(float now, Transform you, Gate door, Transform frame)
        {
            RemoveShadow();
            bool shut = door.Opened && FlatDistance(you.position, frame.TransformPoint(shadowDoorMid)) > 2f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] The double is through '{door.name}'" + (shut ? " and shuts it" : "") +
                                        (anomalyWitnessed ? $" - you saw it {anomalySeenFor:0.0}s" : " - you never saw it"));
            if (!shut)
            {
                EndAnomaly("the door stays open");
                return;
            }
            door.Close();
            anomalyStep = 1;
            anomalyStepAt = now + 5f;
            anomalyUntil = now + ShadowWaitSeconds;
        }

        /// <summary>
        /// Seen: its chest or head, from your eyes. The double has no colliders to get in the way.
        /// </summary>
        private bool SeesShadow()
        {
            if (shadow == null || shadowFrame == null || anomalyStep != 0) return false;

            Vector3 feet = shadowFrame.TransformPoint(shadowFeet);
            return ShadowSees(feet + Vector3.up * 1.1f) || ShadowSees(feet + Vector3.up * 1.6f);
        }

        /// <summary>
        /// Out in space you see in through the windows; aboard, as anything is seen.
        /// </summary>
        private bool ShadowSees(Vector3 point) => shadowFromOutside ? PlayerView.SeesThroughWindows(point) : PlayerView.Sees(point, null);

        /// <summary>
        /// The station the shadow plays on: the Fuel, Oxygen or Solar station you stand on, or, outside, the one
        /// your ship is docked at.
        /// </summary>
        private static SpaceStation? ShadowStation(Transform you, bool outside)
        {
            SpaceStation? station = null;
            if (!outside)
            {
                if (NpcVessels.FloorOwner(you.position, out string? owner, out _) == FloorOwnership.Elsewhere && owner != null) station = StationNamed(owner);
            }
            else if (GameManager.Instance != null && GameManager.Instance.PlayerShip != null)
            {
                SpaceShip ship = GameManager.Instance.PlayerShip;
                foreach (SpaceStation docked in Object.FindObjectsOfType<SpaceStation>())
                {
                    if (docked != null && docked.Docker != null && docked.Docker.DockedShip == ship) station = docked;
                }
            }
            return station is FuelStation or OxygenStation or SolarStation ? station : null;
        }

        private void RemoveShadow()
        {
            if (shadow != null) Destroy(shadow);

            shadow = null;
        }
    }
}
