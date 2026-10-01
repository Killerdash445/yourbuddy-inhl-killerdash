using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Interaction;
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

        // Bloody: the texture it wore before.
        private Texture? goreWearing = null;
        private Texture2D? goreTexture = null;

        // Walking to a window or a wall, then staring at it.
        private bool anomalyWalking = false;
        private Vector3 anomalyStand;
        private Vector3 anomalyFace;
        private float anomalyArrival = NpcAgent.ReachStandArrival;

        // The wrong name in the talk window's title, until it is next opened.
        private string? wrongTitle = null;
        private float wrongTitleUntil = 0f;
        private static readonly List<Vector3> RunOffNodes = [];

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
        private const float BloodySeconds = 150f;
        private const float BloodySeenRate = 15f;
        private const float BloodyUnseenSeconds = 5f;
        private const float BloodyRunRetrySeconds = 6f;
        private const float WrongNameSeconds = 900f;
        private const float RunOffMinDist = 6f;
        private const float RunOffMaxDist = 20f;
        private const float RunOffGain = 4f;
        private const float RunOffLegSeconds = 20f;
        private const float RunOffUnseenSeconds = 3f;

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

            Player? player = NpcPlayer.Pilot;
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
            Player? player = NpcPlayer.Pilot;
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
