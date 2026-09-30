using NPC.Core.Agents;

namespace YourBuddy
{
    /// <summary>
    /// What one buddy's NpcAgent may do, read live from YourBuddy's config - and from the buddy's
    /// suit state, which is why every buddy gets its own instance. docs/eva.md
    /// </summary>
    internal sealed class BuddyAgentSettings(BuddyBehaviour? buddy) : NpcAgentSettings
    {
        public override float MoveSpeed => YourBuddyPlugin.ConfigMoveSpeed.Value;
        public override bool CanOpenDoors => YourBuddyPlugin.ConfigAutoDoors.Value;
        public override bool KeepOutOfSpace => YourBuddyPlugin.ConfigPreventSpace.Value;
        public override bool Mortal => YourBuddyPlugin.ConfigMortal.Value;
        public override bool DebugVisuals => YourBuddyPlugin.ConfigDebugVisuals.Value;

        // A suited buddy breathes its own air and may route through Outdoor nodes: that is what
        // the suit means. docs/eva.md
        public override bool Suited => buddy is { } b && b.SuitSuited;
        public override bool MayGoOutside => Suited;

        // The worn suit slows it and stops its jumps, as it does the player (PlayerController). docs/eva.md
        public override float SpeedFactor => buddy is { } b ? b.SuitSpeedFactor : 1f;
        public override bool CanJump => !Suited;
    }
}
