using System;
using YourBuddy;
using UnityEngine;
using NPC.Core;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
ItemDetector slot = new() { transform = new() };
slot.Components[typeof(BoxCollider)] = new BoxCollider();
ResourceController loader = new() { transform = new(), Slot = slot };
SpaceShip ship = new() { CellController = loader };
GameManager.Instance.PlayerShip = ship;
ship.OxygenController.Value = 20;
ResourceDuty.Settings.Oxygen.Enabled = true;
IErrandBody body = new();
ResourceErrand job = new(body);
Check(job.Count(out _) == 1, "empty initialized loader offers refill");
foreach (int value in new[] { 0, -1, 10 })
{
    loader.Current = new ResourceContainer { Value = value };
    Check(job.Count(out _) == 0, "occupied loader is not a refill candidate");
    Check(ResourceDuty.Status.Contains(value <= 0 ? "spent cell" : "loader occupied"), "occupied diagnostic is precise");
    job.DueAt = 0;
    Check(!job.TryStart(out string report) && report.Contains("loader occupied"), "start agrees with count and reports loader");
    Check(loader.Ejections == 0, "player cell left untouched");
    job.DueAt = 0;
}
ResourceDuty.Settings.Buying = true;
Check(job.Count(out _) == 1, "occupied loader does not block independent restocking");
ResourceDuty.Settings.Buying = false;
loader.Current = null;
loader.Initialized = false;
Check(job.Count(out _) == 0 && !job.TryStart(out _), "uninitialized loader rejected by both entry points");
loader.Initialized = true;
job.DueAt = 0;
slot.isActiveAndEnabled = false;
Check(job.Count(out _) == 0 && !job.TryStart(out _), "unavailable slot rejected by both entry points");
slot.isActiveAndEnabled = true;
job.DueAt = 0;
ResourceDuty.Owner = job;
ResourceDuty.Status = "Refilling oxygen";
Check(job.Describe().StartsWith("active - Refilling oxygen"), "active owner HUD is active");
Check(job.Count(out _) == 0, "active owner cannot be scheduled twice");
Check(new ResourceErrand(new()).Describe().Contains("another buddy"), "other buddy HUD waits for owner");
ResourceDuty.Owner = null;
ResourceDuty.Settings.Oxygen.Enabled = false;
Check(job.Describe().Contains("Resources page") && !job.Describe().Contains("still works"), "disabled HUD points to actual controls");
ResourceDuty.Settings.Oxygen.Enabled = true;
body.IsOutside = true;
Check(job.Describe().Contains("come inside") && job.Count(out _) == 0, "outside buddy waits indoors");
body.IsOutside = false;
// A cell staged after the cached scan must be rejected when Buddy arrives.
Grabbable item = new() { transform = new() };
ResourceContainer cell = new() { transform = item.transform };
cell.Components[typeof(Grabbable)] = item;
SceneScan.Snapshot<ResourceContainer>.Items = [cell];
SceneScan.Snapshot<ItemDetector>.Items = [slot];
Check(job.TryStart(out _), "available cell starts production job");
Check(body.Leg != null, "fetch leg started");
slot.Items.Add(item);
body.Leg!.Approach(out _); // The preceding check established a leg.
Check(body.Hands.Item == null && ResourceDuty.Owner == null, "newly staged cell is not picked up from another slot");
foreach (string resource in new[] { "oxygen", "fuel", "energy" })
{
    Reserve reserve = resource == "oxygen" ? ship.OxygenController : resource == "fuel" ? ship.FuelController : ship.ElectricityController;
    reserve.Value = 100;
    Check(BuddyResourceDebug.Drain([resource]).Contains("drained to 20%") && reserve.Value == 20 && reserve.LastAmount == 80, "drain uses selected game API");
    int calls = reserve.Calls;
    Check(BuddyResourceDebug.Drain([resource]).Contains("already") && reserve.Calls == calls, "already low reserves are unchanged");
    reserve.Value = 10;
    BuddyResourceDebug.Drain([resource]);
    Check(reserve.Value == 10 && reserve.Calls == calls, "drain never refills a lower reserve");
    reserve.Value = 100;
    reserve.Accept = false;
    Check(BuddyResourceDebug.Drain([resource]).Contains("Could not fully drain"), "failed game operation reported");
    reserve.Accept = true;
    reserve.Capacity = 0;
    Check(BuddyResourceDebug.Drain([resource]).Contains("No capacity"), "zero capacity rejected");
    reserve.Capacity = 100;
}
Check(BuddyResourceDebug.Drain([]).StartsWith("Usage"), "missing argument rejected");
Check(BuddyResourceDebug.Drain(["food"]).StartsWith("Usage"), "unknown resource rejected");
Check(BuddyResourceDebug.Drain(["fuel", "extra"]).StartsWith("Usage"), "extra argument rejected");
ship.FuelController.Value = 100;
Check(BuddyResourceDebug.Drain(["FUEL"]).Contains("drained"), "resource argument is case insensitive");
GameManager.Instance.PlayerShip = null;
Check(BuddyResourceDebug.Drain(["fuel"]).Contains("No player ship"), "no ship handled");
Console.WriteLine($"{checks} resource job/debug checks passed.");
