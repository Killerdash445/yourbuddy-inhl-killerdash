using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Interaction;
using NPC.Core.Navigation;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The anomalies one buddy acts out: funny, strange or frightening moments that make you doubt it is
    /// the friend you woke up with. AnomalyDirector decides when; this does them. docs/anomalies.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private AnomalyKind? anomaly = null;
        private int anomalyStep = 0;
        private float anomalyUntil = 0f;
        private float anomalyStepAt = 0f;
        private float anomalyGiveUpAt = 0f;
        private string anomalyLast = "none yet";

        // Being watched, sampled at SightSampleSeconds.
        private bool anomalySeen = false;
        private float anomalySeenFor = 0f;
        private float anomalyUnseenSince = 0f;
        private float anomalySightAt = 0f;
        private bool anomalyStartled = false;
        private const float SightSampleSeconds = 0.1f;

        // Vanished: drawn nowhere, no collisions, no AI. docs/invariants.md#an-anomaly-puts-back-what-it-changed
        private bool vanished = false;
        private readonly List<Renderer> vanishedRenderers = [];

        // Bloody: the texture it wore before.
        private Texture? goreWearing = null;
        private Texture2D? goreTexture = null;

        // Walking to a window or a wall, then staring at it.
        private bool anomalyWalking = false;
        private Vector3 anomalyStand;
        private Vector3 anomalyFace;
        private float anomalyArrival = NpcAgent.ReachStandArrival;
        private readonly List<Gate> doorsToShut = [];
        /// <summary>
        /// The round's second pass, over the doors the first left open.
        /// </summary>
        private bool doorsRetried = false;
        private static readonly float[] DoorPassDists = [DoorPassDist, 1.1f];

        // The wrong name in the talk window's title, until it is next opened.
        private string? wrongTitle = null;
        private float wrongTitleUntil = 0f;

        private static readonly List<Vector3> ReappearNodes = [];
        private static readonly RaycastHit[] WallHits = new RaycastHit[8];
        private static readonly float[] BehindTurns = [0f, 30f, -30f];

        /// <summary>
        /// The talk window's conversation with this buddy, set by SpawnBuddy.
        /// </summary>
        internal BuddyConversation? Conversation { get; set; }

        // Tuning: docs/reference.md#1-tuning-constants
        private const float SpinSeconds = 1.8f;
        private const float StareMinSeconds = 50f;
        private const float StareMaxSeconds = 140f;
        private const float StareSearchRadius = 20f;
        private const float WallSearchDist = 4f;
        private const float WallStandOff = 0.45f;
        private const float VanishMinSeconds = 40f;
        private const float VanishMaxSeconds = 110f;
        private const float ReappearMinDist = 6f;
        private const float ReappearMaxDist = 16f;
        private const float BloodySeconds = 150f;
        private const float BloodySeenRate = 6f;
        private const float BloodyUnseenSeconds = 5f;
        private const float BehindYouDist = 1.2f;
        private const float DoorPassDist = 1.6f;
        private const float DoorLegSeconds = 25f;
        private const float DoorWaitSeconds = 4f;
        private const int MaxDoors = 6;
        private const float WrongNameSeconds = 900f;

        /// <summary>
        /// One is running and holds the buddy, its looks or its schedule.
        /// </summary>
        internal bool AnomalyRunning => anomaly.HasValue;

        /// <summary>
        /// Gone from sight, the scanner and the monster's reach: docs/anomalies.md#vanish
        /// </summary>
        internal bool Vanished => vanished;

        /// <summary>
        /// It does not answer while vanished, frozen in a stare at you, or stalking you.
        /// </summary>
        internal bool IgnoresYou => vanished || anomaly is AnomalyKind.Statue or AnomalyKind.Stalker or AnomalyKind.BehindYou;

        /// <summary>
        /// Orders do not end these: it is not listening. docs/anomalies.md#2-the-anomalies
        /// </summary>
        private bool AnomalyIgnoresOrders => anomaly is AnomalyKind.Vanish or AnomalyKind.Stalker or AnomalyKind.BehindYou;

        /// <summary>
        /// Only its looks: it goes on through orders, tasks, fear and bad air. docs/anomalies.md#bloody
        /// </summary>
        private bool AnomalyIsLooks => anomaly == AnomalyKind.Bloody;

        /// <summary>
        /// An order or a task does not end these: a spin finishes first, blood stays on.
        /// </summary>
        private bool AnomalySurvivesOrders => anomaly == AnomalyKind.Spin || AnomalyIsLooks;

        /// <summary>
        /// The title the talk window shows instead of its name, once. docs/anomalies.md#wrongname
        /// </summary>
        internal string? TitleOverride => wrongTitle != null && Time.time < wrongTitleUntil ? wrongTitle : null;

        // ------------------------------------------------------------------
        // Starting
        // ------------------------------------------------------------------

        /// <summary>
        /// Why this buddy cannot act out anything now, or null.
        /// </summary>
        internal string? AnomalyReady()
        {
            if (IsDead) return "dead";

            if (Asleep || !gameObject.activeInHierarchy) return "not awake here";

            if (anomaly.HasValue) return "already acting one out";

            if (agent.IsOutside) return "outside";

            if (agent.IsBeingCaught) return "being caught";

            if (InDialog) return "being talked to";

            if (Hiding) return "hiding";
            // docs/invariants.md#fear-owns-the-buddy
            if (fearState != FearState.Calm || mode == BuddyMode.Flee) return "afraid";

            if (reachTask != null) return "busy " + DescribeReachTask();

            if (mode == BuddyMode.Route) return "walking to a node you sent it to";

            return suit.RunActive ? "on an airlock run" : null;
        }

        /// <summary>
        /// Starts one now, or says why not. The director has already judged the severity; buddy_anomaly
        /// skips that and the chance. docs/anomalies.md
        /// </summary>
        internal string? TryStartAnomaly(AnomalyKind kind)
        {
            string? blocker = AnomalyReady();
            if (blocker != null) return blocker;

            Player? player = PilotPlayer();
            if (player == null || player.Controller == null) return "no player";

            Transform you = player.Controller.CachedTransform;
            Vector3 feet = agent.FloorUnderNpc();
            Vector3 toYou = you.position - transform.position;
            toYou.y = 0f;
            float dist = toYou.magnitude;
            bool seen = PlayerView.SeesBody(transform, feet);
            NpcVessels.FloorOwner(you.position, out string? yourOwner, out _);
            bool sameVessel = yourOwner == null || agent.CurrentOwner == null || yourOwner == agent.CurrentOwner;

            blocker = kind switch
            {
                AnomalyKind.Spin => seen && dist <= 10f ? StartHold(kind, SpinSeconds) : "you are not watching it",
                AnomalyKind.PeekABoo => !seen && dist >= 4f ? StartPrank(false) : "you would see it climb in",
                AnomalyKind.ClosetAmbush => !seen && dist >= 4f ? StartPrank(true) : "you would see it climb in",
                AnomalyKind.Chatter => mode != BuddyMode.Follow ? "it is not following you"
                    : Near(dist, sameVessel) ?? Speak(AnomalyLines.Spoken(AnomalySeverity.Funny)),
                AnomalyKind.Whisper => Near(dist, sameVessel) ?? Speak(AnomalyLines.Spoken(AnomalyDirector.Ceiling)),
                AnomalyKind.FakeCommand => StartFakeCommand(),
                AnomalyKind.WrongName => StartWrongName(),
                AnomalyKind.Vanish => !seen && dist >= 6f ? StartVanish(kind, Random.Range(VanishMinSeconds, VanishMaxSeconds)) : "you could see it go",
                AnomalyKind.Noises => StartNoises(seen, dist, sameVessel),
                AnomalyKind.WindowStare => StartWindowStare(),
                AnomalyKind.WallStare => StartWallStare(),
                AnomalyKind.Bloody => StartBloody(kind, seen, dist),
                AnomalyKind.ShutDoors => StartShutDoors(you),
                AnomalyKind.Statue => StartStatue(kind, dist, sameVessel),
                AnomalyKind.Stalker => !seen ? StartStalker(dist, sameVessel) : "you are watching it",
                AnomalyKind.BehindYou => !seen && dist >= 6f ? StartVanish(kind, Random.Range(20f, 45f)) : "you could see it go",
                _ => "unknown",
            };
            if (blocker != null) return blocker;

            AnomalyDirector.Began(this, kind);
            AnomalyInfo info = Anomalies.Info(kind);
            anomalyLast = info.Name;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}: {info.Name} ({info.Severity.ToString().ToLowerInvariant()}, " +
                                        $"{dist:0.0}m from you{(seen ? ", in your view" : ", out of sight")})");
            return null;
        }

        private static string? Near(float dist, bool sameVessel) =>
            !sameVessel ? "you are on another vessel" : dist > 8f ? $"you are {dist:0.0}m away" : null;

        /// <summary>
        /// Starts a running one: the shared bookkeeping. `keepPlan` leaves a walk already committed alone.
        /// </summary>
        private string? StartHold(AnomalyKind kind, float seconds, bool keepPlan = false)
        {
            anomaly = kind;
            anomalyStep = 0;
            anomalyUntil = Time.time + seconds;
            anomalyStepAt = Time.time;
            anomalySeen = false;
            anomalySeenFor = 0f;
            anomalyUnseenSince = Time.time;
            anomalyStartled = false;
            // The yaw a spin starts from; a stare overwrites it with what it stares at.
            anomalyFace = new Vector3(0f, transform.eulerAngles.y, 0f);
            if (keepPlan) return null;

            agent.ClearMoveTarget();
            agent.ReleasePlan();
            return null;
        }

        private string? StartPrank(bool scary)
        {
            string? report = StartPrankHide(scary);
            if (report != null) return report;

            StartHold(scary ? AnomalyKind.ClosetAmbush : AnomalyKind.PeekABoo, PrankMaxSeconds + 30f, keepPlan: true);
            return null;
        }

        private string? StartFakeCommand()
        {
            if (Conversation == null) return "it has no talk window";

            if (NpcInteraction.IsOpenOn(Conversation)) return "the talk window is open on it";

            (string said, string reply) = AnomalyLines.Order(AnomalyDirector.Ceiling);
            NpcInteraction.AddLine(Conversation, said, true);
            NpcInteraction.AddLine(Conversation, reply, false);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s talk log now reads '$ {said}' / '> {reply}'");
            return null;
        }

        private string? StartWrongName()
        {
            if (TitleOverride != null) return "the wrong name is already waiting";

            if (Conversation != null && NpcInteraction.IsOpenOn(Conversation)) return "the talk window is open on it";

            wrongTitle = AnomalyLines.WrongName(Name);
            wrongTitleUntil = Time.time + WrongNameSeconds;
            return null;
        }

        private string? StartVanish(AnomalyKind kind, float seconds)
        {
            StartHold(kind, seconds);
            anomalyGiveUpAt = anomalyUntil + 60f;
            Disappear();
            return null;
        }

        private string? StartNoises(bool seen, float dist, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (seen) return "you are watching it";

            if (dist < 1.2f || dist > 7f) return $"it is {dist:0.0}m from you";

            if (!PlayerView.IsBehind(agent.GroundPos(1.1f))) return "it is not behind you";

            StartHold(AnomalyKind.Noises, 12f);
            anomalyStepAt = Time.time + 0.4f;
            return null;
        }

        private string? StartBloody(AnomalyKind kind, bool seen, float dist)
        {
            if (seen) return "you are watching it";

            if (dist < 3f) return "you are too close";

            string? gore = PutGoreOn();
            if (gore != null) return gore;

            StartHold(kind, BloodySeconds);
            return null;
        }

        private string? PutGoreOn()
        {
            if (suit.Suited) return "it wears a suit";

            goreWearing = BuddyGore.Apply(transform, Number, out goreTexture);
            return goreWearing == null ? "its body takes no skin" : null;
        }

        private void TakeGoreOff()
        {
            if (goreTexture == null) return;

            // A suit put on since drew over it; taking it off restored the original.
            if (!suit.Suited && BuddySkin.IsApplied(transform, goreTexture)) BuddyGore.Remove(transform, goreWearing);

            goreTexture = null;
            goreWearing = null;
        }

        private string? StartStatue(AnomalyKind kind, float dist, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (dist < 3f || dist > 30f) return $"it is {dist:0.0}m from you";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (mode == BuddyMode.Wander) SetMode(BuddyMode.Follow);

            StartHold(kind, Random.Range(50f, 90f));
            return null;
        }

        private string? StartStalker(float dist, bool sameVessel)
        {
            string? statue = StartStatue(AnomalyKind.Stalker, dist, sameVessel);
            if (statue != null) return statue;

            string? gore = PutGoreOn();
            if (gore == null) return null;

            anomaly = null;
            return gore;
        }

        // ------------------------------------------------------------------
        // Window and wall
        // ------------------------------------------------------------------

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

                // The pane itself: every window block of ship and station has a child 'Glass'.
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
        /// The nearest wall around it at chest height, walked to in a straight line.
        /// </summary>
        private string? StartWallStare()
        {
            Vector3 chest = agent.GroundPos(1.1f);
            Vector3 bestHit = Vector3.zero;
            Vector3 bestDir = Vector3.zero;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                int count = Physics.RaycastNonAlloc(chest, dir, WallHits, WallSearchDist, NavProbe.ProbeLayers,
                    QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    RaycastHit hit = WallHits[h];
                    if (!IsWall(hit.collider)) continue;

                    if (hit.distance >= bestDist) continue;

                    bestDist = hit.distance;
                    bestHit = hit.point;
                    bestDir = dir;
                }
            }
            if (bestDist > WallSearchDist) return $"no wall within {WallSearchDist:0}m";

            Vector3 floor = agent.FloorUnderNpc();
            Vector3 stand = bestHit - bestDir * WallStandOff;
            stand.y = floor.y;
            if (Vector3.Distance(stand, floor) > 0.3f && !NavProbe.WalkLos(floor, stand, 0.5f)) return "the wall cannot be walked up to";

            StartHold(AnomalyKind.WallStare, StareMaxSeconds + 60f);
            anomalyWalking = true;
            anomalyStand = stand;
            anomalyArrival = NpcAgent.ReachStandArrival;
            anomalyFace = new Vector3(bestHit.x, chest.y + 0.4f, bestHit.z);
            anomalyGiveUpAt = Time.time + 8f;
            return null;
        }

        /// <summary>
        /// Building, not a body or a loose item: what a wall stare may face.
        /// </summary>
        private static bool IsWall(Collider? collider)
        {
            if (collider == null) return false;

            Transform t = collider.transform;
            if (NpcPlayer.Controller is { } you && t.IsChildOf(you.transform)) return false;

            return t.GetComponentInParent<NpcAgent>() == null && t.GetComponentInParent<Grabbable>() == null;
        }

        /// <summary>
        /// The active node nearest `point` on the flat, between `min` and `max` from it.
        /// </summary>
        private static Vector3? NodeNear(Vector3 point, float min, float max)
        {
            Vector3? best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                if (Mathf.Abs(node.y - point.y) > 2.5f) continue;

                float d = FlatDistance(node, point);
                if (d < min || d > max || d >= bestDist) continue;

                best = node;
                bestDist = d;
            }
            return best;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Steer, while it walks to a window, a wall or through a door: the plan, then a straight stretch
        /// to the stand point.
        /// </summary>
        private Vector3 WalkAnomalyLeg(out bool wantMove)
        {
            wantMove = false;
            if (Time.time > anomalyGiveUpAt)
            {
                if (anomaly == AnomalyKind.ShutDoors) NextDoorOrEnd("that door took too long");
                else EndAnomaly("the walk there took too long");

                return Vector3.zero;
            }
            if (agent.HasPlanLeft)
            {
                agent.AdvancePlan();
                if (agent.HasPlanLeft) return agent.HeadAlongPlan(agent.WalkSpeed, out wantMove);

                agent.DropPlan();
            }
            Vector3 toStand = anomalyStand - transform.position;
            toStand.y = 0f;
            if (toStand.sqrMagnitude <= anomalyArrival * anomalyArrival)
            {
                anomalyWalking = false;
                agent.ClearMoveTarget();
                if (anomaly == AnomalyKind.ShutDoors)
                {
                    anomalyStepAt = Time.time + DoorWaitSeconds;
                    return Vector3.zero;
                }
                anomalyUntil = Time.time + Random.Range(StareMinSeconds, StareMaxSeconds);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stops and stares at the " +
                                            (anomaly == AnomalyKind.WindowStare ? "window" : "wall") +
                                            $" for {anomalyUntil - Time.time:0}s");
                return Vector3.zero;
            }
            wantMove = true;
            agent.SetMoveTarget(anomalyStand);
            return toStand.normalized * agent.WalkSpeed;
        }

        // ------------------------------------------------------------------
        // Doors
        // ------------------------------------------------------------------

        /// <summary>
        /// A round of the open room doors aboard, nearest next: it walks through each and the agent shuts
        /// it behind it (CloseBehind), clear of the doorway and of you. Airlocks, locked and password doors
        /// are left alone. npc-core:docs/invariants.md#close-only-what-you-walked-through
        /// </summary>
        private string? StartShutDoors(Transform you)
        {
            if (NpcVessels.FloorOwner(you.position) != FloorOwnership.PlayerShip) return "you are not aboard";

            if (!agent.IsAboardPlayerShip()) return "it is not aboard";

            if (!YourBuddyPlugin.ConfigAutoDoors.Value) return "it may not touch doors (AutoDoors)";

            doorsToShut.Clear();
            foreach (Gate gate in SceneScan.ThisFrame<Gate>())
            {
                if (gate == null || !gate.Opened || gate.Locked || !gate.gameObject.activeInHierarchy) continue;

                if (NpcVessels.OwnerOfTransform(gate.transform) != NavGraph.ShipOwner) continue;

                if (NpcDoors.IsAirlockGate(gate) || NpcDoors.IsPasswordGate(gate)) continue;

                doorsToShut.Add(gate);
            }
            if (doorsToShut.Count < 2) return $"only {doorsToShut.Count} door(s) open aboard";

            OrderDoorsNearestNext(transform.position);
            StartHold(AnomalyKind.ShutDoors, 2 * MaxDoors * DoorLegSeconds + 10f);
            anomalyStep = -1;
            doorsRetried = false;
            if (NextDoor()) return null;

            anomaly = null;
            return "no open door it can walk through";
        }

        /// <summary>
        /// Nearest to the buddy first, then nearest to the last, at most MaxDoors.
        /// </summary>
        private void OrderDoorsNearestNext(Vector3 from)
        {
            for (int i = 0; i < doorsToShut.Count; i++)
            {
                int best = i;
                for (int j = i + 1; j < doorsToShut.Count; j++)
                {
                    if (FlatDistance(doorsToShut[j].transform.position, from) < FlatDistance(doorsToShut[best].transform.position, from)) best = j;
                }
                (doorsToShut[i], doorsToShut[best]) = (doorsToShut[best], doorsToShut[i]);
                from = doorsToShut[i].transform.position;
            }
            if (doorsToShut.Count > MaxDoors) doorsToShut.RemoveRange(MaxDoors, doorsToShut.Count - MaxDoors);
        }

        /// <summary>
        /// The walk through the next door it can plan through. False when none is left.
        /// </summary>
        private bool NextDoor()
        {
            while (++anomalyStep < doorsToShut.Count)
            {
                Gate gate = doorsToShut[anomalyStep];
                if (gate == null || !gate.Opened) continue;

                if (FarSideOf(gate) is not { } beyond)
                {
                    SkipDoor(gate, "no clear walk through its doorway");
                    continue;
                }
                NavPath? plan = NavGraph.FindPath(agent.FloorUnderNpc(), beyond);
                if (plan is not { Count: > 0 })
                {
                    SkipDoor(gate, "no path to its far side");
                    continue;
                }
                if (!agent.CloseBehind(gate))
                {
                    SkipDoor(gate, "the agent will not close it");
                    continue;
                }

                agent.CommitPlan(plan.Value, false);
                anomalyWalking = true;
                anomalyStand = beyond;
                anomalyArrival = 0.6f;
                anomalyFace = gate.transform.position;
                anomalyGiveUpAt = Time.time + DoorLegSeconds;
                TraceDoors($"through '{gate.name}' ({anomalyStep + 1} of {doorsToShut.Count})");
                return true;
            }
            return false;
        }

        private void SkipDoor(Gate gate, string why) =>
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} leaves '{gate.name}' open - {why}");

        private void NextDoorOrEnd(string why)
        {
            agent.DropPlan();
            anomalyWalking = false;
            if (NextDoor()) return;

            // A second pass over what is still open: a door the agent had not shut by the time it moved on,
            // or one planned around the first time, from a different side now.
            if (!doorsRetried && DoorsShut() < doorsToShut.Count)
            {
                doorsRetried = true;
                doorsToShut.RemoveAll(g => g == null || !g.Opened);
                OrderDoorsNearestNext(transform.position);
                anomalyStep = -1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} goes round again for {doorsToShut.Count} door(s) still open");
                if (NextDoor()) return;
            }

            EndAnomaly($"{DoorsShut()} of {doorsToShut.Count} doors shut ({why})");
        }

        private int DoorsShut()
        {
            int shut = 0;
            foreach (Gate gate in doorsToShut)
            {
                if (gate != null && !gate.Opened) shut++;
            }
            return shut;
        }

        /// <summary>
        /// A ground node DoorPassDist beyond the doorway, on the side away from the buddy. The doorway's
        /// axis is whichever of the gate's own two that walks clear both ways from its middle.
        /// </summary>
        private Vector3? FarSideOf(Gate gate)
        {
            Vector3 middle = gate.transform.position;
            if (NavProbe.TryFloorHeight(middle + Vector3.up * 0.5f, out float floorY)) middle.y = floorY;

            Vector3 here = transform.position;
            // A narrow room behind the door may not leave 1.6 m clear: a shorter step is tried next.
            foreach (float pass in DoorPassDists)
            {
                foreach (Vector3 axis in new[] { gate.transform.forward, gate.transform.right })
                {
                    Vector3 flat = new(axis.x, 0f, axis.z);
                    if (flat.sqrMagnitude < 0.01f) continue;

                    flat.Normalize();
                    Vector3 a = middle + flat * pass;
                    Vector3 b = middle - flat * pass;
                    if (!NavProbe.WalkLos(middle, a, 0.5f) || !NavProbe.WalkLos(middle, b, 0.5f)) continue;

                    Vector3 far = FlatDistance(a, here) > FlatDistance(b, here) ? a : b;
                    Vector3? node = NodeNear(far, 0f, 1.5f);
                    return node != null && Vector3.Dot(node.Value - middle, far - middle) > 0f ? node : far;
                }
            }
            return null;
        }

        private void TraceDoors(string line)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} shutting doors: {line}");
        }

        // ------------------------------------------------------------------
        // Vanishing
        // ------------------------------------------------------------------

        private void Disappear()
        {
            vanishedRenderers.Clear();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;

                renderer.enabled = false;
                vanishedRenderers.Add(renderer);
            }
            cc.detectCollisions = false;
            agent.ClearMoveTarget();
            agent.DropPlan();
            Asleep = true;
            vanished = true;
        }

        /// <summary>
        /// Back where it is now: only the renderers it switched off, so the mask SpawnBuddy hid stays hidden.
        /// </summary>
        private void Reappear()
        {
            if (!vanished) return;

            foreach (Renderer renderer in vanishedRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }
            vanishedRenderers.Clear();
            if (cc != null) cc.detectCollisions = true;

            Asleep = false;
            vanished = false;
        }

        /// <summary>
        /// A node on your vessel, out of your sight, ReappearMinDist..MaxDist from you; null when none.
        /// </summary>
        private Vector3? HiddenSpotNear(Transform you)
        {
            NpcVessels.FloorOwner(you.position, out string? owner, out _);
            ReappearNodes.Clear();
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                if (owner != null && NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                if (Mathf.Abs(node.y - you.position.y) > 3f) continue;

                float d = FlatDistance(node, you.position);
                if (d < ReappearMinDist || d > ReappearMaxDist) continue;

                ReappearNodes.Add(node);
            }
            for (int tries = 0; tries < 12 && ReappearNodes.Count > 0; tries++)
            {
                int index = Random.Range(0, ReappearNodes.Count);
                Vector3 node = ReappearNodes[index];
                ReappearNodes.RemoveAt(index);
                if (!PlayerView.Sees(node + Vector3.up * 1.1f, null) && !PlayerView.Sees(node + Vector3.up * 0.3f, null)) return node;
            }
            return null;
        }

        /// <summary>
        /// The floor point a little behind the player, where they will not see it until they turn.
        /// </summary>
        private static Vector3? SpotBehind(Transform you)
        {
            Transform? cam = PlayerView.Camera();
            if (cam == null || NpcPlayer.Controller is not { } controller) return null;

            Vector3 back = -cam.forward;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) return null;

            float feetY = controller.transform.position.y + controller.center.y - controller.height * 0.5f;
            Vector3 yourFeet = new(you.position.x, feetY, you.position.z);
            foreach (float turn in BehindTurns)
            {
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * back.normalized;
                Vector3 spot = yourFeet + dir * BehindYouDist;
                if (!NavProbe.TryFloorHeight(spot + Vector3.up * 0.5f, out float floorY)) continue;

                spot.y = floorY;
                if (!NavProbe.WalkLos(yourFeet, spot, 0.4f)) continue;

                return spot;
            }
            return null;
        }

        private void MoveTo(Vector3 floor)
        {
            agent.TeleportTo(floor - Vector3.up * agent.OriginToFeet, true);
            NpcVessels.FloorOwner(floor, out string? owner, out Transform? anchor);
            if (owner != null && anchor != null) agent.RideOwner(owner, anchor);
        }

        // ------------------------------------------------------------------
        // Every frame
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!anomaly.HasValue) return;

            using NpcRegistry.ActingScope _ = NpcRegistry.Acting(agent);
            UpdateAnomaly();
        }

        private void UpdateAnomaly()
        {
            if (IsDead)
            {
                EndAnomaly("it died");
                return;
            }
            Player? player = PilotPlayer();
            if (player == null || player.Controller == null) return;

            Transform you = player.Controller.CachedTransform;
            float now = Time.time;
            if (now >= anomalySightAt)
            {
                anomalySightAt = now + SightSampleSeconds;
                bool seen = !vanished && PlayerView.SeesBody(transform, agent.FloorUnderNpc());
                if (seen) anomalySeenFor += SightSampleSeconds;
                // Watched, the blood's time runs out faster: as if it wanted it gone before you looked
                // too closely. docs/anomalies.md#bloody
                if (seen && anomaly == AnomalyKind.Bloody) anomalyUntil -= SightSampleSeconds * (BloodySeenRate - 1f);
                else if (anomalySeen) anomalyUnseenSince = now;

                anomalySeen = seen;
            }
            float dist = FlatDistance(you.position, transform.position);

            switch (anomaly)
            {
                case AnomalyKind.Spin:
                    if (now >= anomalyUntil) EndAnomaly("done");
                    break;
                case AnomalyKind.ShutDoors:
                    UpdateShutDoors(now);
                    break;
                case AnomalyKind.PeekABoo:
                case AnomalyKind.ClosetAmbush:
                    // Fear took the closet over, or the hide ended some other way: docs/invariants.md#fear-owns-the-buddy
                    if (!hidePrank) EndAnomaly(Hiding ? "it is hiding from the Breathless now" : "out of the closet");
                    break;
                case AnomalyKind.Noises:
                    UpdateNoises(now, dist);
                    break;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                    if (!anomalyWalking && now >= anomalyUntil) EndAnomaly("done staring");
                    break;
                case AnomalyKind.Bloody:
                    UpdateBloody(now);
                    break;
                case AnomalyKind.Statue:
                    if (anomalySeen && dist < 2.5f) Startle(15);
                    if (now >= anomalyUntil && !anomalySeen) EndAnomaly("done");
                    break;
                case AnomalyKind.Stalker:
                    UpdateStalker(now, dist);
                    break;
                case AnomalyKind.Vanish:
                case AnomalyKind.BehindYou:
                    UpdateVanished(now, you);
                    break;
            }
        }

        /// <summary>
        /// Standing past a door it walked through: on to the next once the agent has shut it, or after
        /// DoorWaitSeconds. The round as a whole ends at anomalyUntil.
        /// </summary>
        private void UpdateShutDoors(float now)
        {
            if (now >= anomalyUntil)
            {
                EndAnomaly($"{DoorsShut()} of {doorsToShut.Count} doors shut (out of time)");
                return;
            }
            if (anomalyWalking) return;

            Gate? gate = anomalyStep < doorsToShut.Count ? doorsToShut[anomalyStep] : null;
            if (gate != null && gate.Opened && now < anomalyStepAt) return;

            NextDoorOrEnd("the round is done");
        }

        private void UpdateNoises(float now, float dist)
        {
            if (anomalySeen)
            {
                EndAnomaly("you turned round");
                return;
            }
            if (now < anomalyStepAt) return;

            AnomalySeverity ceiling = AnomalyDirector.Ceiling;
            ScareSound sound = anomalyStep switch
            {
                0 => Random.value < 0.5f ? ScareSound.Click : ScareSound.Wet,
                1 => ceiling >= AnomalySeverity.Scary && Random.value < 0.5f ? ScareSound.Creature : ScareSound.Wet,
                _ => ceiling >= AnomalySeverity.Extreme && Random.value < 0.4f ? ScareSound.Shriek : ScareSound.Creature,
            };
            if (ScareSounds.Play(sound, agent.GroundPos(1.2f)) && sound >= ScareSound.Creature) Startle(sound == ScareSound.Shriek ? 25 : 8);

            anomalyStep++;
            anomalyStepAt = now + Random.Range(1.2f, 2.6f);
            int sounds = ceiling >= AnomalySeverity.Scary ? 3 : 2;
            if (anomalyStep >= sounds || dist > 9f) EndAnomaly("silent again");
        }

        private void UpdateBloody(float now)
        {
            if (goreTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            // Something in the world can rewrite the body's materials, as BuddySuit's watcher finds: put it back.
            if (!BuddySkin.IsApplied(transform, goreTexture))
            {
                BuddySkin.ApplyTexture(transform, goreTexture);
                YourBuddyPlugin.Log.LogWarning($"[anomaly] {Name}'s blood was lost (its materials changed) - applying it again");
            }
            // Never while you look at it: only once its time is up and you have not seen it for a while.
            if (now < anomalyUntil || anomalySeen || now - anomalyUnseenSince < BloodyUnseenSeconds) return;

            EndAnomaly(anomalySeenFor > 0f ? "out of your sight, clean again" : "nobody saw");
        }

        private void UpdateStalker(float now, float dist)
        {
            if (goreTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            if (anomalySeen && dist < 3f) Startle(25);

            if (!anomalySeen && now >= anomalyStepAt && dist < 6f && PlayerView.IsBehind(agent.GroundPos(1.1f)))
            {
                anomalyStepAt = now + Random.Range(6f, 10f);
                ScareSounds.Play(Random.value < 0.5f ? ScareSound.Wet : ScareSound.Creature, agent.GroundPos(1.2f));
            }
            if (now >= anomalyUntil && !anomalySeen) EndAnomaly("gone quiet");
        }

        /// <summary>
        /// Waits out its time unseen, then comes back where you will not see it arrive - for BehindYou,
        /// right at your back - and says so in the log while it cannot.
        /// </summary>
        private void UpdateVanished(float now, Transform you)
        {
            if (vanished)
            {
                if (now < anomalyUntil || now < anomalyStepAt) return;

                anomalyStepAt = now + 2f;
                bool behindYou = anomaly == AnomalyKind.BehindYou;
                Vector3? spot = behindYou ? SpotBehind(you) : HiddenSpotNear(you);
                if (spot == null && now < anomalyGiveUpAt)
                {
                    TraceAnomaly(behindYou ? "waiting for room behind you" : "waiting for a spot out of your sight");
                    return;
                }
                if (spot == null && PlayerView.SeesBody(transform, agent.FloorUnderNpc()) && now < anomalyGiveUpAt + 60f)
                {
                    TraceAnomaly("waiting for you to look away from where it vanished");
                    return;
                }
                if (spot != null) MoveTo(spot.Value);

                Reappear();
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is back, {FlatDistance(transform.position, you.position):0.0}m from you" +
                                            (spot == null ? ", where it vanished" : ""));
                if (!behindYou || spot == null)
                {
                    EndAnomaly(spot == null && behindYou ? "no room behind you" : "back");
                    return;
                }
                anomalyStep = 1;
                anomalyUntil = now + 15f;
                agent.FacePoint(you.position);
                ScareSounds.Play(ScareSound.Creature, agent.GroundPos(1.2f));
                Speak(AnomalyLines.Spoken(AnomalySeverity.Extreme));
                return;
            }
            // BehindYou, standing at your back: once you turn round, it holds a moment and lets go.
            if (anomalyStep == 1 && anomalySeen)
            {
                anomalyStep = 2;
                anomalyUntil = now + 1.5f;
                Startle(30);
            }
            if (now >= anomalyUntil) EndAnomaly(anomalyStep == 2 ? "you saw it" : "you never turned round");
        }

        // ------------------------------------------------------------------
        // The body, from OverrideMovement and Steer
        // ------------------------------------------------------------------

        /// <summary>
        /// OverrideMovement's turn: true while the anomaly holds the buddy still. Fear ends any but a vanish.
        /// docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private bool AnomalyHoldsBody()
        {
            if (!anomaly.HasValue) return false;

            if (fearState != FearState.Calm && !vanished && !AnomalyIsLooks)
            {
                EndAnomaly("the Breathless");
                return false;
            }
            switch (anomaly)
            {
                case AnomalyKind.Spin:
                    float turned = 1f - Mathf.Clamp01((anomalyUntil - Time.time) / SpinSeconds);
                    transform.rotation = Quaternion.Euler(0f, anomalyFace.y + turned * 720f, 0f);
                    return true;
                case AnomalyKind.Noises:
                case AnomalyKind.BehindYou:
                    return true;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                case AnomalyKind.ShutDoors:
                    return !anomalyWalking;
                case AnomalyKind.Statue:
                case AnomalyKind.Stalker:
                    if (!anomalySeen) return false;

                    agent.ClearMoveTarget();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// TryIdleFacing's turn, standing still: where the anomaly looks. False leaves the usual facing.
        /// The agent turns an idle body after the brain's override, so facing set there would be undone.
        /// </summary>
        private bool AnomalyFaces(Player player)
        {
            switch (anomaly)
            {
                case AnomalyKind.Spin:
                    return true;
                case AnomalyKind.Noises:
                case AnomalyKind.BehindYou:
                    agent.FacePlayer(player);
                    return true;
                case AnomalyKind.Statue:
                case AnomalyKind.Stalker:
                    if (!anomalySeen) return false;

                    agent.FacePlayer(player);
                    return true;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                case AnomalyKind.ShutDoors:
                    if (anomalyWalking) return false;

                    agent.FacePoint(anomalyFace);
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // Ending
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts back everything the anomaly changed - looks, collisions, AI, the closet - on every way out.
        /// docs/invariants.md#an-anomaly-puts-back-what-it-changed
        /// </summary>
        internal void EndAnomaly(string why)
        {
            if (!anomaly.HasValue) return;

            AnomalyKind kind = anomaly.Value;
            anomaly = null;
            Reappear();
            TakeGoreOff();
            if (anomalyWalking)
            {
                anomalyWalking = false;
                agent.DropPlan();
                agent.ClearMoveTarget();
            }
            doorsToShut.Clear();
            if (kind is AnomalyKind.PeekABoo or AnomalyKind.ClosetAmbush && hidePrank)
            {
                // With the Breathless about it stays in, hiding for real now: docs/invariants.md#fear-owns-the-buddy
                if (fearState != FearState.Calm)
                {
                    hidePrank = false;
                    hideFromFear = true;
                }
                else
                {
                    ForceLeaveHidingSpot("the prank is over - " + why);
                }
            }
            decideAt = 0f;
            anomalyLast = Anomalies.Info(kind).Name + " - " + why;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}: {Anomalies.Info(kind).Name} over - {why}");
        }

        /// <summary>
        /// An order reaching it: it ends what it was acting out, unless it is not listening.
        /// </summary>
        private void EndAnomalyForOrder(string order)
        {
            if (anomaly.HasValue && !AnomalyIgnoresOrders && !AnomalySurvivesOrders) EndAnomaly("you told me to " + order);
        }

        // ------------------------------------------------------------------
        // Saying things, and scaring you
        // ------------------------------------------------------------------

        /// <summary>
        /// A line said aloud: in NPC.Core's speech panel and its talk log, with the robot's blips as its
        /// voice. Null: it was said.
        /// </summary>
        private string? Speak(string text)
        {
            if (Conversation != null) NpcInteraction.Speak(Conversation, text);

            ScareSounds.Babble(transform, Mathf.Clamp(text.Length / 3, 4, 16));
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} says: \"{text}\"");
            return null;
        }

        /// <summary>
        /// Once per anomaly: your stress, through the director's own source.
        /// </summary>
        private void Startle(int stress)
        {
            if (anomalyStartled) return;

            anomalyStartled = true;
            AnomalyDirector.Startle(stress);
        }

        /// <summary>
        /// BuddyConversation.SetOpen: the window opened under the wrong name, once; it says nothing about it.
        /// </summary>
        internal void OnTalkOpened()
        {
            if (TitleOverride == null) return;

            wrongTitle = null;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s talk window opened under the wrong name");
        }

        private float anomalyTraceAt = 0f;

        private void TraceAnomaly(string line)
        {
            if (Time.time < anomalyTraceAt) return;

            anomalyTraceAt = Time.time + 15f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is still gone - {line}");
        }

        /// <summary>
        /// For the HUD and buddy_anomaly.
        /// </summary>
        internal string DescribeAnomaly()
        {
            string now = anomaly.HasValue
                ? Anomalies.Info(anomaly.Value).Name + (vanished ? ", gone" : anomalyWalking ? ", on the way" : "") +
                  $", {Mathf.Max(0f, anomalyUntil - Time.time):0}s"
                : "none";
            return now + (TitleOverride != null ? "; wrong name waiting" : "") + " (last: " + anomalyLast + ")";
        }
    }
}
