// Only the Unity-dependent coordinator is replaced; the menu and settings are production code.
namespace YourBuddy
{
    internal static class ResourceDuty
    {
        internal static ResourceDutySettings Settings { get; } = new();
        internal static string Describe() => "Settings summary";
    }
}
