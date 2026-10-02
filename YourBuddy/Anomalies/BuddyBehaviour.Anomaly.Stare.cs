using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// WindowStare and WallStare. It walks up to a window or into a corner and stares. docs/anomalies.md#windowstare-and-wallstare
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private static readonly RaycastHit[] WallHits = new RaycastHit[8];
        private const float StareMinSeconds = 50f;
        private const float StareMaxSeconds = 140f;
        private const float StareSearchRadius = 20f;
        private const int CornerRays = 16;
        private const float WallSearchDist = 4f;
        private const float CornerSearchDist = 5f;
        private const float CornerStandOff = 0.65f;
        private const float CornerSlack = 0.35f;
        private static readonly Vector3[] WallPoints = new Vector3[CornerRays];
        private static readonly Vector3[] WallNormals = new Vector3[CornerRays];

        // Window and wall

        /// <summary>
        /// A window on its own vessel within StareSearchRadius, and a node in front of it to stand on.
        /// </summary>
        private string? StartWindowStare()
        {
            Vector3 here = transform.position;
            Renderer? best = null;
            float bestDist = float.MaxValue;
            foreach (MeshRenderer renderer in SceneScan.ThisFrame<MeshRenderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                // The pane itself. Every window block of ship and station has a child 'Glass'.
                if (!renderer.name.StartsWith("Glass")) continue;

                float d = Vector3.Distance(renderer.bounds.center, here);
                if (d > StareSearchRadius || d >= bestDist) continue;

                if (!agent.OnMyVessel(renderer.transform)) continue;

                best = renderer;
                bestDist = d;
            }
            if (best == null) return $"no window within {StareSearchRadius:0}m";

            Vector3 window = best.bounds.center;
            Vector3? stand = NodeNear(window, 0.8f, 4f);
            if (stand == null) return "no node in front of the window";

            NavPath? plan = NavGraph.FindPath(agent.FloorUnderNpc(), stand.Value);
            if (plan is not { Count: > 0 }) return "no way to the window";

            StartHold(AnomalyKind.WindowStare, StareMaxSeconds + 60f);
            agent.CommitPlan(plan.Value, false);
            anomalyWalking = true;
            anomalyStand = stand.Value;
            anomalyArrival = NpcAgent.ReachStandArrival;
            anomalyFace = window;
            anomalyGiveUpAt = Time.time + 10f + Vector3.Distance(here, stand.Value) * 1.5f;
            return null;
        }

        /// <summary>
        /// The nearest inside corner around it: two walls at chest height that meet, walked to in a straight line.
        /// </summary>
        private string? StartWallStare()
        {
            Vector3 chest = agent.GroundPos(1.1f);
            int walls = 0;
            for (int i = 0; i < CornerRays; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 360f / CornerRays, 0f) * Vector3.forward;
                if (!NearestWall(chest, dir, WallSearchDist, out RaycastHit hit)) continue;

                Vector3 normal = new(hit.normal.x, 0f, hit.normal.z);
                // A floor, a ceiling or a slope is not a wall.
                if (normal.magnitude < 0.95f) continue;

                WallPoints[walls] = hit.point;
                WallNormals[walls] = normal.normalized;
                walls++;
            }

            Vector3 floor = agent.FloorUnderNpc();
            Vector3 bestStand = Vector3.zero;
            Vector3 bestCorner = Vector3.zero;
            float bestDist = float.MaxValue;
            for (int a = 0; a < walls; a++)
            {
                for (int b = a + 1; b < walls; b++)
                {
                    if (!CornerOf(a, b, out Vector3 corner)) continue;

                    Vector3 toCorner = corner - chest;
                    toCorner.y = 0f;
                    float d = toCorner.magnitude;
                    if (d > CornerSearchDist || d >= bestDist) continue;

                    // The walls really meet there: no doorway in the corner, nothing in front of it.
                    if (!NearestWall(chest, toCorner / d, d + CornerSlack, out RaycastHit hit) || hit.distance < d - CornerSlack) continue;

                    Vector3 stand = corner + (WallNormals[a] + WallNormals[b]).normalized * CornerStandOff;
                    stand.y = floor.y;
                    if (Vector3.Distance(stand, floor) > 0.3f && !NavProbe.WalkLos(floor, stand, 0.5f)) continue;

                    bestDist = d;
                    bestStand = stand;
                    bestCorner = corner;
                }
            }
            if (bestDist == float.MaxValue) return $"no corner it can walk into within {CornerSearchDist:0}m ({walls} wall hits)";

            StartHold(AnomalyKind.WallStare, StareMaxSeconds + 60f);
            anomalyWalking = true;
            anomalyStand = bestStand;
            anomalyArrival = NpcAgent.ReachStandArrival;
            anomalyFace = new Vector3(bestCorner.x, chest.y + 0.4f, bestCorner.z);
            anomalyGiveUpAt = Time.time + 8f;
            return null;
        }

        /// <summary>
        /// Where walls a and b meet, if they form an inside corner: roughly square, both facing the buddy's side.
        /// </summary>
        private static bool CornerOf(int a, int b, out Vector3 corner)
        {
            corner = Vector3.zero;
            Vector3 na = WallNormals[a], nb = WallNormals[b];
            if (Mathf.Abs(Vector3.Dot(na, nb)) > 0.5f) return false;

            // The two wall planes, crossed in the floor plane.
            float da = na.x * WallPoints[a].x + na.z * WallPoints[a].z;
            float db = nb.x * WallPoints[b].x + nb.z * WallPoints[b].z;
            float det = na.x * nb.z - na.z * nb.x;
            corner = new Vector3((da * nb.z - db * na.z) / det, WallPoints[a].y, (na.x * db - nb.x * da) / det);

            // Inside, not the outer edge of a box: each wall runs from the corner to the other's front side.
            return Vector3.Dot(WallPoints[a] - corner, nb) > 0.05f && Vector3.Dot(WallPoints[b] - corner, na) > 0.05f;
        }

        /// <summary>
        /// The nearest hit along a ray, if it is a wall.
        /// </summary>
        private static bool NearestWall(Vector3 from, Vector3 dir, float range, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            int count = Physics.RaycastNonAlloc(from, dir, WallHits, range, NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                if (!IsWall(WallHits[h].collider) || WallHits[h].distance >= best) continue;

                best = WallHits[h].distance;
                nearest = WallHits[h];
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Building, not a body or a loose item, which is what a wall stare may face.
        /// </summary>
        private static bool IsWall(Collider? collider)
        {
            if (collider == null) return false;

            Transform t = collider.transform;
            if (NpcPlayer.Controller is { } you && t.IsChildOf(you.transform)) return false;

            return t.GetComponentInParent<NpcAgent>() == null && t.GetComponentInParent<Grabbable>() == null;
        }


        /// <summary>
        /// Steer while it walks to a window, a corner or through a door. The plan, then a straight stretch
        /// to the stand point.
        /// </summary>
        private Vector3 WalkAnomalyLeg(out bool wantMove)
        {
            wantMove = false;
            if (Time.time > anomalyGiveUpAt)
            {
                if (anomaly == AnomalyKind.ShutDoors) NextDoorOrEnd("that door took too long");
                else if (anomaly == AnomalyKind.Bloody) StopBloodyRun();
                else if (anomaly == AnomalyKind.Meat) StopCaughtRun();
                else EndAnomaly("the walk there took too long");

                return Vector3.zero;
            }
            // Running from you after the robot, and round the doors; a walk otherwise.
            float speed = anomaly is AnomalyKind.BotTalk or AnomalyKind.Bloody or AnomalyKind.Meat or AnomalyKind.ShutDoors
                ? agent.WalkSpeed * FleeSpeedFactor
                : agent.WalkSpeed;
            if (agent.HasPlanLeft)
            {
                agent.AdvancePlan();
                if (agent.HasPlanLeft) return agent.HeadAlongPlan(speed, out wantMove);

                agent.DropPlan();
            }
            Vector3 toStand = anomalyStand - transform.position;
            toStand.y = 0f;
            if (toStand.sqrMagnitude <= anomalyArrival * anomalyArrival)
            {
                anomalyWalking = false;
                agent.ClearMoveTarget();
                // On to the next door at once; the agent shuts this one behind it.
                if (anomaly == AnomalyKind.ShutDoors) return Vector3.zero;
                if (anomaly == AnomalyKind.BotTalk)
                {
                    anomalyStep = 3;
                    anomalyUntil = Time.time + RunOffLegSeconds;
                    return Vector3.zero;
                }
                if (anomaly == AnomalyKind.Bloody)
                {
                    StopBloodyRun();
                    return Vector3.zero;
                }
                if (anomaly == AnomalyKind.Meat)
                {
                    StopCaughtRun();
                    return Vector3.zero;
                }
                anomalyUntil = Time.time + Random.Range(StareMinSeconds, StareMaxSeconds);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stops and stares at the " +
                                            (anomaly == AnomalyKind.WindowStare ? "window" : "corner") +
                                            $" for {anomalyUntil - Time.time:0}s");
                return Vector3.zero;
            }
            wantMove = true;
            agent.SetMoveTarget(anomalyStand);
            return toStand.normalized * speed;
        }
    }
}
