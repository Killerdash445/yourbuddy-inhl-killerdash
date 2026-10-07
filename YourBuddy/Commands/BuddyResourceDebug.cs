using System;

namespace YourBuddy
{
    internal static class BuddyResourceDebug
    {
        internal static string Drain(string[] args)
        {
            if (args.Length != 1) return "Usage: buddy_dev drain <oxygen|fuel|energy>";
            string resource = args[0].ToLowerInvariant();
            if (resource != "oxygen" && resource != "fuel" && resource != "energy")
                return "Usage: buddy_dev drain <oxygen|fuel|energy>";
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            if (ship == null) return "No player ship is available.";
            int capacity, current;
            Func<int, bool> reduce;
            switch (resource)
            {
                case "oxygen":
                    if (ship.OxygenController == null) return "Oxygen controller unavailable.";
                    capacity = ship.OxygenController.Capacity;
                    current = ship.OxygenController.RemainingOxygen;
                    reduce = ship.OxygenController.TryReduceOxygen;
                    break;
                case "fuel":
                    if (ship.FuelController == null) return "Fuel controller unavailable.";
                    capacity = ship.FuelController.Capacity;
                    current = ship.FuelController.Fuel;
                    reduce = ship.FuelController.TryConsumeFuel;
                    break;
                default:
                    if (ship.ElectricityController == null) return "Energy controller unavailable.";
                    capacity = ship.ElectricityController.Capacity;
                    current = ship.ElectricityController.Energy;
                    reduce = ship.ElectricityController.TryReduceEnergy;
                    break;
            }
            if (capacity <= 0) return "No capacity available for " + resource + ".";
            int amount = current - capacity / 5;
            if (amount <= 0) return resource + " is already at or below 20%.";
            return reduce(amount) ? resource + " drained to 20%; resource settings unchanged."
                : "Could not fully drain " + resource + "; check functional tanks or batteries.";
        }
    }
}
