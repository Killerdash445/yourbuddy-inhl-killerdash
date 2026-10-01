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
    /// Every anomaly the buddy can act out, each once per save. docs/anomalies.md#2-the-anomalies
    /// </summary>
    internal enum AnomalyKind
    {
        Spin,
        PeekABoo,
        Whisper,
        FakeCommand,
        WrongName,
        Vanish,
        Noises,
        WindowStare,
        WallStare,
        BotTalk,
        Bloody,
        ClosetAmbush,
        ShutDoors,
        Statue,
        Meat,
        Pipe,
        Move,
        Shadow,
        Smile,
        Stalker,
        BehindYou,
        UnderTheSuit,
        Sleeper
    }

    /// <summary>
    /// One row of the catalogue: its severity, how often it is drawn among its peers, and what to call it.
    /// </summary>
    internal readonly struct AnomalyInfo(AnomalyKind kind, AnomalySeverity severity, float weight, string name)
    {
        public readonly AnomalyKind Kind = kind;
        public readonly AnomalySeverity Severity = severity;
        public readonly float Weight = weight;
        public readonly string Name = name;
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
            new(AnomalyKind.Whisper, AnomalySeverity.Strange, 1.2f, "saying something odd"),
            new(AnomalyKind.FakeCommand, AnomalySeverity.Strange, 0.8f, "an order you never gave"),
            new(AnomalyKind.WrongName, AnomalySeverity.Strange, 0.6f, "the wrong name"),
            new(AnomalyKind.Vanish, AnomalySeverity.Strange, 1f, "vanishing"),
            new(AnomalyKind.Noises, AnomalySeverity.Strange, 1f, "noises behind you"),
            new(AnomalyKind.WindowStare, AnomalySeverity.Strange, 0.9f, "staring out of a window"),
            new(AnomalyKind.WallStare, AnomalySeverity.Strange, 0.6f, "facing the wall"),
            // Fits only while you are on the ship side of the docked Shipyard: when it does, it should win.
            new(AnomalyKind.BotTalk, AnomalySeverity.Strange, 2.5f, "talking with the Shipyard's robot"),
            new(AnomalyKind.Bloody, AnomalySeverity.Scary, 1f, "covered in blood"),
            new(AnomalyKind.ClosetAmbush, AnomalySeverity.Scary, 1f, "an ambush from a closet"),
            new(AnomalyKind.ShutDoors, AnomalySeverity.Scary, 0.7f, "shutting every door"),
            new(AnomalyKind.Statue, AnomalySeverity.Scary, 0.9f, "moving only while unseen"),
            // Fits only at the docked Shipyard, away from its cryo room: when it does, it should win.
            new(AnomalyKind.Meat, AnomalySeverity.Scary, 1.5f, "caught in the cryo room"),
            new(AnomalyKind.Pipe, AnomalySeverity.Scary, 1f, "carrying a bloody pipe"),
            // Fits only while it is left on another station than yours: when it does, it should win.
            new(AnomalyKind.Move, AnomalySeverity.Scary, 2f, "turning up far from where you left it"),
            // Fits only at the Fuel, Oxygen or Solar station: when it does, it should win.
            new(AnomalyKind.Shadow, AnomalySeverity.Scary, 2f, "a dark double running through the station"),
            new(AnomalyKind.Smile, AnomalySeverity.Scary, 0.9f, "a bloody smile, for a blink"),
            new(AnomalyKind.Stalker, AnomalySeverity.Extreme, 1f, "the bloody stalker"),
            new(AnomalyKind.BehindYou, AnomalySeverity.Extreme, 1f, "right behind you"),
            new(AnomalyKind.UnderTheSuit, AnomalySeverity.Extreme, 0.9f, "something under the suit, for a blink"),
            new(AnomalyKind.Sleeper, AnomalySeverity.Extreme, 1.2f, "still asleep in its cryo capsule"),
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
