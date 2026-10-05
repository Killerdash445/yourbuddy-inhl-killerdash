using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    internal static class ResourceDuty
    {
        internal static ResourceDutySettings Settings { get; private set; } = new();
        internal static ResourceErrand? Owner;
        internal static string Status = "Waiting for an enabled duty and a free buddy.";
        private static readonly Dictionary<string, (string Text, float At)> Reports = [];
        internal static bool Enabled => !Settings.Paused &&
            (Settings.Oxygen.Enabled || Settings.Fuel.Enabled || Settings.Energy.Enabled);

        internal static void Load(ResourceDutySettings? settings)
        {
            Cancel();
            Settings = settings ?? new();
            Settings.Normalize();
            Status = "Waiting for an enabled duty and a free buddy.";
            Reports.Clear();
        }

        internal static void Cancel() => Owner?.Cancel("interrupted; will check again later");
        internal static void Tick() => Owner?.Tick();
        internal static void Report(string text, string subject = "loader")
        {
            Status = text;
            Trace(text, subject);
        }
        internal static void Trace(string text, string subject)
        {
            if (Reports.TryGetValue(subject, out var previous) && previous.Text == text && Time.time < previous.At) return;
            Reports[subject] = (text, Time.time + 15f);
            YourBuddyPlugin.Log.LogInfo("[resources] " + text);
        }

        internal static string Describe()
        {
            ResourceDutySettings s = Settings;
            string result = !YourBuddyPlugin.ConfigAutonomy.Value ? "Buddy autonomy is off. Choose Start duties to enable it." : s.Paused ? "Resource duties paused." : "Resource duties ready when Buddy is free.";
            for (int i = 0; i < 3; i++)
            {
                ResourceRule r = s.Rule(i);
                result += $"\n{ResourceDutySettings.Label(i)}: {(r.Enabled ? "on" : "off")}, at/below {r.Start}% -> {r.Target}%, buy {(r.Buy ? "on" : "off")}, stock < {r.MinCells}, up to {r.BuyQuantity} cells.";
            }
            return result + $"\nShared allowance left: {s.Budget}; keep cash: {s.Reserve}.\n{Status}\n" +
                "Use Commands for resource controls, or type resources help. Orders take priority; choose Start duties to free this buddy.";
        }
    }
}
