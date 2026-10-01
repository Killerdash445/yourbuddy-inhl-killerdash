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
        /// <summary>
        /// You saw what it acted out. One nobody saw may come again: docs/anomalies.md#once-per-save
        /// </summary>
        private bool anomalyWitnessed = false;
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
        /// Passes over the doors still open, and how many were shut when the last one began.
        /// </summary>
        private int doorPasses = 0;
        private int doorsShutAtPass = 0;
        /// <summary>
        /// Past this door it has already stepped further out of the doorway once.
        /// </summary>
        private bool doorSteppedClear = false;
        private static readonly float[] DoorPassDists = [DoorPassDist, 1.1f];

        // The wrong name in the talk window's title, until it is next opened.
        private string? wrongTitle = null;
        private float wrongTitleUntil = 0f;

        // Talking with the Shipyard's robot: whose turn, and the robot's line. docs/anomalies.md#bottalk
        private AssistanceBot? talkBot = null;
        /// <summary>
        /// The Shipyard a set piece put it on, and where it stood aboard before: an undock puts it back.
        /// </summary>
        private ShipyardStation? shipyard = null;
        private Vector3? fromAboard = null;
        // Meat: the cryo room, its doorways, the one it faces, and whether it ran. docs/anomalies.md#meat
        private Room? cryoRoom = null;
        private readonly List<EntryDetector> cryoDoorways = [];
        private EntryDetector? caughtDoorway = null;
        private bool caughtRan = false;
        // Pipe: the bloody pipe in its hands, and the frame it was made. docs/anomalies.md#pipe
        private Grabbable? pipe = null;
        private int pipeFrame = 0;
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
        // Smile and UnderTheSuit: the flicker's texture, what it wore, and the beat it is on. docs/anomalies.md#smile-and-underthesuit
        private Texture2D? flickerTexture = null;
        private Texture? flickerWearing = null;
        private bool flickerOn = false;
        private int flickerBeat = 0;
        // Sleeper: the copy in its capsule, the capsule, and when you last saw it. docs/anomalies.md#sleeper
        private GameObject? sleeper = null;
        private SleeperBed? sleeperBed = null;
        private Vector3 sleeperHead;
        private bool sleeperOpened = false;
        private float sleeperSeenAt = 0f;
        private bool sleeperActorFound = false;
        private bool botTalksNext = false;
        private float botBurstUntil = 0f;
        private float botBlipAt = 0f;

        private static readonly List<Vector3> ReappearNodes = [];
        private static readonly List<Vector3> RunOffNodes = [];
        private static readonly List<Gate> ShadowDoors = [];
        private static readonly List<Vector3> ShadowStarts = [];
        private static readonly RaycastHit[] WallHits = new RaycastHit[8];
        private static readonly float[] BehindTurns = [0f, 30f, -30f];

        /// <summary>
        /// The talk window's conversation with this buddy, set by SpawnBuddy.
        /// </summary>
        internal BuddyConversation? Conversation { get; set; }

        /// <summary>
        /// The animated body, set by SpawnBuddy: what a double copies. docs/anomalies.md#shadow
        /// </summary>
        internal GameObject? Model { get; set; }

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
        private const float BloodySeenRate = 15f;
        private const float BloodyUnseenSeconds = 5f;
        private const float BloodyRunRetrySeconds = 6f;
        private const float BehindYouDist = 1.2f;
        private const float DoorPassDist = 1.6f;
        private const float DoorClearDist = 1.8f;
        private const int MaxDoorPasses = 3;
        private const float DoorLegSeconds = 25f;
        private const float DoorWaitSeconds = 4f;
        private const int MaxDoors = 6;
        private const float WrongNameSeconds = 900f;
        private const float BotTalkMinDist = 12f;
        private const float BotTalkMaxDist = 45f;
        private const float BotStandMin = 1.2f;
        private const float BotStandMax = 2.2f;
        private const float BotTalkGlanceDist = 4f;
        private const float BotTalkCloseDist = 2.5f;
        private const float BotTalkWaitSeconds = 240f;
        private const float BotTalkGlanceSeconds = 1.2f;
        private const float BotBlipGap = 0.035f;
        private const float RunOffMinDist = 6f;
        private const float RunOffMaxDist = 20f;
        private const float RunOffGain = 4f;
        private const float RunOffLegSeconds = 20f;
        private const float RunOffUnseenSeconds = 3f;
        private const string CryoRoomName = "YardCryo";
        private const float CaughtMinDist = 8f;
        private const float CaughtMaxDist = 60f;
        private const float CaughtStandMin = 2f;
        private const float CaughtStandMax = 5.5f;
        private const float CaughtStandDist = 3f;
        private const float CaughtViewInset = 0.8f;
        private const float CaughtEnterDist = 3f;
        private const float CaughtMeatAhead = 0.7f;
        private const float MessProbeAbove = 0.05f;
        private const float MessLevelTolerance = 0.05f;
        private const float CaughtWaitSeconds = 480f;
        private const float CaughtNearDist = 3f;
        private const float CaughtTurnSeconds = 0.6f;
        private const float CaughtCorneredSeconds = 120f;
        private const float CaughtGoneUnseenSeconds = 1f;
        private const float CaughtRunMin = 2f;
        private const float CaughtRunGain = 1f;
        private const float PipeMinDist = 5f;
        private const float PipeMaxDist = 30f;
        private const float PipeGiveUpSeconds = 120f;
        private const float PipeStartleDist = 4f;
        private const float MoveWaitSeconds = 300f;
        private const float MoveStartleDist = 4f;
        private const float ShadowDoorMin = 6f;
        private const float ShadowDoorMax = 25f;
        private const float ShadowStartMin = 3f;
        private const float ShadowStartMax = 10f;
        private const float ShadowRouteMax = 25f;
        private const float ShadowPastDoor = 1.5f;
        private const float ShadowOpenDist = 1.2f;
        private const float ShadowSeenSeconds = 0.8f;
        private const float ShadowNearDist = 4f;
        private const float ShadowRunSeconds = 30f;
        private const float ShadowWaitSeconds = 120f;
        private const float FlickerWaitSeconds = 240f;
        private const float FlickerMinDist = 1.5f;
        private const float FlickerMaxDist = 7f;
        private const float FlickerLookAngle = 25f;
        private const float FlickerFacingAngle = 50f;
        private const float SleeperMinDist = 8f;
        private const float SleeperWithYouDist = 25f;
        private const float SleeperWaitSeconds = 900f;
        private const float SleeperOpenDist = 2.5f;
        private const float SleeperOpenAngle = 60f;
        private const float SleeperSeeDist = 6f;
        private const float SleeperLookAngle = 45f;
        private const float SleeperGoneUnseenSeconds = 1f;
        private const float SleeperStareSeconds = 30f;
        /// <summary>
        /// The flickers' beats, on and off in turn, in seconds.
        /// </summary>
        private static readonly float[] SmileBeats = [0.2f, 0.12f, 0.12f];
        private static readonly float[] FleshBeats = [0.12f];
        /// <summary>
        /// The pipe's hold, from the body: its bloody top end down and forward, a little to one side.
        /// </summary>
        private static readonly Quaternion PipeHold = Quaternion.Euler(150f, 0f, 20f);

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
        internal bool IgnoresYou => vanished || anomaly is AnomalyKind.Statue or AnomalyKind.Stalker or AnomalyKind.BehindYou or
            AnomalyKind.BotTalk or AnomalyKind.Bloody or AnomalyKind.Meat or AnomalyKind.Pipe;

        /// <summary>
        /// Orders do not end these: it is not listening. docs/anomalies.md#2-the-anomalies
        /// </summary>
        private bool AnomalyIgnoresOrders => anomaly is AnomalyKind.Vanish or AnomalyKind.Stalker or AnomalyKind.BehindYou or
            AnomalyKind.Bloody or AnomalyKind.Meat or AnomalyKind.Pipe;

        /// <summary>
        /// Its looks, or a set piece somewhere else: the decider, orders, fear and bad air go on around it.
        /// docs/anomalies.md#2-the-anomalies
        /// </summary>
        private bool AnomalyInBackground => anomaly is AnomalyKind.Bloody or AnomalyKind.Shadow or AnomalyKind.Smile or
            AnomalyKind.UnderTheSuit or AnomalyKind.Sleeper;

        /// <summary>
        /// A task does not end these: a spin finishes first, blood stays on, the rest go on elsewhere.
        /// </summary>
        private bool AnomalySurvivesOrders => anomaly == AnomalyKind.Spin || AnomalyInBackground;

        /// <summary>
        /// The title the talk window shows instead of its name, once. docs/anomalies.md#wrongname
        /// </summary>
        internal string? TitleOverride => wrongTitle != null && Time.time < wrongTitleUntil ? wrongTitle : null;

        // ------------------------------------------------------------------
        // Starting
        // ------------------------------------------------------------------

        /// <summary>
        /// Why this buddy cannot act out anything now, or null. `outsideOk`: the shadow, which runs inside
        /// while the buddy may be out in space with you, or on its way through an airlock.
        /// </summary>
        internal string? AnomalyReady(bool outsideOk = false)
        {
            if (IsDead) return "dead";

            if (Asleep || !gameObject.activeInHierarchy) return "not awake here";

            if (anomaly.HasValue) return "already acting one out";

            if (agent.IsOutside && !outsideOk) return "outside";

            if (agent.IsBeingCaught) return "being caught";

            if (InDialog) return "being talked to";

            if (Hiding) return "hiding";
            // docs/invariants.md#fear-owns-the-buddy
            if (fearState != FearState.Calm || mode == BuddyMode.Flee) return "afraid";

            if (reachTask != null) return "busy " + DescribeReachTask();

            if (mode == BuddyMode.Route) return "walking to a node you sent it to";

            return suit.RunActive && !outsideOk ? "on an airlock run" : null;
        }

        /// <summary>
        /// Starts one now, or says why not. The director has already judged the severity; buddy_anomaly
        /// skips that and the chance. docs/anomalies.md
        /// </summary>
        internal string? TryStartAnomaly(AnomalyKind kind)
        {
            // A buddy left on a station is parked: only Move wakes it. docs/anomalies.md#move
            string? blocker = kind == AnomalyKind.Move ? MoveReady() : AnomalyReady(outsideOk: kind == AnomalyKind.Shadow);
            if (blocker != null) return blocker;

            Player? player = PilotPlayer();
            if (player == null || player.Controller == null) return "no player";

            // Out in space only the shadow plays, seen through a station's windows. docs/anomalies.md#shadow
            bool youOutside = NpcAgent.IsPlayerInSpace(player);
            if (youOutside && kind != AnomalyKind.Shadow) return "you are outside";

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
                AnomalyKind.Whisper => Near(dist, sameVessel) ?? SpeakNew(AnomalyLines.Spoken(AnomalyDirector.Ceiling)),
                AnomalyKind.FakeCommand => StartFakeCommand(),
                AnomalyKind.WrongName => StartWrongName(),
                AnomalyKind.Vanish => !seen && dist >= 6f ? StartVanish(kind, Random.Range(VanishMinSeconds, VanishMaxSeconds)) : "you could see it go",
                AnomalyKind.Noises => StartNoises(seen, dist, sameVessel),
                AnomalyKind.WindowStare => StartWindowStare(),
                AnomalyKind.WallStare => StartWallStare(),
                AnomalyKind.BotTalk => StartBotTalk(you, seen),
                AnomalyKind.Bloody => StartBloody(kind, seen, dist),
                AnomalyKind.ShutDoors => StartShutDoors(you),
                AnomalyKind.Statue => StartStatue(kind, dist, sameVessel),
                AnomalyKind.Meat => StartCaught(you, seen),
                AnomalyKind.Pipe => !seen ? StartPipe(dist, sameVessel) : "you are watching it",
                AnomalyKind.Move => StartMove(you),
                AnomalyKind.Shadow => StartShadow(you, youOutside),
                AnomalyKind.Smile or AnomalyKind.UnderTheSuit => StartFlicker(kind, sameVessel),
                AnomalyKind.Sleeper => StartSleeper(you, dist, sameVessel),
                AnomalyKind.Stalker => !seen ? StartStalker(dist, sameVessel) : "you are watching it",
                AnomalyKind.BehindYou => !seen && dist >= 6f ? StartVanish(kind, Random.Range(20f, 45f)) : "you could see it go",
                _ => "unknown",
            };
            if (blocker != null) return blocker;

            // It is somewhere else now.
            if (kind == AnomalyKind.Move) dist = FlatDistance(you.position, transform.position);

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
            anomalyWitnessed = false;
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

            if (AnomalyLines.Order(AnomalyDirector.Ceiling) is not { } order) return "every order it could be told was told already";

            (string said, string reply) = order;
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
            if (wrongTitle == null) return "every wrong name was used already";

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
            if (gore == null)
            {
                // Your door codes do not stop it: docs/anomalies.md#2-the-anomalies
                agent.KnowsEveryCode = true;
                return null;
            }
            anomaly = null;
            return gore;
        }

        // ------------------------------------------------------------------
        // The bloody pipe
        // ------------------------------------------------------------------

        /// <summary>
        /// Out of your sight a bloody pipe is put in its hands; it comes up to you with it, not answering, and
        /// the moment you spot it, puts it down without a word. docs/anomalies.md#pipe
        /// </summary>
        private string? StartPipe(float dist, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (dist < PipeMinDist || dist > PipeMaxDist) return $"it is {dist:0.0}m from you - it needs {PipeMinDist:0}-{PipeMaxDist:0}m";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (agent.Hands.Item != null) return "its hands are full";

            Vector3 hold = agent.Hands.Point;
            Transform? parent = agent.ItemParentAt(hold);
            if (parent == null) return "no room here to keep a pipe in";

            Grabbable? made = AnomalyProps.SpawnPipe(parent, hold, transform.rotation * PipeHold);
            if (made == null) return "no pipe to hold - see the warning above";

            if (mode == BuddyMode.Wander) SetMode(BuddyMode.Follow);

            StartHold(AnomalyKind.Pipe, PipeGiveUpSeconds);
            pipe = made;
            // Picked up next frame: the item's Start puts it back where its data says.
            pipeFrame = Time.frameCount;
            return null;
        }

        /// <summary>
        /// Takes the pipe up and comes to you as a Follow does; the moment you see it, it puts the pipe down.
        /// Never seen in PipeGiveUpSeconds: down unseen.
        /// </summary>
        private void UpdatePipe(float now, float dist)
        {
            if (anomalyStep == 0)
            {
                if (Time.frameCount <= pipeFrame) return;

                if (pipe == null || !agent.Hands.PickUp(pipe))
                {
                    EndAnomaly("it could not take the pipe up");
                    return;
                }
                agent.Hands.Turn(transform.rotation * PipeHold);
                anomalyStep = 1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} comes to you with a bloody pipe, {dist:0.0}m away");
                return;
            }
            if (pipe == null || agent.Hands.Item != pipe)
            {
                EndAnomaly("the pipe is out of its hands");
                return;
            }
            if (!anomalySeen && now < anomalyUntil) return;

            if (anomalySeen && dist < PipeStartleDist) Startle(15);

            agent.Hands.Drop(anomalySeen ? "you saw it" : "nobody saw");
            pipe = null;

            EndAnomaly(anomalySeen ? $"you spotted it {dist:0.0}m away - it put the pipe down" : "put the pipe down unseen");
        }

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

            for (int i = 0; i < ShadowDoors.Count; i++)
            {
                int pick = Random.Range(i, ShadowDoors.Count);
                (ShadowDoors[i], ShadowDoors[pick]) = (ShadowDoors[pick], ShadowDoors[i]);
                if (PlanShadow(ShadowDoors[i], you.position, yours, frame)) return null;
            }
            return "no way to that door from out of your sight";
        }

        /// <summary>
        /// A ground node out of your sight on your side of `gate`, and a path from it through the doorway;
        /// the double is made there and the run begins. False when there is none.
        /// </summary>
        private bool PlanShadow(Gate gate, Vector3 yourPos, string owner, Transform frame)
        {
            Vector3 middle = gate.transform.position;
            if (NavProbe.TryFloorHeight(middle + Vector3.up * 0.5f, out float floorY)) middle.y = floorY;

            if (FarSideOf(gate, yourPos) is not { } farSide) return false;

            // A node marker hovers over its deck: npc-core:docs/invariants.md#node-hover-not-height
            Vector3 beyond = OnFloor(farSide);
            Vector3 ahead = beyond - middle;
            ahead.y = 0f;
            ahead.Normalize();
            ShadowStarts.Clear();
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground || NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                float d = FlatDistance(node, middle);
                if (d < ShadowStartMin || d > ShadowStartMax || Mathf.Abs(node.y - middle.y) > 1.5f) continue;

                // On your side, a step back from the doorway, and not on top of you.
                if (Vector3.Dot(node - middle, ahead) > -1f || FlatDistance(node, yourPos) < ShadowNearDist + 2f) continue;

                if (ShadowSees(node + Vector3.up * 1.1f) || ShadowSees(node + Vector3.up * 0.3f)) continue;

                ShadowStarts.Add(node);
            }
            for (int tries = 0; tries < 8 && ShadowStarts.Count > 0; tries++)
            {
                int index = Random.Range(0, ShadowStarts.Count);
                Vector3 start = OnFloor(ShadowStarts[index]);
                ShadowStarts.RemoveAt(index);
                NavPath? plan = NavGraph.FindPath(start, beyond);
                if (plan is not { Count: > 0 } path) continue;

                // Through this doorway, not round by another, and not far.
                float length = 0f;
                float nearest = float.MaxValue;
                Vector3 last = start;
                for (int w = 0; w < path.Count; w++)
                {
                    length += Vector3.Distance(last, path[w]);
                    nearest = Mathf.Min(nearest, FlatDistance(path[w], middle));
                    last = path[w];
                }
                if (length > ShadowRouteMax || nearest > 1.2f) continue;

                return MakeShadow(gate, middle, ahead, start, path, beyond, frame);
            }
            return false;
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

        // ------------------------------------------------------------------
        // Skin flickers
        // ------------------------------------------------------------------

        /// <summary>
        /// Armed: the next time you look it in the face, close, the skin flickers for a blink - a bloody grin
        /// on the visor, or flesh with a face behind the glass. docs/anomalies.md#smile-and-underthesuit
        /// </summary>
        private string? StartFlicker(AnomalyKind kind, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (suit.Suited) return "it wears a suit";

            Texture2D? texture = BuddyGore.Overlaid(transform, kind == AnomalyKind.Smile ? "FlickerSmile.png" : "FlickerFlesh.png", out Texture? wearing);
            if (texture == null) return "its body takes no skin";

            StartHold(kind, FlickerWaitSeconds, keepPlan: true);
            flickerTexture = texture;
            flickerWearing = wearing;
            flickerBeat = -1;
            return null;
        }

        private void UpdateFlicker(float now, float dist)
        {
            if (flickerTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            if (flickerBeat < 0)
            {
                if (now >= anomalyUntil)
                {
                    EndAnomaly("you never looked it in the face");
                    return;
                }
                if (!FacesYouClose(dist)) return;

                flickerBeat = 0;
                anomalyStepAt = now;
                anomalyWitnessed = true;
                Startle(anomaly == AnomalyKind.Smile ? 10 : 15);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s skin flickers, {dist:0.0}m from you");
            }
            if (now < anomalyStepAt) return;

            float[] beats = anomaly == AnomalyKind.Smile ? SmileBeats : FleshBeats;
            if (flickerBeat >= beats.Length)
            {
                EndAnomaly("a blink");
                return;
            }
            ShowFlicker(flickerBeat % 2 == 0);
            anomalyStepAt = now + beats[flickerBeat];
            flickerBeat++;
        }

        /// <summary>
        /// You look straight at its head, FlickerMinDist..MaxDist away, and it faces you.
        /// </summary>
        private bool FacesYouClose(float dist)
        {
            if (dist < FlickerMinDist || dist > FlickerMaxDist) return false;

            Transform? cam = PlayerView.Camera();
            Vector3 head = agent.GroundPos(1.6f);
            if (cam == null || PlayerView.AngleTo(head) > FlickerLookAngle || !PlayerView.Sees(head, transform)) return false;

            Vector3 toYou = cam.position - transform.position;
            toYou.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, toYou) <= FlickerFacingAngle;
        }

        private void ShowFlicker(bool on)
        {
            if (on && flickerTexture != null)
            {
                BuddySkin.ApplyTexture(transform, flickerTexture);
                flickerOn = true;
                return;
            }
            if (!flickerOn) return;

            BuddyGore.Remove(transform, flickerWearing);
            flickerOn = false;
        }

        // ------------------------------------------------------------------
        // The second sleeper
        // ------------------------------------------------------------------

        /// <summary>
        /// While it follows you aboard, the capsule it woke in is shut again, its monitor showing a pulse, and a
        /// copy of it sleeps inside. The door is opaque: it opens by itself when you come up to it.
        /// docs/anomalies.md#sleeper
        /// </summary>
        private string? StartSleeper(Transform you, float dist, bool sameVessel)
        {
            if (NpcVessels.FloorOwner(you.position) != FloorOwnership.PlayerShip) return "you are not aboard";

            if (!sameVessel || dist > SleeperWithYouDist) return "it is not with you";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (suit.Suited) return "it wears a suit";

            if (Model == null) return "it has no body to copy";

            if (BuddyCryoSpawn.OpenedBed(out string why) is not { } bed) return why;

            float far = FlatDistance(bed.Position, you.position);
            if (far < SleeperMinDist) return $"you are {far:0.0}m from its capsule - it needs you {SleeperMinDist:0}m away";

            if (FlatDistance(bed.Position, transform.position) < SleeperMinDist / 2f) return "it is by its capsule itself";

            if (PlayerView.Sees(bed.Position + Vector3.up * 1.2f, bed.Capsule)) return "you can see its capsule";

            GameObject? copy = BuddyDouble.Make(Model, transform, bed.Capsule, bed.Position, bed.Rotation, shadow: false, "YB_Sleeper");
            if (copy == null) return "it has no body to copy";

            BuddyDouble.Walk(copy, 0f);
            BuddyCryoSpawn.ShutWithSleeper(bed);
            StartHold(AnomalyKind.Sleeper, SleeperWaitSeconds, keepPlan: true);
            sleeper = copy;
            sleeperBed = bed;
            sleeperOpened = false;
            sleeperActorFound = false;
            sleeperHead = bed.Capsule.InverseTransformPoint(HeadOf(copy, bed.Position));
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is asleep in '{bed.Capsule.name}' again, {far:0.0}m from you - and it follows you " +
                                        $"(its head {HeadOf(copy, bed.Position).y - bed.Position.y:0.00}m over the bed point)");
            return null;
        }

        /// <summary>
        /// The top of the copy's drawn body, a little down: its head. The bed point plus 1.5 m when nothing draws.
        /// </summary>
        private static Vector3 HeadOf(GameObject copy, Vector3 fallback)
        {
            bool any = false;
            Bounds all = default;
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;

                if (any) all.Encapsulate(renderer.bounds);
                else all = renderer.bounds;
                any = true;
            }
            return any ? new Vector3(all.center.x, all.max.y - 0.2f, all.center.z) : fallback + Vector3.up * 1.5f;
        }

        /// <summary>
        /// You look at its head, this near: the capsule is the sight test's target, so its own walls never block.
        /// </summary>
        private bool LooksAtSleeper(float near, float angle)
        {
            if (sleeperBed is not { } bed || bed.Capsule == null) return false;

            Transform? cam = PlayerView.Camera();
            Vector3 head = bed.Capsule.TransformPoint(sleeperHead);
            return cam != null && FlatDistance(cam.position, head) <= near && PlayerView.AngleTo(head) <= angle && PlayerView.Sees(head, bed.Capsule);
        }

        /// <summary>
        /// Shut until you come up to it; then it opens, and there it is. Found: your stress, and the one following
        /// you stands still facing you until you turn to it. Once you look away from the capsule it is empty.
        /// </summary>
        private void UpdateSleeper(float now, float dist)
        {
            if (sleeperBed is not { } bed || bed.Capsule == null)
            {
                EndAnomaly("the capsule went away");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    if (LooksAtSleeper(SleeperOpenDist, SleeperOpenAngle))
                    {
                        anomalyStep = 1;
                        BuddyCryoSpawn.OpenForSleeper(bed, () => sleeperOpened = true);
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s capsule opens in front of you");
                        return;
                    }
                    if (now >= anomalyUntil) EndAnomaly("you never came to its capsule");

                    break;
                case 1:
                    if (!sleeperOpened) return;

                    anomalyStep = 2;
                    anomalyWitnessed = true;
                    anomalyUntil = now + SleeperStareSeconds;
                    sleeperSeenAt = now;
                    Startle(30);
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] You found {Name} asleep in its capsule - the other one is {dist:0.0}m from you");
                    break;
                case 2:
                    if (sleeper != null && LooksAtSleeper(SleeperSeeDist, SleeperLookAngle)) sleeperSeenAt = now;

                    if (sleeper != null && now - sleeperSeenAt >= SleeperGoneUnseenSeconds)
                    {
                        RemoveSleeper();
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s capsule is empty again");
                    }
                    if (anomalySeen && !sleeperActorFound)
                    {
                        sleeperActorFound = true;
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] You turn to {Name} - it was standing there, facing you");
                    }
                    if (sleeper == null && (sleeperActorFound || now >= anomalyUntil)) EndAnomaly(sleeperActorFound ? "you saw both" : "it stopped waiting");

                    break;
            }
        }

        /// <summary>
        /// The copy gone and the capsule open and dark, as the buddy left it.
        /// </summary>
        private void RemoveSleeper()
        {
            if (sleeper != null) Destroy(sleeper);

            sleeper = null;
            if (sleeperBed is { } bed) BuddyCryoSpawn.OpenEmpty(bed);

            sleeperBed = null;
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
                else if (anomaly == AnomalyKind.Bloody) StopBloodyRun();
                else if (anomaly == AnomalyKind.Meat) StopCaughtRun();
                else EndAnomaly("the walk there took too long");

                return Vector3.zero;
            }
            // Running from you after the robot; a walk otherwise.
            float speed = anomaly is AnomalyKind.BotTalk or AnomalyKind.Bloody or AnomalyKind.Meat
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
                if (anomaly == AnomalyKind.ShutDoors)
                {
                    anomalyStepAt = Time.time + DoorWaitSeconds;
                    return Vector3.zero;
                }
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
                                            (anomaly == AnomalyKind.WindowStare ? "window" : "wall") +
                                            $" for {anomalyUntil - Time.time:0}s");
                return Vector3.zero;
            }
            wantMove = true;
            agent.SetMoveTarget(anomalyStand);
            return toStand.normalized * speed;
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
            doorPasses = 1;
            doorsShutAtPass = 0;
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

                if (FarSideOf(gate, transform.position) is not { } beyond)
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
                doorSteppedClear = false;
                anomalyFace = gate.transform.position;
                anomalyGiveUpAt = Time.time + DoorLegSeconds;
                TraceDoors($"through '{gate.name}' ({anomalyStep + 1} of {doorsToShut.Count})");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Still in reach of the door it waits to see shut: a straight step further out, once.
        /// </summary>
        private void StepClearOf(Gate gate, float now)
        {
            doorSteppedClear = true;
            Vector3 away = transform.position - gate.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;

            Vector3 floor = agent.FloorUnderNpc();
            Vector3 clear = gate.transform.position + away.normalized * (DoorClearDist + 0.4f);
            clear.y = floor.y;
            if (!NavProbe.WalkLos(floor, clear, 0.5f))
            {
                TraceDoors($"in reach of '{gate.name}' and no room to step clear");
                return;
            }
            anomalyWalking = true;
            anomalyStand = clear;
            anomalyArrival = 0.3f;
            anomalyGiveUpAt = now + 4f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} steps clear of '{gate.name}' so it can shut");
        }

        private void SkipDoor(Gate gate, string why) =>
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} leaves '{gate.name}' open - {why}");

        private void NextDoorOrEnd(string why)
        {
            agent.DropPlan();
            anomalyWalking = false;
            if (NextDoor()) return;

            // Another pass over what is still open - a door the agent had not shut by the time it moved on,
            // or one planned around, from a different side now - while a pass still shuts something.
            int shut = DoorsShut();
            if (shut < doorsToShut.Count && doorPasses < MaxDoorPasses && (doorPasses == 1 || shut > doorsShutAtPass))
            {
                doorPasses++;
                doorsToShut.RemoveAll(g => g == null || !g.Opened);
                doorsShutAtPass = 0;
                OrderDoorsNearestNext(transform.position);
                anomalyStep = -1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} goes round again for {doorsToShut.Count} door(s) still open: {OpenDoorNames()}");
                if (NextDoor()) return;
            }
            string open = OpenDoorNames();
            EndAnomaly($"{DoorsShut()} of {doorsToShut.Count} doors shut ({why})" + (open.Length > 0 ? " - left open: " + open : ""));
        }

        private string OpenDoorNames()
        {
            string names = "";
            foreach (Gate gate in doorsToShut)
            {
                if (gate != null && gate.Opened) names += (names.Length > 0 ? ", " : "") + "'" + gate.name + "'";
            }
            return names;
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
        /// A ground node DoorPassDist beyond the doorway, on the side away from `here`. The doorway's
        /// axis is whichever of the gate's own two that walks clear both ways from its middle.
        /// </summary>
        private static Vector3? FarSideOf(Gate gate, Vector3 here)
        {
            Vector3 middle = gate.transform.position;
            if (NavProbe.TryFloorHeight(middle + Vector3.up * 0.5f, out float floorY)) middle.y = floorY;

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
                    Vector3 dir = (far - middle).normalized;
                    // Clear of the doorway: the agent never closes a door on itself, and the anomaly holds it
                    // still past the door. npc-core:docs/invariants.md#never-force-a-close-into-the-npc
                    Vector3? node = NodeNear(far, 0f, 1.5f);
                    if (node != null && Vector3.Dot(node.Value - middle, dir) > 0f && FlatDistance(node.Value, middle) >= DoorClearDist) return node;

                    Vector3 clear = middle + dir * DoorClearDist;
                    return NavProbe.WalkLos(middle, clear, 0.5f) ? clear : far;
                }
            }
            return null;
        }

        private void TraceDoors(string line)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} shutting doors: {line}");
        }

        // ------------------------------------------------------------------
        // The Shipyard
        // ------------------------------------------------------------------

        /// <summary>
        /// The Shipyard, with your ship docked at it; else null and why not. Each set piece then checks
        /// how far you are from where it plays.
        /// </summary>
        private static ShipyardStation? DockedShipyard(out string why)
        {
            why = "";
            ShipyardStation? station = Object.FindObjectOfType<ShipyardStation>();
            GameManager gm = GameManager.Instance;
            if (station != null && gm != null && station.Docker != null && station.Docker.DockedShip == gm.PlayerShip) return station;

            why = "it plays only at the Shipyard - dock there first";
            return null;
        }

        /// <summary>
        /// Undocked while it is on the Shipyard for a set piece: back where it stood aboard, never left
        /// behind, and the set piece ends. True when it did.
        /// </summary>
        private bool EndIfUndocked()
        {
            GameManager gm = GameManager.Instance;
            if (shipyard != null && gm != null && shipyard.Docker.DockedShip == gm.PlayerShip) return false;

            if (fromAboard != null && !agent.IsAboardPlayerShip()) MoveTo(fromAboard.Value);

            EndAnomaly("you undocked");
            return true;
        }

        // ------------------------------------------------------------------
        // Caught in the cryo room
        // ------------------------------------------------------------------

        /// <summary>
        /// Out of your sight, it is put in the Shipyard's cryo room, bloody, back to the shut door and in view
        /// of it, over raw meat and blood it leaves there. docs/anomalies.md#meat
        /// </summary>
        private string? StartCaught(Transform you, bool seen)
        {
            if (seen) return "you are watching it";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (suit.Suited) return "it wears a suit";

            ShipyardStation? station = DockedShipyard(out string why);
            if (station == null) return why;

            Room? cryo = CryoRoom(cryoDoorways);
            if (cryo == null) return "the Shipyard's cryo room (YardCryo) was not found";

            if (cryoDoorways.Count == 0) return "the cryo room's door was not found";

            if (!Items.Loadable(cryo.ContentParent)) return "the cryo room cannot be loaded";

            // The doorway you will come through: the one nearest you.
            EntryDetector doorway = cryoDoorways[0];
            foreach (EntryDetector d in cryoDoorways)
            {
                if (FlatDistance(you.position, d.transform.position) < FlatDistance(you.position, doorway.transform.position)) doorway = d;
            }
            // CryoRoom keeps only doorways with a door.
            Gate door = NpcDoors.DoorOf(doorway)!;
            float far = FlatDistance(you.position, door.transform.position);
            if (far < CaughtMinDist || far > CaughtMaxDist)
            {
                return $"you are {far:0.0}m from the cryo room's door - it needs you {CaughtMinDist:0}-{CaughtMaxDist:0}m away, outside that room";
            }
            foreach (EntryDetector d in cryoDoorways)
            {
                if (NpcDoors.DoorOf(d) is { Opened: true } open && PlayerView.Sees(open.transform.position + Vector3.up * 1.2f, null))
                {
                    return "you can see the cryo room's open door - look away or step out of sight of it";
                }
            }

            agent.LoadRoom(cryo, "it hides in the cryo room");
            if (!cryo.ContentEnabled) return "the cryo room will not load";

            Vector3? view = DoorwayView(doorway, cryo, door);
            if (view == null) return $"cannot tell which side of '{door.name}' the cryo room is";

            Vector3? stand = CaughtStand(doorway, cryo, view.Value);
            if (stand == null) return $"no spot in the cryo room in view of '{door.name}' and out of yours";

            string? gore = PutGoreOn();
            if (gore != null) return gore;

            // Shut, so that you are the one to open it.
            foreach (EntryDetector d in cryoDoorways)
            {
                if (NpcDoors.DoorOf(d) is not { Opened: true } shut) continue;

                shut.Close();
                NpcDoors.NoteClosedBy(shut, agent);
            }
            StartHold(AnomalyKind.Meat, CaughtWaitSeconds);
            fromAboard = agent.IsAboardPlayerShip() ? agent.FloorUnderNpc() : null;
            MoveTo(stand.Value);
            shipyard = station;
            cryoRoom = cryo;
            caughtDoorway = doorway;
            caughtRan = false;

            Vector3 away = stand.Value - view.Value;
            away.y = 0f;
            away.Normalize();
            Vector3 feet = stand.Value;
            if (NavProbe.TryFloorHeight(feet + Vector3.up * MessProbeAbove, out float feetY)) feet.y = feetY;
            Vector3 meat = MeatSpot(feet, away);
            anomalyFace = meat;
            anomalyStepAt = Time.time + 1f;
            LeaveTheMess(cryo.ContentParent, feet, meat, away);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} crouches over something in the cryo room, " +
                                        $"{FlatDistance(stand.Value, view.Value):0.0}m in from '{door.name}', {FlatDistance(stand.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// The Shipyard's cryo room, and into `doorways` the doorways with a door that join it. They come from
        /// the doorway detectors, as NPC.Core loads rooms by them: the room's own door list can leave one out.
        /// </summary>
        private static Room? CryoRoom(List<EntryDetector> doorways)
        {
            doorways.Clear();
            Room? cryo = null;
            foreach (Room room in Object.FindObjectsOfType<Room>(true))
            {
                if (room != null && room.name == CryoRoomName) cryo = room;
            }
            if (cryo == null) return null;

            foreach (EntryDetector detector in NpcDoors.Detectors)
            {
                if (detector == null || NpcDoors.DoorOf(detector) == null) continue;

                NpcDoors.RoomsOf(detector, out Room? inner, out Room? outer);
                if (inner == cryo || outer == cryo) doorways.Add(detector);
            }
            return cryo;
        }

        /// <summary>
        /// On the cryo room's side of this doorway, by the test the game uses for you.
        /// </summary>
        private static bool InCryo(EntryDetector doorway, Room cryo, Vector3 point)
        {
            NpcDoors.RoomsOf(doorway, out Room? inner, out _);
            return NpcDoors.TryInnerSide(doorway, point, out bool isInner) && isInner == (inner == cryo);
        }

        /// <summary>
        /// The floor CaughtViewInset inside the doorway, on the cryo room's side: where you first see in.
        /// </summary>
        private static Vector3? DoorwayView(EntryDetector doorway, Room cryo, Gate door)
        {
            Vector3 middle = door.transform.position;
            foreach (Vector3 axis in new[] { door.transform.forward, -door.transform.forward, door.transform.right, -door.transform.right })
            {
                Vector3 flat = new(axis.x, 0f, axis.z);
                if (flat.sqrMagnitude < 0.01f) continue;

                Vector3 at = middle + flat.normalized * CaughtViewInset;
                if (!InCryo(doorway, cryo, at) || !NavProbe.TryFloorHeight(at + Vector3.up * 0.5f, out float floorY)) continue;

                at.y = floorY;
                return at;
            }
            return null;
        }

        /// <summary>
        /// A ground node in clear view of the doorway, on the cryo room's side, CaughtStandMin..Max in and
        /// nearest CaughtStandDist, out of your sight. NPC.Core gives a room the nodes nearest its furniture,
        /// which can lie in the hallway: the side and the clear line keep it in the room you open.
        /// </summary>
        private static Vector3? CaughtStand(EntryDetector doorway, Room cryo, Vector3 view)
        {
            NpcVessels.FloorOwner(view, out string? owner, out _);
            Vector3? best = null;
            float bestOff = float.MaxValue;
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                if (owner != null && NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 at = NavGraph.GetNodeWorld(i);
                float d = FlatDistance(at, view);
                float off = Mathf.Abs(d - CaughtStandDist);
                if (d < CaughtStandMin || d > CaughtStandMax || off >= bestOff || Mathf.Abs(at.y - view.y) > 1f) continue;

                if (!InCryo(doorway, cryo, at) || !NavProbe.WalkLos(view, at, 0.5f)) continue;

                if (PlayerView.Sees(at + Vector3.up * 1.1f, null)) continue;

                best = at;
                bestOff = off;
            }
            return best;
        }

        /// <summary>
        /// The meat in front of it, blood under and around the meat and at its feet.
        /// </summary>
        private void LeaveTheMess(Transform room, Vector3 feet, Vector3 meat, Vector3 away)
        {
            int stains = 0;
            float yaw = Quaternion.LookRotation(away).eulerAngles.y;
            if (Stain(meat, 1.6f)) stains++;
            if (Stain(feet, 1f)) stains++;
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = meat + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0.4f, 1f);
                if (Stain(at, Random.Range(0.8f, 1.3f))) stains++;
            }
            bool meatLeft = AnomalyProps.SpawnMeat(room, meat, yaw + Random.Range(-40f, 40f)) != null;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} leaves {(meatLeft ? "raw meat and " : "")}{stains} blood stain(s) in the cryo room");

            bool Stain(Vector3 at, float scale) =>
                FloorNear(at, feet.y, out at) && AnomalyProps.SpawnBlood(room, at, Random.Range(0f, 360f), scale) != null;
        }

        /// <summary>
        /// The meat CaughtMeatAhead beyond its feet, or nearer, on bare floor it could walk to; at its feet if
        /// furniture fills both.
        /// </summary>
        private static Vector3 MeatSpot(Vector3 feet, Vector3 away)
        {
            foreach (float ahead in new[] { CaughtMeatAhead, CaughtMeatAhead * 0.6f })
            {
                if (FloorNear(feet + away * ahead, feet.y, out Vector3 at) && NavProbe.WalkLos(feet, at, 0.1f)) return at;
            }
            return feet + away * 0.3f;
        }

        /// <summary>
        /// The floor at `at`, level with `floorY`. Probed from just above it, so a crate top or a chair seat
        /// is never taken for the floor. docs/anomalies.md#the-mess-and-the-meat-model
        /// </summary>
        private static bool FloorNear(Vector3 at, float floorY, out Vector3 floor)
        {
            floor = new Vector3(at.x, floorY, at.z);
            if (!NavProbe.TryFloorHeight(floor + Vector3.up * MessProbeAbove, out float y) || Mathf.Abs(y - floorY) > MessLevelTolerance) return false;

            floor.y = y;
            return true;
        }

        /// <summary>
        /// Wet sounds until you open a door or come near; then it stares at you until you step in, and runs
        /// deeper into the room. Gone once you have seen it and look away; it comes back clean.
        /// </summary>
        private void UpdateCaught(float now, float dist, Transform you)
        {
            if (EndIfUndocked()) return;

            if (cryoRoom == null || caughtDoorway == null)
            {
                EndAnomaly("the cryo room went away");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    bool doorOpen = cryoDoorways.Exists(d => d != null && NpcDoors.DoorOf(d) is { Opened: true });
                    if (doorOpen || anomalySeen || dist < CaughtNearDist)
                    {
                        anomalyStep = 1;
                        anomalyStepAt = now + CaughtTurnSeconds;
                        anomalyUntil = now + CaughtCorneredSeconds;
                        anomalySeenFor = 0f;
                        ScareSounds.Play(ScareSound.Creature, agent.GroundPos(1.2f));
                        Startle(30);
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is caught ({(doorOpen ? "you opened the door" : anomalySeen ? "you saw it" : "you came near")}, " +
                                                    $"{dist:0.0}m, {(anomalySeen ? "in your view" : "not in your view yet")})");
                        return;
                    }
                    if (now >= anomalyUntil)
                    {
                        EndAnomaly("you never came");
                        return;
                    }
                    if (now < anomalyStepAt) return;

                    anomalyStepAt = now + Random.Range(2.5f, 4.5f);
                    ScareSounds.Play(ScareSound.Wet, agent.GroundPos(0.6f));
                    break;
                case 1:
                    // It stares at you until you step in.
                    if (now < anomalyStepAt) return;

                    bool youIn = dist < CaughtNearDist || (InCryo(caughtDoorway, cryoRoom, you.position) &&
                                                           FlatDistance(you.position, caughtDoorway.transform.position) < CaughtEnterDist);
                    if (!youIn && now < anomalyUntil) return;

                    if (!youIn && !anomalySeen)
                    {
                        CaughtGone(now, "you never came in");
                        return;
                    }
                    Room cryo = cryoRoom;
                    EntryDetector doorway = caughtDoorway;
                    bool InRoom(Vector3 node) => InCryo(doorway, cryo, node);
                    string? stuck = RunFrom(now, you.position, CaughtRunMin, CaughtRunGain, InRoom);
                    if (stuck != null) stuck = RunFrom(now, you.position, 1f, 0f, InRoom);

                    anomalyUntil = now + CaughtCorneredSeconds;
                    if (stuck == null)
                    {
                        anomalyStep = 2;
                        caughtRan = true;
                        return;
                    }
                    anomalyStep = 3;
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is cornered in the cryo room - {stuck}");
                    break;
                case 3:
                    // Never gone before you have seen it, unless it ran where you could not.
                    bool lookedAway = !anomalySeen && now - anomalyUnseenSince >= CaughtGoneUnseenSeconds;
                    if (lookedAway && (anomalySeenFor > 0f || caughtRan))
                    {
                        CaughtGone(now, anomalySeenFor > 0f ? "you looked away" : "it ran out of your sight");
                        return;
                    }
                    if (now < anomalyUntil) return;

                    if (anomalySeen) EndAnomaly("you never looked away");
                    else CaughtGone(now, "out of time");
                    break;
                case 4:
                    UpdateVanished(now, you);
                    break;
            }
        }

        private void CaughtGone(float now, string why)
        {
            anomalyStep = 4;
            anomalyUntil = now + Random.Range(VanishMinSeconds, VanishMaxSeconds);
            anomalyGiveUpAt = anomalyUntil + 60f;
            Disappear();
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is gone from the cryo room - {why} (seen {anomalySeenFor:0.0}s)");
        }

        /// <summary>
        /// There, or the run gave up: it waits, cornered, for you to look away.
        /// </summary>
        private void StopCaughtRun()
        {
            StopAnomalyWalk();
            anomalyStep = 3;
        }

        // ------------------------------------------------------------------
        // The Shipyard's robot
        // ------------------------------------------------------------------

        /// <summary>
        /// Out of your sight, it is put at the Shipyard robot's desk, on the side you will come from, while
        /// you are on the ship side of the docked station. docs/anomalies.md#bottalk
        /// </summary>
        private string? StartBotTalk(Transform you, bool seen)
        {
            if (seen) return "you are watching it";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            ShipyardStation? station = DockedShipyard(out string why);
            if (station == null) return why;

            AssistanceBot? bot = station.Bot;
            // Off with its hallway while you are aboard: loaded below, as a sell run loads its station.
            // docs/items.md#4-selling-trash-boxes
            if (bot == null || !Items.Loadable(bot)) return "the Shipyard's robot is not here";

            string? botOwner = NpcVessels.OwnerOfTransform(bot.transform);

            Vector3 botPos = bot.transform.position;
            float far = FlatDistance(you.position, botPos);
            if (far < BotTalkMinDist || far > BotTalkMaxDist)
            {
                return $"you are {far:0.0}m from the Shipyard's robot - it needs you {BotTalkMinDist:0}-{BotTalkMaxDist:0}m away, on the ship side";
            }

            Vector3? stand = BotStand(botPos, botOwner, you.position);
            if (stand == null) return "no node by the robot out of your sight";

            if (!LoadBotRoom(bot)) return "the robot's room will not load";

            StartHold(AnomalyKind.BotTalk, BotTalkWaitSeconds);
            fromAboard = agent.IsAboardPlayerShip() ? agent.FloorUnderNpc() : null;
            MoveTo(stand.Value);
            talkBot = bot;
            shipyard = station;
            botTalksNext = Random.value < 0.5f;
            botBurstUntil = 0f;
            anomalyFace = botPos;
            anomalyStepAt = Time.time + 0.5f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stands at the Shipyard's robot, {FlatDistance(stand.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// The robot's room switched on if the game has it off. False when it cannot be.
        /// </summary>
        private bool LoadBotRoom(AssistanceBot bot)
        {
            if (bot.gameObject.activeInHierarchy) return true;

            if (!Items.Loadable(bot)) return false;

            agent.LoadRoom(bot.GetComponentInParent<Room>(true), "it talks with the robot");
            return bot.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// The node BotStandMin..Max from the robot that faces where you are, out of your sight.
        /// </summary>
        private static Vector3? BotStand(Vector3 botPos, string? owner, Vector3 yourPos)
        {
            Vector3 toYou = yourPos - botPos;
            toYou.y = 0f;
            toYou.Normalize();
            Vector3? best = null;
            float bestDot = -2f;
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                if (owner != null && NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                if (Mathf.Abs(node.y - botPos.y) > 1.5f) continue;

                float d = FlatDistance(node, botPos);
                if (d < BotStandMin || d > BotStandMax) continue;

                Vector3 dir = node - botPos;
                dir.y = 0f;
                float dot = Vector3.Dot(dir.normalized, toYou);
                if (dot <= bestDot) continue;

                if (PlayerView.Sees(node + Vector3.up * 1.1f, null) || PlayerView.Sees(node + Vector3.up * 0.3f, null)) continue;

                best = node;
                bestDot = dot;
            }
            return best;
        }

        /// <summary>
        /// Turns: a line of its blips, then the robot's answer in its own talk sound, its eye moving.
        /// The robot's sound plays as fast as its typewriter would: about once a frame.
        /// </summary>
        private void Converse(float now, AssistanceBot bot)
        {
            bot.Animator.TargetLookPosition = agent.GroundPos(1.5f);
            if (now < botBurstUntil)
            {
                if (now < botBlipAt) return;

                bot.PlayTalkSound(0);
                botBlipAt = now + BotBlipGap;
                return;
            }
            bot.Animator.Eye.scaleAnimation = false;
            if (now < anomalyStepAt) return;

            botTalksNext = !botTalksNext;
            if (botTalksNext)
            {
                botBurstUntil = now + Random.Range(0.8f, 2.4f);
                botBlipAt = now;
                bot.Animator.Eye.scaleAnimation = true;
                anomalyStepAt = botBurstUntil + Random.Range(0.4f, 1.2f);
                return;
            }
            int blips = Random.Range(5, 15);
            ScareSounds.Babble(transform, blips);
            anomalyStepAt = now + blips * 0.1f + Random.Range(0.4f, 1.2f);
        }

        /// <summary>
        /// Talking until you come near, then a look round at you, then a run away from you; done once it
        /// is out of your sight. The run itself is WalkAnomalyLeg's.
        /// </summary>
        private void UpdateBotTalk(float now, float dist, Transform you)
        {
            if (EndIfUndocked()) return;

            AssistanceBot? bot = talkBot;
            if (bot == null || !LoadBotRoom(bot))
            {
                EndAnomaly("the robot is gone");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    if (dist < BotTalkCloseDist || (anomalySeen && dist < BotTalkGlanceDist))
                    {
                        anomalyStep = 1;
                        anomalyStepAt = now + BotTalkGlanceSeconds;
                        botBurstUntil = 0f;
                        bot.Animator.Eye.scaleAnimation = false;
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} sees you coming ({dist:0.0}m) and looks round");
                        return;
                    }
                    if (now >= anomalyUntil)
                    {
                        EndAnomaly("you never came");
                        return;
                    }
                    Converse(now, bot);
                    break;
                case 1:
                    // The robot looks at you too.
                    bot.Animator.TargetLookPosition = you.position;
                    if (now < anomalyStepAt) break;

                    string? stuck = RunFrom(now, you.position);
                    if (stuck != null) EndAnomaly(stuck);
                    else anomalyStep = 2;
                    break;
                case 3:
                    if ((!anomalySeen && now - anomalyUnseenSince >= RunOffUnseenSeconds) || now >= anomalyUntil) EndAnomaly("ran off");
                    break;
            }
        }

        /// <summary>
        /// A run to a node further from you, away from you, out of your sight if it can; WalkAnomalyLeg
        /// runs it. Null when it is on its way, else why not. `within` keeps the run in one place (Meat).
        /// </summary>
        private string? RunFrom(float now, Vector3 yourPos, float minDist = RunOffMinDist, float gain = RunOffGain,
            System.Predicate<Vector3>? within = null)
        {
            Vector3 here = transform.position;
            Vector3 toYou = yourPos - here;
            toYou.y = 0f;
            float yours = toYou.magnitude;
            RunOffNodes.Clear();
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                if (agent.CurrentOwner != null && NavGraph.GetNodeOwner(i) != agent.CurrentOwner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                if (Mathf.Abs(node.y - here.y) > 3f) continue;

                float d = FlatDistance(node, here);
                if (d < minDist || d > RunOffMaxDist || FlatDistance(node, yourPos) < yours + gain) continue;

                Vector3 away = node - here;
                away.y = 0f;
                if (Vector3.Dot(away, toYou) >= 0f || (within != null && !within(node))) continue;

                RunOffNodes.Add(node);
            }
            Vector3 from = agent.FloorUnderNpc();
            // Out of your sight first, so it is gone once there; else anywhere away from you.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < RunOffNodes.Count; i++)
                {
                    int pick = Random.Range(i, RunOffNodes.Count);
                    (RunOffNodes[i], RunOffNodes[pick]) = (RunOffNodes[pick], RunOffNodes[i]);
                    Vector3 node = RunOffNodes[i];
                    if (pass == 0 && PlayerView.Sees(node + Vector3.up * 1.1f, null)) continue;

                    NavPath? plan = NavGraph.FindPath(from, node);
                    if (plan is not { Count: > 0 }) continue;

                    agent.CommitPlan(plan.Value, false);
                    anomalyWalking = true;
                    anomalyStand = node;
                    anomalyArrival = NpcAgent.ReachStandArrival;
                    anomalyGiveUpAt = now + RunOffLegSeconds;
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} runs off {FlatDistance(node, here):0.0}m, " +
                                                $"to {FlatDistance(node, yourPos):0.0}m from you" + (pass == 0 ? ", out of your sight" : ""));
                    return null;
                }
            }
            return $"nowhere to run ({RunOffNodes.Count} node(s) away from you)";
        }

        /// <summary>
        /// There, or the run gave up: it stands still and waits to be out of your sight.
        /// </summary>
        private void StopBloodyRun()
        {
            StopAnomalyWalk();
            anomalyStep = 2;
        }

        private void StopAnomalyWalk()
        {
            anomalyWalking = false;
            agent.DropPlan();
            agent.ClearMoveTarget();
        }

        /// <summary>
        /// The robot as the game leaves it when you walk away: eye still, looking ahead.
        /// </summary>
        private void ReleaseBot()
        {
            if (talkBot != null)
            {
                talkBot.Animator.Eye.scaleAnimation = false;
                talkBot.Animator.TargetLookPosition = talkBot.Animator.Forward;
            }
            talkBot = null;
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
                // The shadow is what you see, not the buddy.
                bool seen = anomaly == AnomalyKind.Shadow ? SeesShadow() : !vanished && PlayerView.SeesBody(transform, agent.FloorUnderNpc());
                if (seen) anomalySeenFor += SightSampleSeconds;
                // A flicker is witnessed when it flashes, a sleeper when it is found.
                if (seen && anomaly is not (AnomalyKind.Smile or AnomalyKind.UnderTheSuit or AnomalyKind.Sleeper)) anomalyWitnessed = true;
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
                case AnomalyKind.BotTalk:
                    UpdateBotTalk(now, dist, you);
                    break;
                case AnomalyKind.Meat:
                    UpdateCaught(now, dist, you);
                    break;
                case AnomalyKind.Bloody:
                    UpdateBloody(now, you.position);
                    break;
                case AnomalyKind.Statue:
                    if (anomalySeen && dist < 2.5f) Startle(15);
                    if (now >= anomalyUntil && !anomalySeen) EndAnomaly("done");
                    break;
                case AnomalyKind.Stalker:
                    UpdateStalker(now, dist);
                    break;
                case AnomalyKind.Pipe:
                    UpdatePipe(now, dist);
                    break;
                case AnomalyKind.Move:
                    UpdateMove(now, dist);
                    break;
                case AnomalyKind.Shadow:
                    UpdateShadow(now, you);
                    break;
                case AnomalyKind.Smile:
                case AnomalyKind.UnderTheSuit:
                    UpdateFlicker(now, dist);
                    break;
                case AnomalyKind.Sleeper:
                    UpdateSleeper(now, dist);
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
            if (gate != null && gate.Opened && !doorSteppedClear && FlatDistance(transform.position, gate.transform.position) < DoorClearDist)
            {
                StepClearOf(gate, now);
                return;
            }
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

        private void UpdateBloody(float now, Vector3 yourPos)
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
            // The run gives way to the Breathless and to bad air: docs/invariants.md#fear-owns-the-buddy
            if (anomalyWalking)
            {
                if (fearState == FearState.Calm && !lifeSupport.AirIsDangerous()) return;

                StopAnomalyWalk();
                anomalyStep = 0;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stops running off - it has worse to worry about");
                return;
            }
            // Never while you look at it: only once its time is up and you have not seen it for a while.
            if (now < anomalyUntil) return;

            if (!anomalySeen && now - anomalyUnseenSince >= BloodyUnseenSeconds)
            {
                EndAnomaly(anomalySeenFor > 0f ? "out of your sight, clean again" : "nobody saw");
                return;
            }
            // You keep it in sight: it gets away from you to be clean.
            if (now < anomalyStepAt || fearState != FearState.Calm) return;

            anomalyStepAt = now + BloodyRunRetrySeconds;
            if (RunFrom(now, yourPos) is { } stuck) TraceBloody(stuck);
            else anomalyStep = 1;
        }

        private void TraceBloody(string why)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} cannot get out of your sight yet - {why}");
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
                SpeakNew(AnomalyLines.Spoken(AnomalySeverity.Extreme));
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

            if (fearState != FearState.Calm && !vanished && !AnomalyInBackground)
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
                case AnomalyKind.Move:
                    return true;
                case AnomalyKind.Sleeper:
                    // You found the one in the capsule: it stands where it is, facing you, until you see it.
                    if (anomalyStep != 2 || sleeperActorFound) return false;

                    agent.ClearMoveTarget();
                    return true;
                case AnomalyKind.Bloody:
                    // Got away from you: it stays there to be clean. Fear frees it.
                    if (anomalyStep == 2 && fearState != FearState.Calm) anomalyStep = 0;

                    return anomalyStep == 2;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                case AnomalyKind.ShutDoors:
                case AnomalyKind.BotTalk:
                case AnomalyKind.Meat:
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
                case AnomalyKind.Move:
                    agent.FacePlayer(player);
                    return true;
                case AnomalyKind.Statue:
                case AnomalyKind.Stalker:
                    if (!anomalySeen) return false;

                    agent.FacePlayer(player);
                    return true;
                case AnomalyKind.Sleeper:
                    if (anomalyStep != 2 || sleeperActorFound) return false;

                    agent.FacePlayer(player);
                    return true;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                case AnomalyKind.ShutDoors:
                    if (anomalyWalking) return false;

                    agent.FacePoint(anomalyFace);
                    return true;
                case AnomalyKind.Meat:
                    // Over the meat, back to the door; then at you, caught, and cornered.
                    if (anomalyWalking || vanished) return false;

                    if (anomalyStep == 0) agent.FacePoint(anomalyFace);
                    else agent.FacePlayer(player);

                    return true;
                case AnomalyKind.BotTalk:
                    // The robot while they talk, you for the look round; once away, the usual facing.
                    if (anomalyStep == 0) agent.FacePoint(anomalyFace);
                    else if (anomalyStep == 1) agent.FacePlayer(player);
                    else return false;

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
            ShowFlicker(false);
            flickerTexture = null;
            flickerWearing = null;
            RemoveShadow();
            RemoveSleeper();
            ReleaseBot();
            agent.KnowsEveryCode = false;
            shipyard = null;
            fromAboard = null;
            cryoRoom = null;
            caughtDoorway = null;
            // Whatever ended it, the pipe goes down where it stands.
            if (pipe != null && agent.Hands.Item == pipe) agent.Hands.Drop("the bloody pipe is over - " + why);
            pipe = null;
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
            // Nobody saw it, and nothing of it is left to find: it may come again. docs/anomalies.md#once-per-save
            if (!anomalyWitnessed && kind is not (AnomalyKind.Meat or AnomalyKind.Pipe or AnomalyKind.Move))
            {
                AnomalyMemory.Forget(kind);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] Nobody saw {Anomalies.Info(kind).Name} - it may happen again");
            }
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
        /// A line not said before in this save; null, as Speak, when it was said. With every line said it stays
        /// silent and says why. docs/anomalies.md#once-per-save
        /// </summary>
        private string? SpeakNew(string? text)
        {
            if (text != null) return Speak(text);

            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} has nothing left to say that it has not said");
            return "it has said every line it has";
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
                  (anomalyWitnessed ? ", seen" : "") +
                  $", {Mathf.Max(0f, anomalyUntil - Time.time):0}s"
                : "none";
            return now + (TitleOverride != null ? "; wrong name waiting" : "") + " (last: " + anomalyLast + ")";
        }
    }
}
