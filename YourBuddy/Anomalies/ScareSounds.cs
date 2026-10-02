using System.Collections.Generic;
using System.Text;
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
        /// <summary>The robot's talk blips, the buddy's "voice" for a spoken line.</summary>
        Voice,
        /// <summary>Something at your back, from the mod's knocks, cracks, creaks and sighs.</summary>
        Odd,
        /// <summary>Feeding, from the mod's tearing and popping.</summary>
        Gore,
        /// <summary>The meat in your hands, from the mod's slimy squishes.</summary>
        Slime,
        /// <summary>The Breathless moving, the game's own scare stings.</summary>
        Creature,
        /// <summary>The Breathless screech, a scream.</summary>
        Shriek
    }

    /// <summary>
    /// The game's FMOD events, borrowed from whatever instance of their owner the scene holds and kept until
    /// the world resets, and the mod's own clips (ModSounds). A category with nothing found stays silent.
    /// </summary>
    internal static class ScareSounds
    {
        /// <summary>A game event, or the name of one of the mod's clips.</summary>
        private readonly struct Source(EventReference sound, string? clip)
        {
            internal readonly EventReference Event = sound;
            internal readonly string? Clip = clip;
        }

        private static readonly Dictionary<ScareSound, List<Source>> Found = [];
        /// <summary>
        /// Each category's last picks, by index, not picked again yet. docs/anomalies.md#5-sounds
        /// </summary>
        private static readonly Dictionary<ScareSound, List<int>> Recent = [];
        private const int RecentCount = 2;
        private static bool _gathered;
        private static float _gatheredAt;
        /// <summary>
        /// The monster's and the game's scare sounds can run for many seconds: they fade to silence over the
        /// last SoundFadeSeconds before SoundCapSeconds, and stop there. docs/anomalies.md#5-sounds
        /// </summary>
        internal const float SoundCapSeconds = 3f;
        internal const float SoundFadeSeconds = 1.5f;
        /// <summary>How fast the sounds still playing die away once you turn round.</summary>
        internal const float FadeOutSeconds = 0.25f;
        /// <summary>An event still playing: when it starts to fade, when it stops, and who it came from.</summary>
        private sealed class Live
        {
            internal EventInstance Sound;
            internal float FadeFrom = float.MaxValue;
            internal float StopAt = float.MaxValue;
            internal float Volume = 1f;
            internal Transform? Source;
        }

        private static readonly List<Live> Playing = [];
        /// <summary>
        /// Voice blips still to come, with who speaks and when. docs/anomalies.md#3-what-it-says
        /// </summary>
        private static readonly List<(Transform speaker, float at)> Blips = [];
        private const float BlipGapMin = 0.07f;
        private const float BlipGapMax = 0.12f;
        private const float BlipHeight = 1.5f;
        /// <summary>
        /// A category found empty is looked for again after this, since its owners may load later.
        /// </summary>
        private const float RegatherSeconds = 60f;

        static ScareSounds()
        {
            NpcEvents.WorldReset += () =>
            {
                Found.Clear();
                _gathered = false;
                foreach (Live live in Playing) Stop(live.Sound);

                Playing.Clear();
                Blips.Clear();
            };
        }

        /// <summary>
        /// Plays one sound of the category at `at`, in 3D. False when none was found.
        /// </summary>
        internal static bool Play(ScareSound kind, Vector3 at) => Play(kind, at, null, out _);

        /// <summary>
        /// Plays from `source` (its colliders never muffle a clip) and returns how long the sound lasts.
        /// `volume` multiplies the sound's own.
        /// </summary>
        internal static bool Play(ScareSound kind, Vector3 at, Transform? source, out float seconds, float volume = 1f)
        {
            seconds = 0f;
            Gather();
            if (!Found.TryGetValue(kind, out List<Source>? list) || list.Count == 0) return false;

            return PlayOne(kind, list[Pick(kind, list.Count)], at, source, out seconds, volume);
        }

        private static bool PlayOne(ScareSound kind, Source picked, Vector3 at, Transform? source, out float seconds,
            float volume = 1f)
        {
            if (picked.Clip != null)
            {
                bool played = ModSounds.Play(picked.Clip, at, source, out seconds, volume);
                if (played) Trace(kind, picked.Clip, seconds);

                return played;
            }
            seconds = Length(picked.Event, kind >= ScareSound.Creature);
            if (kind >= ScareSound.Odd) Trace(kind, Path(picked.Event), seconds);

            if (kind == ScareSound.Voice)
            {
                RuntimeManager.PlayOneShot(picked.Event, at);
                return true;
            }
            // Released now, freed once it stops; the handle still fades and stops it until then.
            EventInstance sound = RuntimeManager.CreateInstance(picked.Event);
            sound.set3DAttributes(at.To3DAttributes());
            sound.setVolume(volume);
            sound.start();
            sound.release();
            Live live = new() { Sound = sound, Source = source, Volume = volume };
            if (kind >= ScareSound.Creature)
            {
                live.StopAt = Time.time + SoundCapSeconds;
                live.FadeFrom = live.StopAt - SoundFadeSeconds;
            }
            Playing.Add(live);
            return true;
        }

        /// <summary>
        /// Every sound from `source` still playing, clip or event, fades out over FadeOutSeconds and stops:
        /// you turned round. docs/anomalies.md#5-sounds
        /// </summary>
        internal static void FadeOut(Transform source)
        {
            foreach (Live live in Playing)
            {
                if (live.Source != source || live.StopAt <= Time.time + FadeOutSeconds) continue;

                live.FadeFrom = Time.time;
                live.StopAt = Time.time + FadeOutSeconds;
            }
            ModSounds.FadeOut(source, FadeOutSeconds);
        }

        /// <summary>
        /// buddy_dev sound: every category's sounds, numbered, with what each is. docs/anomalies.md#7-testing
        /// </summary>
        internal static string List()
        {
            Gather();
            StringBuilder text = new();
            foreach (KeyValuePair<ScareSound, List<Source>> entry in Found)
            {
                text.Append(text.Length > 0 ? "\n" : "").Append(entry.Key).Append(':');
                for (int i = 0; i < entry.Value.Count; i++) text.Append("\n  ").Append(i).Append(' ').Append(Describe(entry.Value[i]));
            }
            return text.Length > 0 ? text.ToString() : "No sounds found yet.";
        }

        /// <summary>
        /// buddy_dev sound: one of `kind`'s sounds by number, or a pick as an anomaly makes it.
        /// </summary>
        internal static string Audition(ScareSound kind, int? index, Vector3 at, Transform? source)
        {
            Gather();
            if (!Found.TryGetValue(kind, out List<Source>? list) || list.Count == 0) return kind + ": none found";

            int i = index ?? Pick(kind, list.Count);
            if (i < 0 || i >= list.Count) return $"{kind} has {list.Count} sound(s), 0-{list.Count - 1}";

            bool played = PlayOne(kind, list[i], at, source, out float seconds);
            return $"{kind} {i}: {Describe(list[i])}" + (played ? $", playing {seconds:0.0}s" : " - did not play");
        }

        /// <summary>
        /// A clip's name, or an event's path, length and 3D range: an event whose range ends short of you is never heard.
        /// </summary>
        private static string Describe(Source sound)
        {
            if (sound.Clip != null) return "clip " + sound.Clip;

            EventDescription description = RuntimeManager.GetEventDescription(sound.Event);
            string length = description.getLength(out int ms) == FMOD.RESULT.OK && ms > 0 ? $"{ms / 1000f:0.0}s" : "looping or unknown length";
            string range = description.getMinMaxDistance(out float min, out float max) == FMOD.RESULT.OK ? $", heard {min:0.#}-{max:0.#} m" : "";
            return $"{Path(sound.Event)}, {length}{range}";
        }

        /// <summary>
        /// An event's length in seconds, at most SoundCapSeconds for a capped one. Unknown (looping) gives 1 s, or the cap.
        /// </summary>
        private static float Length(EventReference sound, bool capped)
        {
            float seconds = RuntimeManager.GetEventDescription(sound).getLength(out int ms) == FMOD.RESULT.OK && ms > 0 ? ms / 1000f : 0f;
            if (capped) return seconds > 0f ? Mathf.Min(seconds, SoundCapSeconds) : SoundCapSeconds;

            return seconds > 0f ? seconds : 1f;
        }

        private static string Path(EventReference sound) =>
            RuntimeManager.GetEventDescription(sound).getPath(out string path) == FMOD.RESULT.OK ? path : sound.Guid.ToString();

        private static void Trace(ScareSound kind, string what, float seconds)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] Sound {kind}: {what} ({seconds:0.0}s)");
        }

        /// <summary>
        /// A random index under `count`, not one of the category's last RecentCount (fewer when it has fewer).
        /// </summary>
        private static int Pick(ScareSound kind, int count)
        {
            if (!Recent.TryGetValue(kind, out List<int>? recent)) Recent[kind] = recent = [];

            int keep = Mathf.Min(RecentCount, count - 1);
            while (recent.Count > keep) recent.RemoveAt(0);

            int left = Random.Range(0, count - recent.Count);
            int picked = 0;
            for (int i = 0; i < count; i++)
            {
                if (recent.Contains(i)) continue;
                if (left-- == 0)
                {
                    picked = i;
                    break;
                }
            }
            if (keep > 0) recent.Add(picked);

            return picked;
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
        /// From AnomalyDirector.Tick. Plays the blips that are due, and fades out the long sounds whose
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
                Live live = Playing[i];
                if (live.Sound.isValid() && Time.time < live.StopAt)
                {
                    if (Time.time > live.FadeFrom) live.Sound.setVolume(live.Volume * (live.StopAt - Time.time) / (live.StopAt - live.FadeFrom));

                    continue;
                }
                Stop(live.Sound);
                Playing.RemoveAt(i);
            }
            ModSounds.Tick();
        }

        private static void Stop(EventInstance sound)
        {
            if (sound.isValid()) sound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        private static void Gather()
        {
            bool complete = Found.Count == System.Enum.GetValues(typeof(ScareSound)).Length;
            if (_gathered && (complete || Time.time - _gatheredAt < RegatherSeconds)) return;

            _gathered = true;
            _gatheredAt = Time.time;
            Found.Clear();
            Recent.Clear();

            AssistanceBot? bot = Object.FindObjectOfType<AssistanceBot>(true);
            Breathless? breathless = GameManager.Instance != null ? GameManager.Instance.Breathless : null;

            Add(ScareSound.Voice, GameInternals.ScareSoundAccess.GetTalk(bot));
            foreach (string clip in ModSounds.Named("odd_")) Add(ScareSound.Odd, new Source(default, clip));
            foreach (string clip in ModSounds.Named("gore_")) Add(ScareSound.Gore, new Source(default, clip));
            foreach (string clip in ModSounds.Named("slime")) Add(ScareSound.Slime, new Source(default, clip));
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

            Add(kind, new Source(found, null));
        }

        private static void Add(ScareSound kind, Source source)
        {
            if (!Found.TryGetValue(kind, out List<Source>? list)) Found[kind] = list = [];

            list.Add(source);
        }

        /// <summary>
        /// "Voice 1, Odd 12, ...", for the log and buddy_anomaly.
        /// </summary>
        internal static string Describe()
        {
            Gather();
            string text = "";
            foreach (KeyValuePair<ScareSound, List<Source>> entry in Found)
            {
                text += (text.Length > 0 ? ", " : "") + entry.Key + " " + entry.Value.Count;
            }
            return text.Length > 0 ? text : "none";
        }
    }
}
