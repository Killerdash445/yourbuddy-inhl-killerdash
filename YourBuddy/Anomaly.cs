using System;

namespace YourBuddy
{
    /// <summary>
    /// How far an anomaly goes. The difficulty and the game's event tier decide which are allowed.
    /// docs/anomalies.md#1-how-often-and-how-far
    /// </summary>
    public enum AnomalySeverity
    {
        Funny,
        Strange,
        Scary,
        Extreme
    }

    /// <summary>
    /// The AnomalyDifficulty setting: the game's own difficulty, or one fixed for the buddy alone.
    /// </summary>
    public enum AnomalyLevel
    {
        Game,
        Harmless,
        Normal,
        Expert
    }

    /// <summary>
    /// Every anomaly the buddy can act out. docs/anomalies.md#2-the-anomalies
    /// </summary>
    internal enum AnomalyKind
    {
        Spin,
        PeekABoo,
        Chatter,
        Whisper,
        FakeCommand,
        WrongName,
        Vanish,
        Noises,
        WindowStare,
        WallStare,
        Bloody,
        ClosetAmbush,
        ShutDoors,
        Statue,
        Stalker,
        BehindYou
    }

    /// <summary>
    /// One row of the catalogue: its severity, how often it is drawn among its peers, and what to call it.
    /// </summary>
    internal readonly struct AnomalyInfo(AnomalyKind kind, AnomalySeverity severity, float weight, string name,
        bool harmlessOnly = false)
    {
        public readonly AnomalyKind Kind = kind;
        public readonly AnomalySeverity Severity = severity;
        public readonly float Weight = weight;
        public readonly string Name = name;
        /// <summary>
        /// Too silly for Normal or Expert: drawn only at frequency 0.
        /// </summary>
        public readonly bool HarmlessOnly = harmlessOnly;
    }

    internal static class Anomalies
    {
        /// <summary>
        /// The catalogue, in AnomalyKind order. docs/anomalies.md#2-the-anomalies
        /// </summary>
        internal static readonly AnomalyInfo[] All =
        [
            new(AnomalyKind.Spin, AnomalySeverity.Funny, 1f, "a happy spin"),
            new(AnomalyKind.PeekABoo, AnomalySeverity.Funny, 0.8f, "a peek-a-boo from a closet"),
            new(AnomalyKind.Chatter, AnomalySeverity.Funny, 1.2f, "small talk", harmlessOnly: true),
            new(AnomalyKind.Whisper, AnomalySeverity.Strange, 1.2f, "saying something odd"),
            new(AnomalyKind.FakeCommand, AnomalySeverity.Strange, 0.8f, "an order you never gave"),
            new(AnomalyKind.WrongName, AnomalySeverity.Strange, 0.6f, "the wrong name"),
            new(AnomalyKind.Vanish, AnomalySeverity.Strange, 1f, "vanishing"),
            new(AnomalyKind.Noises, AnomalySeverity.Strange, 1f, "noises behind you"),
            new(AnomalyKind.WindowStare, AnomalySeverity.Strange, 0.9f, "staring out of a window"),
            new(AnomalyKind.WallStare, AnomalySeverity.Strange, 0.6f, "facing the wall"),
            new(AnomalyKind.Bloody, AnomalySeverity.Scary, 1f, "covered in blood"),
            new(AnomalyKind.ClosetAmbush, AnomalySeverity.Scary, 1f, "an ambush from a closet"),
            new(AnomalyKind.ShutDoors, AnomalySeverity.Scary, 0.7f, "shutting every door"),
            new(AnomalyKind.Statue, AnomalySeverity.Scary, 0.9f, "moving only while unseen"),
            new(AnomalyKind.Stalker, AnomalySeverity.Extreme, 1f, "the bloody stalker"),
            new(AnomalyKind.BehindYou, AnomalySeverity.Extreme, 1f, "right behind you"),
        ];

        internal static AnomalyInfo Info(AnomalyKind kind) => All[(int)kind];

        /// <summary>
        /// A kind by its name, case-insensitive, for buddy_anomaly.
        /// </summary>
        internal static AnomalyKind? Parse(string text)
        {
            foreach (AnomalyInfo info in All)
            {
                if (info.Kind.ToString().Equals(text, StringComparison.OrdinalIgnoreCase)) return info.Kind;
            }
            return null;
        }
    }
}
