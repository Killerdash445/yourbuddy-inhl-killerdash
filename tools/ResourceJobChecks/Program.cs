using System;
using YourBuddy;
using UnityEngine;
using NPC.Core;
using NPC.Core.World;
using Space.Enums;

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
// The stand point must be on the loader's side of the airlock's floor-level doors.
Airlock airlock = new() { transform = new() };
Transform outer = new() { position = new(0, 0, 4.6f), forward = new(0, 0, 1) };
Transform hatch = new() { position = new(0, 1.53f, 3.75f), forward = new(0, 0, 1) };
airlock.Children.Add(new Gate { transform = outer });
airlock.Children.Add(new Gate { transform = hatch });
loader.Components[typeof(Airlock)] = airlock;
ResourceErrand roomJob = new(body);
Check(roomJob.Count(out _) == 1, "loader with an airlock still offers refill");
Vector3 slotCenter = new(1.12f, 0.85f, 3.9f);
Check(!roomJob.InLoaderRoom(new(1.02f, 0, 4.69f), slotCenter), "stand point beyond the outer door is refused");
Check(roomJob.InLoaderRoom(new(0.32f, 0, 3.9f), slotCenter) && roomJob.InLoaderRoom(new(-0.28f, 0, 3.2f), slotCenter),
    "stand points in the chamber are allowed, whichever side of the ceiling hatch");
foreach (int value in new[] { 0, -1 })
{
    loader.Current = new ResourceContainer { Value = value };
    Check(job.Count(out _) == 1, "a spent cell does not block refilling");
}
loader.Current = new ResourceContainer { Value = 10 };
Check(job.Count(out _) == 0, "occupied loader is not a refill candidate");
Check(ResourceDuty.Status.Contains("loader occupied"), "occupied diagnostic is precise");
job.DueAt = 0;
Check(!job.TryStart(out string report) && report.Contains("loader occupied"), "start agrees with count and reports loader");
Check(loader.Ejections == 0, "player cell left untouched");
job.DueAt = 0;
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
// A spent cell is taken out once Buddy is at the loader with a charged one.
ship.OxygenController.Value = 20;
slot.Items.Clear();
Grabbable freshItem = new() { transform = new() };
ResourceContainer fresh = new() { transform = freshItem.transform };
fresh.Components[typeof(Grabbable)] = freshItem;
SceneScan.Snapshot<ResourceContainer>.Items = [fresh];
loader.Current = new ResourceContainer { Value = 0 };
int ejections = loader.Ejections;
IErrandBody ejectBody = new();
ResourceErrand ejectJob = new(ejectBody);
Check(ejectJob.TryStart(out _) && ejectBody.Leg != null, "refill starts with a spent cell in the loader");
ejectBody.Leg!.Approach(out _); // The preceding check established the fetch leg.
Check(ejectBody.Hands.Item == freshItem && ejectBody.Leg != null, "charged cell picked up for the loader");
ejectBody.Leg!.Approach(out _); // Picking up planned the insert leg.
Check(loader.Ejections == ejections + 1 && loader.Current == null && ResourceDuty.Owner == ejectJob, "spent cell ejected, run continues");
ejectJob.Cancel("check done");
ResourceDuty.Settings.Paused = false; // The stub has no floor to put the cell down on.
// A shop cell priced over the allowance is named in the report, not hidden behind "no supply".
NpcPlayer.Pilot = new Player();
Grabbable cylinder = new() { BuyPrice = 1050 };
cylinder.Components[typeof(ResourceContainer)] = new ResourceContainer { Type = ResourceType.Oxygen };
SceneScan.Snapshot<Shop>.Items = [new Shop { transform = new(), Items = [cylinder] }];
SceneScan.Snapshot<ResourceContainer>.Items = [];
ship.OxygenController.Value = 20;
ResourceDuty.Settings.Buying = true;
ResourceDuty.Settings.Budget = 100;
ResourceErrand shopJob = new(new IErrandBody());
Check(!shopJob.TryStart(out string priceReport) && priceReport.Contains("costs 1050") && priceReport.Contains("allowance is 100"),
    "price over the allowance is reported");
ResourceDuty.Settings.Buying = false;
GameManager.Instance.PlayerShip = null;
Check(BuddyResourceDebug.Drain(["fuel"]).Contains("No player ship"), "no ship handled");
Console.WriteLine($"{checks} resource job/debug checks passed.");
