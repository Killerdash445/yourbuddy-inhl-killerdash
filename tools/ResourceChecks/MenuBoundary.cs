using System;
namespace UnityEngine { internal static class Time { internal static float time = 1; } }
namespace NPC.Core
{
    internal static class NpcEvents
    {
        internal static event Action? Tick, WorldReset;
        internal static event Action<bool>? GameStarting;
        internal static void ResetWorld() => WorldReset?.Invoke();
        internal static void Start(bool newGame) => GameStarting?.Invoke(newGame);
        internal static void Frame() => Tick?.Invoke();
    }
}
namespace YourBuddy
{
    internal sealed class ResourceErrand
    {
        internal bool Cancelled;
        internal void Cancel(string why) { Cancelled = true; ResourceDuty.Owner = null; }
        internal void Tick() { }
    }
    internal static class YourBuddyPlugin
    {
        internal static readonly Logger Log = new();
        internal sealed class Logger { internal void LogInfo(string text) { } }
    }
}
