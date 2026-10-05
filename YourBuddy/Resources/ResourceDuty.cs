using System.Collections.Generic;
using NPC.Core;
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

        internal static void RegisterLifecycle()
        {
            NpcEvents.Tick += Tick;
            NpcEvents.WorldReset += Cancel;
            NpcEvents.GameStarting += OnGameStarting;
        }

        internal static void OnGameStarting(bool newGame)
        {
            if (newGame) Load(null);
        }
    }
}
