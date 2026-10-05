namespace YourBuddy
{
    public sealed partial class BuddyBehaviour
    {
        // Player-facing state, without the debug HUD's navigation details. docs/dialog.md
        internal string ConversationStatus
        {
            get
            {
                if (IsDead) return "No response.";
                if (Asleep) return "Still asleep.";
                if (fearState == FearState.Scared || mode == BuddyMode.Flee)
                {
                    return "I need to get somewhere safe first.";
                }
                if (fearState == FearState.Alert) return "Something's nearby. I'm keeping watch.";
                if (Hiding) return "I'm hiding here.";
                return mode switch
                {
                    BuddyMode.Stay => "I'll wait here. Tell me when you're ready.",
                    BuddyMode.Wander => "I'm having a look around. Need me?",
                    BuddyMode.Route => "I'm on my way to finish a task. Need something?",
                    _ => "I'm with you. What do you need?"
                };
            }
        }
    }
}
