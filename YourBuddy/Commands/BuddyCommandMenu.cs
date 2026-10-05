using System;
using System.Collections.Generic;

namespace YourBuddy
{
    // Only presentation lives here; all actions still use BuddyDialogCommands.
    internal sealed class BuddyCommandMenu
    {
        private string page = "";
        private static readonly Dictionary<string, string[]> Pages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Movement"] = ["Follow", "Stay", "Wander", "Hide", "Goto", "Decide"],
            ["Jobs"] = ["Tidy", "Sell", "Snack", "Play"],
            ["Suit & travel"] = ["Fetch suit", "Unsuit", "Outside", "Inside"]
        };

        internal IReadOnlyList<string> Commands(IReadOnlyList<string> available)
        {
            if (page.Length == 0) return ["Status", "Movement", "Jobs", "Suit & travel", "Resources", "Password"];
            List<string> result = [];
            foreach (string label in Pages[page])
            {
                foreach (string allowed in available)
                {
                    if (label == allowed) { result.Add(label); break; }
                }
            }
            result.Add("Main menu");
            return result;
        }

        internal string? Answer(string text)
        {
            text = text.Trim();
            if (text.Equals("Main menu", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                return "Choose Movement, Jobs, Suit & travel, or Resources.";
            }
            if (!Pages.ContainsKey(text)) return null;
            page = text;
            return text + " commands. Open Commands to choose an action.";
        }

        internal void Reset() => page = "";
    }
}
