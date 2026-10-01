using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using NPC.Core;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What an anomaly sound is meant to suggest. docs/anomalies.md#5-sounds
    /// </summary>
    internal enum ScareSound
    {
        /// <summary>The robot's talk blips: the buddy's "voice" for a spoken line.</summary>
        Voice,
        /// <summary>Blips and a door's failed close: clicking.</summary>
        Click,
        /// <summary>Scrubbing: a wet squelch.</summary>
        Wet,
        /// <summary>The Breathless moving, the game's own scare stings.</summary>
        Creature,
        /// <summary>The Breathless screech, a scream.</summary>
        Shriek
    }

    /// <summary>
    /// The game's FMOD events, borrowed from whatever instance of their owner the scene holds and kept until
    /// the world resets. A category with nothing found stays silent.
    /// </summary>
    internal static class ScareSounds
    {
        private static readonly Dictionary<ScareSound, List<EventReference>> Found = [];
        private static bool _gathered;
        private static float _gatheredAt;
        /// <summary>
        /// The monster's and the game's scare sounds can run for many seconds: these are cut short, faded
        /// out, after SoundCapSeconds. docs/anomalies.md#5-sounds
        /// </summary>
        internal const float SoundCapSeconds = 2.5f;
        private static readonly List<(EventInstance sound, float stopAt)> Playing = [];
        /// <summary>
        /// Voice blips still to come: who speaks, and when. docs/anomalies.md#3-what-it-says
        /// </summary>
        private static readonly List<(Transform speaker, float at)> Blips = [];
        private const float BlipGapMin = 0.07f;
        private const float BlipGapMax = 0.12f;
        private const float BlipHeight = 1.5f;
        /// <summary>
        /// A category found empty is looked for again after this: its owners may load later.
        /// </summary>
        private const float RegatherSeconds = 60f;

        static ScareSounds()
        {
            NpcEvents.WorldReset += () =>
            {
                Found.Clear();
                _gathered = false;
                foreach ((EventInstance sound, float _) in Playing) Stop(sound, FMOD.Studio.STOP_MODE.IMMEDIATE);

                Playing.Clear();
                Blips.Clear();
            };
        }

        /// <summary>
        /// Plays one sound of the category at `at`, in 3D. False when none was found.
        /// </summary>
        internal static bool Play(ScareSound kind, Vector3 at)
        {
            Gather();
            if (!Found.TryGetValue(kind, out List<EventReference>? list) || list.Count == 0) return false;

            EventReference picked = list[Random.Range(0, list.Count)];
            if (kind < ScareSound.Creature)
            {
                RuntimeManager.PlayOneShot(picked, at);
                return true;
            }
            EventInstance sound = RuntimeManager.CreateInstance(picked);
            sound.set3DAttributes(at.To3DAttributes());
            sound.start();
            Playing.Add((sound, Time.time + SoundCapSeconds));
            return true;
        }

        /// <summary>
        /// A line's worth of the robot's talk blips at the speaker, one every BlipGapMin..Max, as the
        /// station robot's own typewriter plays them.
        /// </summary>
        internal static void Babble(Transform speaker, int count)
        {
            float at = Time.time;
            for (int i = 0; i < count; i++)
            {
                Blips.Add((speaker, at));
                at += Random.Range(BlipGapMin, BlipGapMax);
            }
        }

        /// <summary>
        /// From AnomalyDirector.Tick: plays the blips that are due, and fades out the long sounds whose
        /// time is up.
        /// </summary>
        internal static void Tick()
        {
            for (int i = Blips.Count - 1; i >= 0; i--)
            {
                if (Time.time < Blips[i].at) continue;

                if (Blips[i].speaker != null) Play(ScareSound.Voice, Blips[i].speaker.position + Vector3.up * BlipHeight);
                Blips.RemoveAt(i);
            }

            for (int i = Playing.Count - 1; i >= 0; i--)
            {
                if (Time.time < Playing[i].stopAt) continue;

                Stop(Playing[i].sound, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                Playing.RemoveAt(i);
            }
        }

        private static void Stop(EventInstance sound, FMOD.Studio.STOP_MODE how)
        {
            if (!sound.isValid()) return;

            sound.stop(how);
            sound.release();
        }

        private static void Gather()
        {
            bool complete = Found.Count == System.Enum.GetValues(typeof(ScareSound)).Length;
            if (_gathered && (complete || Time.time - _gatheredAt < RegatherSeconds)) return;

            _gathered = true;
            _gatheredAt = Time.time;
            Found.Clear();

            AssistanceBot? bot = Object.FindObjectOfType<AssistanceBot>(true);
            Breathless? breathless = GameManager.Instance != null ? GameManager.Instance.Breathless : null;
            Gate? gate = Object.FindObjectOfType<Gate>(true);

            Add(ScareSound.Voice, GameInternals.ScareSoundAccess.GetTalk(bot));
            Add(ScareSound.Click, GameInternals.ScareSoundAccess.GetTalk(bot));
            Add(ScareSound.Click, GameInternals.ScareSoundAccess.GetCloseFail(gate));
            Add(ScareSound.Wet, GameInternals.ScareSoundAccess.GetClean(Object.FindObjectOfType<Cleanable>(true)));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetMoving(breathless));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetActivity(Object.FindObjectOfType<BreathlessActivity>(true)));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetRandom(Object.FindObjectOfType<RandomSound>(true)));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetBackground(Object.FindObjectOfType<BackgroundSound>(true)));
            Add(ScareSound.Shriek, GameInternals.ScareSoundAccess.GetScreech(breathless));
            Add(ScareSound.Shriek, GameInternals.ScareSoundAccess.GetScream(Object.FindObjectOfType<UnsealScream>(true)));

            if (NpcLog.Level < 2) return;

            YourBuddyPlugin.Log.LogInfo("[anomaly] Sounds found: " + Describe());
        }

        private static void Add(ScareSound kind, EventReference? sound)
        {
            if (sound is not { IsNull: false } found) return;

            if (!Found.TryGetValue(kind, out List<EventReference>? list)) Found[kind] = list = [];

            list.Add(found);
        }

        /// <summary>
        /// "Voice 1, Click 2, ...", for the log and buddy_anomaly.
        /// </summary>
        internal static string Describe()
        {
            Gather();
            string text = "";
            foreach (KeyValuePair<ScareSound, List<EventReference>> entry in Found)
            {
                text += (text.Length > 0 ? ", " : "") + entry.Key + " " + entry.Value.Count;
            }
            return text.Length > 0 ? text : "none";
        }
    }
}
