using System;
using System.Globalization;

namespace YourBuddy
{
    public sealed class ResourceRule
    {
        public bool Enabled { get; set; }
        public bool Buy { get; set; }
        public int Start { get; set; } = 30;
        public int Target { get; set; } = 80;

        public int MinCells { get; set; } = 1;
        public int BuyQuantity { get; set; } = 1;

        internal bool NeedsRefill(float percent, bool pending) =>
            Enabled && !float.IsNaN(percent) && percent < Target && (pending || percent <= Start);

        internal void Normalize()
        {
            if (MinCells < 1 || MinCells > 100 || BuyQuantity < 1 || BuyQuantity > 100)
            {
                Buy = false;
                MinCells = BuyQuantity = 1;
            }
            if (Start < 0 || Start >= Target || Target > 100)
            {
                Enabled = Buy = false;
                Start = 30;
                Target = 80;
            }
        }
    }

    // Shared by the save, not multiplied by the number of buddies. docs/resources.md
    public sealed class ResourceDutySettings
    {
        public bool Paused { get; set; }
        public int Budget { get; set; }
        public int Reserve { get; set; }
        public ResourceRule Oxygen { get; set; } = new();
        public ResourceRule Fuel { get; set; } = new();
        public ResourceRule Energy { get; set; } = new();

        internal ResourceRule Rule(int kind) => kind switch { 0 => Oxygen, 1 => Fuel, _ => Energy };
        internal static string Label(int kind) => kind switch { 0 => "oxygen", 1 => "fuel", _ => "energy" };
        internal bool CanBuy(ResourceRule rule, int price, int cash) =>
            !Paused && rule.Enabled && rule.Buy && price > 0 && price <= Budget &&
            (long)cash - price >= Reserve;

        internal int AffordableQuantity(ResourceRule rule, int price, int cash, int requested) =>
            !CanBuy(rule, price, cash) ? 0 : (int)Math.Min(Math.Max(0, requested),
                Math.Min(Budget / price, ((long)cash - Reserve) / price));

        internal void Normalize()
        {
            Oxygen ??= new();
            Fuel ??= new();
            Energy ??= new();
            Oxygen.Normalize();
            Fuel.Normalize();
            Energy.Normalize();
            Budget = Math.Max(0, Budget);
            Reserve = Math.Max(0, Reserve);
        }

        // Strict, whole commands: never pass malformed settings on to the action parser.
        internal string Edit(string text)
        {
            string[] words = text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2)
            {
                if (words[1] == "pause") { Paused = true; return "Resource duties paused."; }
                if (words[1] == "resume") { Paused = false; return "Resource duties resumed."; }
            }
            if (words.Length == 3 && int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
            {
                if (words[1] == "budget") { Budget = amount; return $"Remaining purchase allowance: {Budget}."; }
                if (words[1] == "reserve") { Reserve = amount; return $"Keep at least {Reserve} cash."; }
            }
            int kind = words.Length > 1 ? Array.IndexOf(new[] { "oxygen", "fuel", "energy" }, words[1]) : -1;
            if (kind >= 0)
            {
                ResourceRule rule = Rule(kind);
                if (words.Length == 3 && (words[2] == "on" || words[2] == "off"))
                {
                    rule.Enabled = words[2] == "on";
                    return $"{Label(kind)} duty {(rule.Enabled ? "enabled" : "disabled")}.";
                }
                if (words.Length == 4 && words[2] == "buy" && (words[3] == "on" || words[3] == "off"))
                {
                    rule.Buy = words[3] == "on";
                    return $"{Label(kind)} purchases {(rule.Buy ? "allowed within your limits" : "disabled")}.";
                }
                if (words.Length == 4 && int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                {
                    if (words[2] == "stock" || words[2] == "quantity")
                    {
                        if (value < 1 || value > 100) return "Use a cell count from 1 to 100.";
                        if (words[2] == "stock") rule.MinCells = value;
                        else rule.BuyQuantity = value;
                        return $"{Label(kind)}: restock below {rule.MinCells} usable cells; buy up to {rule.BuyQuantity} per run.";
                    }
                    if (words[2] == "start" && value >= 0 && value < rule.Target) rule.Start = value;
                    else if (words[2] == "target" && value > rule.Start && value <= 100) rule.Target = value;
                    else return "Use percentages with 0 <= start < target <= 100.";
                    return $"{Label(kind)}: start at or below {rule.Start}%, refill towards {rule.Target}%.";
                }
            }
            return "Use: resources oxygen on/off; resources oxygen start 30; resources oxygen target 80; " +
                   "resources oxygen buy on/off; resources oxygen stock 2; resources oxygen quantity 3. Fuel and energy work too. " +
                   "Shared limits: resources budget 500; resources reserve 100. Resources pause/resume.";
        }
    }
}
