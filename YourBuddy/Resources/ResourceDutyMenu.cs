using System;
using System.Collections.Generic;
using System.Globalization;

namespace YourBuddy
{
    internal sealed class ResourceDutyMenu(Func<string>? start = null, Func<string>? readiness = null)
    {
        private bool open;
        private int selected = -1;
        private string section = "";
        private string? editing;

        // NPC.Core submits the displayed label; keep the full command behind that label.
        private readonly Dictionary<string, string> buttons = new(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<string> Commands(IReadOnlyList<string> normal)
        {
            buttons.Clear();
            if (!open) return normal;
            List<string> labels = [];
            void Add(string label, string command)
            {
                labels.Add(label);
                buttons[label] = command;
            }
            ResourceDutySettings s = ResourceDuty.Settings;
            if (editing != null)
            {
                Add("Cancel edit", "resources cancel");
                return labels;
            }
            if (selected < 0)
            {
                Add("State", "resources state");
                Add("Start duties", "resources run");
                Add("Oxygen", "resources oxygen");
                Add("Fuel", "resources fuel");
                Add("Energy", "resources energy");
                Add(s.Paused ? "Resume duties" : "Pause duties", s.Paused ? "resources resume" : "resources pause");
                Add("Set budget", "resources edit budget");
                Add("Keep cash", "resources edit reserve");
                Add("Help", "resources help");
            }
            else
            {
                string prefix = "resources " + ResourceDutySettings.Label(selected);
                ResourceRule rule = s.Rule(selected);
                if (section == "refill")
                {
                    Add($"Start: {rule.Start}%", prefix + " edit start");
                    Add($"Target: {rule.Target}%", prefix + " edit target");
                }
                else if (section == "stock")
                {
                    Add(rule.Buy ? "Stop buying" : "Allow buying", prefix + (rule.Buy ? " buy off" : " buy on"));
                    Add($"Stock < {rule.MinCells}", prefix + " edit stock");
                    Add($"Buy up to {rule.BuyQuantity}", prefix + " edit quantity");
                }
                else
                {
                    Add(rule.Enabled ? "Disable duty" : "Enable duty", prefix + (rule.Enabled ? " off" : " on"));
                    Add("Refilling", prefix + " refill");
                    Add("Restocking", prefix + " stock");
                    Add("State", "resources state");
                    Add("Start duties", "resources run");
                }
                if (section.Length > 0) Add("Resource menu", prefix);
                Add("All resources", "resources");
            }
            Add("Orders", "resources back");
            return labels;
        }

        internal string? Answer(string text)
        {
            text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (open && buttons.TryGetValue(text, out string? commandText)) text = commandText;
            if (editing != null && !text.StartsWith("resources", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    editing = null;
                    return "Edit cancelled.";
                }
                string number = text.TrimEnd('%').Trim();
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return "Enter a whole number, or type cancel.";
                string reply = ResourceDuty.Settings.Edit(editing + " " + number);
                if (!reply.StartsWith("Use", StringComparison.Ordinal)) editing = null;
                return reply;
            }
            if (!text.Equals("resources", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("resources ", StringComparison.OrdinalIgnoreCase)) return null;
            buttons.Clear();
            open = true;
            string command = text.ToLowerInvariant();
            editing = null;
            if (command == "resources run") return start != null ? start() : "Start duties is unavailable.";
            if (command == "resources state") return Summary();
            if (command == "resources cancel") return "Edit cancelled.";
            string[] words = command.Split(' ');
            string? field = words.Length == 3 && words[1] == "edit" ? words[2] :
                words.Length == 4 && words[2] == "edit" ? words[3] : null;
            if (field != null)
            {
                bool shared = words.Length == 3;
                int kind = shared ? -1 : Array.IndexOf(new[] { "oxygen", "fuel", "energy" }, words[1]);
                if ((shared && (field == "budget" || field == "reserve")) ||
                    (kind >= 0 && (field == "start" || field == "target" || field == "stock" || field == "quantity")))
                {
                    editing = shared ? "resources " + field : "resources " + words[1] + " " + field;
                    string setting = field switch
                    {
                        "start" => "refill start percentage (at or below)",
                        "target" => "refill target percentage",
                        "stock" => "minimum usable cells aboard; restock below this count",
                        "quantity" => "maximum cells to buy per restock run",
                        "budget" => "shared remaining spending allowance",
                        _ => "minimum cash to keep"
                    };
                    return $"Type the {setting} in the conversation box, then press Enter. Type cancel to go back.";
                }
            }
            if (command == "resources back") { Reset(); return "Back to companion commands."; }
            if (command == "resources") { selected = -1; section = ""; return Summary(); }
            for (int i = 0; i < 3; i++)
            {
                string prefix = "resources " + ResourceDutySettings.Label(i);
                if (command != prefix && command != prefix + " refill" && command != prefix + " stock") continue;
                selected = i;
                section = command == prefix + " refill" ? "refill" : command == prefix + " stock" ? "stock" : "";
                return PageSummary();
            }
            return ResourceDuty.Settings.Edit(text) + "\n" + PageSummary();
        }

        private string PageSummary()
        {
            if (selected < 0) return Summary();
            ResourceRule rule = ResourceDuty.Settings.Rule(selected);
            string title = ResourceDutySettings.Label(selected) + (rule.Enabled ? " duty enabled." : " duty disabled.");
            string detail = section switch
            {
                "refill" => $"Refill the ship tank at {rule.Start}% or lower, towards {rule.Target}%. Click Start or Target to type a percentage.",
                "stock" => $"Buying {(rule.Buy ? "allowed" : "off")}. Below {rule.MinCells} usable cells aboard, bring back up to {rule.BuyQuantity}. Click a count to edit it. Purchases use the shared budget and keep-cash limit.",
                _ => "Refilling controls the ship tank percentage. Restocking controls spare cells aboard. Enable duty, set your limits, then choose Start duties and close the conversation."
            };
            return title + "\n" + detail;
        }

        private string Summary() => ResourceDuty.Describe() + (readiness != null ? "\n" + readiness() : "");

        internal void Reset() { open = false; selected = -1; section = ""; editing = null; buttons.Clear(); }
    }
}
