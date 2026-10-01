using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What the buddy says and "was told" during an anomaly, by severity. docs/anomalies.md#3-what-it-says
    /// </summary>
    internal static class AnomalyLines
    {
        private static readonly string[] Funny =
        [
            "This room has 212 bolts. I counted.",
            "The vending machine likes me.",
            "I'm not lost. I'm exploring.",
            "If I hide in the fridge, will you find me?",
            "We should give the ship a name.",
            "I practised standing still.",
            "Space is big. Have you noticed?",
            "I licked a window. Don't tell anyone.",
        ];

        private static readonly string[] Strange =
        [
            "Did you hear that?",
            "You were asleep for a long time.",
            "I don't remember waking up.",
            "Someone was standing here.",
            "I keep counting three of us.",
            "What is my name?",
            "The window was open. Out there.",
            "Were we always two?",
        ];

        private static readonly string[] Scary =
        [
            "He wants to come in.",
            "You breathe very loudly.",
            "I was here before you.",
            "Don't look at the window.",
            "It's warm inside the suit.",
            "Don't go back to the cryo room.",
            "It knows your name now.",
        ];

        private static readonly string[] Extreme =
        [
            "I'm not the one you woke up.",
            "The other one is still in the pod.",
            "Let him in.",
            "Stop looking at me.",
            "You taste like oxygen.",
            "Your buddy says hello.",
        ];

        /// <summary>
        /// Player lines the buddy "was told", each with its reply. docs/anomalies.md#fakecommand
        /// </summary>
        private static readonly (string said, string reply)[] StrangeOrders =
        [
            ("are you awake?", "Yes."),
            ("who else is aboard?", "Just us."),
            ("wait here", "I'll wait."),
            ("don't follow me", "Okay."),
            ("count the crew", "Three."),
        ];

        private static readonly (string said, string reply)[] ScaryOrders =
        [
            ("let him in", "Okay. He's in."),
            ("open the cryo pod", "It's open."),
            ("don't tell the pilot", "I won't."),
            ("leave the airlock open", "Done."),
        ];

        private static readonly (string said, string reply)[] ExtremeOrders =
        [
            ("swap", "Done. They didn't notice."),
            ("where is the real one?", "Asleep."),
            ("let him in", "He was never out."),
        ];

        private static readonly string[] WrongNames = ["{0} 2", "B-UDDY 02", "{0} (copy)", "{0}?"];

        /// <summary>
        /// A spoken line no worse than `ceiling`, leaning toward it.
        /// </summary>
        internal static string Spoken(AnomalySeverity ceiling) => Pick(ceiling switch
        {
            AnomalySeverity.Funny => Funny,
            AnomalySeverity.Strange => Strange,
            AnomalySeverity.Scary => Random.value < 0.7f ? Scary : Strange,
            _ => Random.value < 0.7f ? Extreme : Scary,
        });

        internal static (string said, string reply) Order(AnomalySeverity ceiling)
        {
            (string, string)[] pool = ceiling switch
            {
                AnomalySeverity.Extreme => Random.value < 0.6f ? ExtremeOrders : ScaryOrders,
                AnomalySeverity.Scary => Random.value < 0.7f ? ScaryOrders : StrangeOrders,
                _ => StrangeOrders,
            };
            return pool[Random.Range(0, pool.Length)];
        }

        /// <summary>
        /// "Buddy 2" for a lone "Buddy"; a numbered one gets one of the other forms.
        /// </summary>
        internal static string WrongName(string name)
        {
            bool numbered = name.Length > 0 && char.IsDigit(name[name.Length - 1]);
            int first = numbered ? 1 : 0;
            return string.Format(WrongNames[Random.Range(first, WrongNames.Length)], name);
        }

        private static string Pick(string[] pool) => pool[Random.Range(0, pool.Length)];
    }
}
