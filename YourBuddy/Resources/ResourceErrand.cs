using System;
using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.Agents;
using NPC.Core.World;
using Space;
using Space.Enums;
using UnityEngine;

namespace YourBuddy
{
    // One slot, one owner; each handoff remains a normal reach/carry leg. docs/resources.md
    internal sealed class ResourceErrand(IErrandBody body)
    {
        private readonly SkipList skipped = new();
        private readonly List<Vector3> storagePoints = [];
        private int storageAttempts;
        private string? storageFailure;
        private readonly List<Vector3> rejectedStorage = [];
        private Grabbable? awaitingSafeDrop;
        private bool endReported;
        internal void CheckSoon() => nextCheck = 0;

        private readonly bool[] needed = new bool[3];
        private readonly HashSet<int> beforePurchase = [];
        private readonly HashSet<Grabbable> stagedItems = [];
        private float nextCheck;
        private float deadline;
        private int kind;
        private int nextKind;
        private SpaceShip? ship;
        private ResourceController? loader;
        private ResourceContainer? cell;
        private ItemDetector? slot;
        private bool startedLoading;
        private bool finishing;
        private bool restocking;
        private int stockGoal;
        private int purchasesMade;
        private int PurchasesLeft => Math.Max(0, Rule.BuyQuantity - purchasesMade);
        private ResourceRule Rule => ResourceDuty.Settings.Rule(kind);
        private static ResourceType TypeOf(int index) => index switch
        {
            0 => ResourceType.Oxygen, 1 => ResourceType.Fuel, _ => ResourceType.Energy
        };

        private bool Fits(ResourceContainer candidate)
        {
            if (ship == null || candidate.LoadingSpeed <= 0) return false;
            float remaining = kind switch
            {
                0 => ship.OxygenController.Capacity - ship.OxygenController.RemainingOxygen,
                1 => ship.FuelController.Capacity - ship.FuelController.Fuel,
                _ => ship.ElectricityController.Capacity - ship.ElectricityController.Energy
            };
            return remaining >= candidate.LoadingSpeed;
        }

        private float Percent(int index)
        {
            if (ship == null) return 100;
            float capacity = index switch
            {
                0 => ship.OxygenController.Capacity, 1 => ship.FuelController.Capacity, _ => ship.ElectricityController.Capacity
            };
            float value = index switch
            {
                0 => ship.OxygenController.RemainingOxygen, 1 => ship.FuelController.Fuel, _ => ship.ElectricityController.Energy
            };
            return capacity > 0 ? 100f * value / capacity : 100;
        }

        internal bool TryStart()
        {
            if (awaitingSafeDrop != null && body.Hands.Item == awaitingSafeDrop)
            {
                if (Time.time < nextCheck) return false;
                nextCheck = Time.time + 5f;
                PutDownSafely();
                awaitingSafeDrop = body.Hands.Item;
                return false;
            }
            awaitingSafeDrop = null;
            if (ResourceDuty.Owner != null || Time.time < nextCheck || !ResourceDuty.Enabled ||
                body.IsOutside || body.Hands.Item != null) return false;
            nextCheck = Time.time + 30f;
            skipped.Prune();
            ship = GameManager.Instance.PlayerShip;
            if (ship == null || !GameInternals.ResourceAccess.Ready) return Waiting("ship or loader support unavailable");
            if (!body.IsAboardPlayerShip() && string.IsNullOrEmpty(ship.Autopilot.DockedStation))
                return Waiting("Buddy must be aboard or at the docked station");
            loader = ship.CellController;
            if (loader == null || !loader.Initialized) return Waiting("ship loader is not initialized");
            slot = GameInternals.ResourceAccess.Slot(loader);
            if (slot == null || !Items.Loadable(slot)) return Waiting("ship loader room is unavailable");
            body.LoadRoomOf(slot.transform);
            for (int n = 0; n < 3; n++)
            {
                kind = (nextKind + n) % 3;
                if (!Rule.Enabled) continue;
                needed[kind] = Rule.NeedsRefill(Percent(kind), needed[kind]);
                restocking = false;
                purchasesMade = 0;
                storageAttempts = 0;
                rejectedStorage.Clear();
                storageFailure = null;
                if (needed[kind] && GameInternals.ResourceAccess.Current(loader) == null &&
                    Plan(new Leg(this, slot.GetComponent<BoxCollider>().bounds.center, loader.transform, Phase.Insert), false) &&
                    FindCell()) { Start(); return true; }
                int stock = ShipStock();
                if (Rule.Buy && stock < Rule.MinCells)
                {
                    restocking = true;
                    stockGoal = stock + Rule.BuyQuantity;
                    if (FindCell() || FindShop()) { Start(); return true; }
                }
                if (needed[kind] || (Rule.Buy && stock < Rule.MinCells))
                    ResourceDuty.Report(storageFailure ?? $"Waiting: no reachable {ResourceDutySettings.Label(kind)} supply or affordable shop; {stock} usable cells aboard.", ResourceDutySettings.Label(kind));
            }
            return false;
        }

        private bool Waiting(string reason)
        {
            ResourceDuty.Report("Waiting: " + reason + ".", "scheduler");
            return false;
        }

        private void Start()
        {
            ResourceDuty.Owner = this;
            endReported = false;
            deadline = Time.time + 240f;
            startedLoading = false;
            nextKind = (kind + 1) % 3;
            if (restocking) ResourceDuty.Report($"Restocking {ResourceDutySettings.Label(kind)}: up to {PurchasesLeft} cells.", ResourceDutySettings.Label(kind));
            else ResourceDuty.Report($"Refilling {ResourceDutySettings.Label(kind)} at {Percent(kind):0.0}% towards {Rule.Target}%.", ResourceDutySettings.Label(kind));
        }

        private int ShipStock()
        {
            int count = 0;
            SceneScan.MayRescan(mustScan: true);
            foreach (ResourceContainer candidate in UnityEngine.Object.FindObjectsOfType<ResourceContainer>(true))
            {
                if (candidate == null || candidate.Data == null || candidate.Type != TypeOf(kind) || candidate.Value <= 0) continue;
                if (NpcVessels.OwnerOfTransform(candidate.transform) != NavGraph.ShipOwner) continue;
                if (Items.Loadable(candidate) || (loader != null && GameInternals.ResourceAccess.Current(loader) == candidate)) count++;
            }
            return count;
        }

        private bool FindCell()
        {
            stagedItems.Clear();
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>()) stagedItems.UnionWith(detector.Items);
            List<ResourceContainer> candidates = [];
            // Only on a scheduled search or immediately before purchase, never in the movement loop.
            SceneScan.MayRescan(mustScan: true);
            foreach (ResourceContainer candidate in UnityEngine.Object.FindObjectsOfType<ResourceContainer>(true))
            {
                if (candidate == null || candidate.Data == null || candidate.Type != TypeOf(kind) || candidate.Value <= 0 || !Items.Loadable(candidate)) continue;
                if (restocking && NpcVessels.OwnerOfTransform(candidate.transform) == NavGraph.ShipOwner) continue;
                if (skipped.Has(candidate.transform)) continue;
                if (!restocking && !Fits(candidate)) continue;
                Grabbable item = candidate.GetComponent<Grabbable>();
                if (item == null || stagedItems.Contains(item) || item.IsGrabbed || item.restrictGrab || body.TakenByAnother(item.transform)) continue;
                candidates.Add(candidate);
            }
            // Use partially spent cells first, preserving full cells when possible.
            candidates.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) :
                (a.transform.position - body.Transform.position).sqrMagnitude.CompareTo(
                    (b.transform.position - body.Transform.position).sqrMagnitude));
            int attempts = 0;
            foreach (ResourceContainer candidate in candidates)
            {
                if (attempts++ >= 8) break;
                body.LoadRoomOf(candidate.transform);
                Grabbable item = candidate.GetComponent<Grabbable>();
                if (Items.TakeBlocker(item, body.Hands.Item) != null) continue;
                if (restocking && !PlanStorage(false, item)) continue;
                Leg leg = new(this, Items.ItemTop(item), candidate.transform, Phase.Fetch);
                if (!Plan(leg, true)) continue;
                cell = candidate;
                return true;
            }
            return false;
        }

        private bool FindShop()
        {
            Player? player = NpcPlayer.Pilot;
            if (player == null || !GameInternals.ShopAccess.Ready || !Rule.Buy) return false;
            SceneScan.MayRescan(mustScan: true);
            foreach (Shop shop in UnityEngine.Object.FindObjectsOfType<Shop>(true))
            {
                if (shop == null || !Items.Loadable(shop) || skipped.Has(shop.transform)) continue;
                Grabbable[]? stock = GameInternals.ShopAccess.Stock(shop);
                if (stock == null) continue;
                for (int i = 0; i < stock.Length; i++)
                {
                    Grabbable product = stock[i];
                    if (product == null || !product.TryGetComponent(out ResourceContainer resource) ||
                        resource.Type != TypeOf(kind) || (!restocking && !Fits(resource)) || !ResourceDuty.Settings.CanBuy(Rule, product.BuyPrice, player.CashSystem.Cash)) continue;
                    body.LoadRoomOf(shop.transform);
                    if (restocking && !PlanStorage(false, product)) continue;
                    Leg leg = new(this, shop.transform.position, shop.transform, Phase.Buy) { Shop = shop, Product = i };
                    if (Plan(leg, true)) return true;
                }
            }
            return false;
        }

        private bool Plan(Leg leg, bool walk)
        {
            if (body.InReach(leg))
            {
                leg.Node = leg.StandPoint = body.Transform.position;
                if (walk) body.Walk(leg, null);
                return true;
            }
            string? failure = body.PlanReach(leg, out NavPath plan);
            if (failure != null)
            {
                skipped.Skip(leg.Own, 60f);
                ResourceDuty.Report($"Cannot reach {leg.Name}: {failure}; trying another target.", leg.Name);
                return false;
            }
            if (walk) body.Walk(leg, plan);
            return true;
        }

        private bool PlanStorage(bool walk, Grabbable? prospective = null)
        {
            if (loader == null || storageAttempts >= 3) return false;
            if (walk) storageAttempts++;
            Vector3 size = body.Hands.Item != null ? body.Hands.Extents : Vector3.one * .15f;
            if (prospective != null && NpcHands.ColliderBounds(prospective.gameObject, out Bounds bounds) && bounds.size.sqrMagnitude > .001f)
                size = bounds.extents;
            Vector3 half = ResourceStorage.Clearance(size);
            ResourceStorage.Candidates(loader.transform.position, storagePoints);
            int plans = 0;
            int floors = 0, clear = 0;
            string blocker = "no supported ship floor near active nodes";
            foreach (Vector3 candidate in storagePoints)
            {
                if (!ResourceStorage.FindFloor(candidate, out RaycastHit floor)) continue;
                floors++;
                Room? room = floor.collider.GetComponentInParent<Room>();
                if (room != null) body.LoadRoomOf(room.ContentParent);
                Vector3 point = floor.point + Vector3.up * (half.y + .03f);
                if (rejectedStorage.Exists(p => (p - point).sqrMagnitude < .36f)) continue;
                Transform? carried = body.Hands.Item != null ? body.Hands.Item.transform : prospective != null ? prospective.transform : null;
                if (!ResourceStorage.Clear(point, half, carried)) { blocker = ResourceStorage.LastBlocker; continue; }
                clear++;
                Leg leg = new(this, point, loader.transform, Phase.Deliver)
                {
                    Floor = floor.collider.transform,
                    FloorPoint = floor.collider.transform.InverseTransformPoint(point)
                };
                if (++plans > 12) break;
                if (!body.InReach(leg))
                {
                    string? failure = body.PlanReach(leg, out NavPath route);
                    if (failure != null) { blocker = failure; continue; }
                    if (walk) body.Walk(leg, route);
                }
                else
                {
                    leg.Node = leg.StandPoint = body.Transform.position;
                    if (walk) body.Walk(leg, null);
                }
                if (walk) YourBuddyPlugin.Log.LogInfo($"[resources] Carrying cell to clear ship storage at {point}");
                return true;
            }
            storageFailure = clear > 0 ? "Waiting: clear storage exists, but Buddy cannot reach it. Check the ship's doors and approach." :
                "Waiting: no clear floor storage found aboard. Make room for a spare cell.";
            ResourceDuty.Trace($"Storage scan: {floors} supported, {clear} clear, {plans} route checks; half-size {half}; {blocker}.", "storage scan");
            ResourceDuty.Report(storageFailure, "storage");
            return false;
        }

        private void PutDownSafely()
        {
            if (body.Hands.Item == null) return;
            Vector3 half = ResourceStorage.Clearance(body.Hands.Extents);
            Vector3 origin = body.Transform.position;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                Vector3 sample = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .9f;
                if (!ResourceStorage.FindFloor(sample, out RaycastHit floor, shipOnly: false)) continue;
                Vector3 point = floor.point + Vector3.up * (half.y + .03f);
                if (!ResourceStorage.Clear(point, half, body.Hands.Item.transform, shipOnly: false) ||
                    !NavProbe.WalkLos(origin, sample, .3f)) continue;
                body.Hands.PutDown(point, Vector3.zero);
                return;
            }
            ResourceDuty.Settings.Paused = true;
            awaitingSafeDrop = body.Hands.Item;
            ResourceDuty.Report("Duties paused: no clear floor nearby. Move Buddy to open floor, then choose Start duties to retry putting down the cell.", "storage");
        }

        internal void Tick()
        {
            if (ResourceDuty.Owner != this) return;
            if (body.Transform == null) { End(); return; }
            if (ResourceDuty.Settings.Paused || !Rule.Enabled || !YourBuddyPlugin.ConfigAutonomy.Value ||
                ship == null || loader == null || body.Leg is not Leg || !body.OnRoute || Time.time > deadline)
            {
                Cancel("paused, unavailable, or timed out");
                return;
            }
            if (restocking && !Rule.Buy) { Cancel("purchase permission disabled"); return; }
            if (!restocking && Percent(kind) >= Rule.Target) { needed[kind] = false; Cancel("target reached"); }
        }

        internal void Cancel(string why)
        {
            if (ResourceDuty.Owner != this || finishing) return;
            using NpcRegistry.ActingScope scope = NpcRegistry.Acting(
                body.Transform != null ? body.Transform.GetComponent<NpcAgent>() : null);
            endReported = true;
            ResourceDuty.Report(ResourceDutySettings.Label(kind) + ": " + why, ResourceDutySettings.Label(kind));
            if (body.Transform != null && body.Leg is Leg) body.FinishRoute();
            else End();
        }

        private void End()
        {
            if (finishing) return;
            finishing = true;
            if (!endReported && ResourceDuty.Owner == this)
                ResourceDuty.Report("Resource run interrupted; checking again shortly.", "scheduler");
            try
            {
                // Eject only our own cell, including before the game's save snapshot.
                if (loader != null && cell != null && GameInternals.ResourceAccess.Current(loader) == cell)
                    loader.TryTakeOut();
                if (cell != null && body.Hands.Item == cell.GetComponent<Grabbable>()) PutDownSafely();
            }
            finally
            {
                cell = null;
                startedLoading = false;
                if (ResourceDuty.Owner == this) ResourceDuty.Owner = null;
                nextCheck = Time.time + 30f;
                finishing = false;
            }
        }

        private Vector3 Approach(Leg leg, out bool wantMove)
        {
            wantMove = false;
            Tick();
            if (ResourceDuty.Owner != this) return Vector3.zero;
            if (loader == null || slot == null) return Stop("loader unavailable");
            ResourceContainer? current = GameInternals.ResourceAccess.Current(loader);
            if (!restocking && current != null && current != cell) return Stop("the player is using the loader");
            if (leg.Stage == Phase.Insert && cell != null && current == cell)
            {
                if (body.Hands.Item == cell.GetComponent<Grabbable>()) body.Hands.Release();
                if (cell.Value <= 0) return Stop("cell empty; checking again shortly");
                if (!startedLoading)
                {
                    if (!Fits(cell)) return Stop("not enough room for another loading increment");
                    if (!GameInternals.ResourceAccess.IsLoading(loader)) loader.SwitchLoading();
                    startedLoading = true;
                }
                else if (!GameInternals.ResourceAccess.IsLoading(loader)) return Stop("loader stopped; checking again shortly");
                body.StandFacing(leg.TargetPoint);
                return Vector3.zero;
            }
            if (leg.Stage != Phase.Buy && (cell == null || !cell.gameObject.activeInHierarchy)) return Stop("cell unavailable");
            if (leg.Stage == Phase.Deliver && (leg.Floor == null ||
                (leg.Floor.TransformPoint(leg.FloorPoint) - leg.TargetPoint).sqrMagnitude > .04f))
            {
                if (!PlanStorage(true)) return Stop("ship storage moved or became unavailable");
                return Vector3.zero;
            }
            if (!body.StepIntoReach(leg, out Vector3 move, out wantMove)) return move;
            if (leg.Stage == Phase.Buy) return Buy(leg);
            // The guard above establishes the cell for both remaining phases.
            Grabbable item = cell!.GetComponent<Grabbable>();
            if (leg.Stage == Phase.Fetch)
            {
                if (Items.TakeBlocker(item, body.Hands.Item) != null || body.TakenByAnother(item.transform) ||
                    (Items.ItemTop(item) - leg.TargetPoint).sqrMagnitude > 0.5f) return Stop("cell moved or was taken");
                if (!body.Hands.PickUp(item)) return Stop("could not pick up the cell");
                storageAttempts = 0;
                rejectedStorage.Clear();
                if (restocking)
                {
                    if (!PlanStorage(true)) return Stop("no reachable clear ship storage space");
                    return Vector3.zero;
                }
                body.LoadRoomOf(slot.transform);
                Vector3 point = slot.GetComponent<BoxCollider>().bounds.center;
                if (!Plan(new Leg(this, point, loader.transform, Phase.Insert), true)) return Stop("cannot reach the loader");
            }
            else if (leg.Stage == Phase.Deliver)
            {
                if (body.Hands.Item != item || !body.IsAboardPlayerShip()) return Stop("cannot deliver the cell aboard");
                if (!ResourceStorage.Clear(leg.TargetPoint, ResourceStorage.Clearance(body.Hands.Extents), item.transform))
                {
                    rejectedStorage.Add(leg.TargetPoint);
                    if (!PlanStorage(true)) return Stop("ship storage became blocked");
                    return Vector3.zero;
                }
                body.Hands.PutDown(leg.TargetPoint, Vector3.zero);
                Room? destination = leg.Floor != null ? leg.Floor.GetComponentInParent<Room>() : null;
                if (destination != null)
                {
                    item.SetParent(destination.ContentParent);
                    item.SavePosition();
                }
                YourBuddyPlugin.Log.LogInfo($"[resources] Stored {ResourceDutySettings.Label(kind)} cell at {leg.TargetPoint}");
                if (NpcVessels.OwnerOfTransform(item.transform) != NavGraph.ShipOwner)
                {
                    ResourceDuty.Settings.Paused = true;
                    return Stop("delivered cell was not registered aboard; duties paused for inspection");
                }
                cell = null;
                int stock = ShipStock();
                if (stock >= stockGoal || PurchasesLeft <= 0) return Stop($"restock complete; {stock} usable cells aboard");
                deadline = Time.time + 240f;
                if (FindCell() || FindShop()) return Vector3.zero;
                return Stop($"restock stopped; {stock} usable cells aboard, no reachable supply or affordable purchase");
            }
            else
            {
                if (body.Hands.Item != item) return Stop("cell left Buddy's hands");
                if (!slot.isActiveAndEnabled) return Stop("loader slot is unavailable");
                if (leg.InsertUntil == 0)
                {
                    body.Hands.ReachTo(slot.GetComponent<BoxCollider>().bounds.center);
                    leg.InsertUntil = Time.time + 5f;
                }
                else if (Time.time > leg.InsertUntil) return Stop("cell did not enter the loader");
                body.StandFacing(leg.TargetPoint);
            }
            return Vector3.zero;
        }

        private Vector3 Buy(Leg leg)
        {
            Player? player = NpcPlayer.Pilot;
            Shop? shop = leg.Shop;
            if (player == null || shop == null || !shop.isActiveAndEnabled ||
                (shop.transform.position - leg.TargetPoint).sqrMagnitude > 0.5f || player.Controller.IsControlling)
                return Stop("shop or player is busy");
            Grabbable[]? stock = GameInternals.ShopAccess.Stock(shop);
            if (stock == null || leg.Product < 0 || leg.Product >= stock.Length) return Stop("shop stock changed");
            Grabbable product = stock[leg.Product];
            if (product == null || !product.TryGetComponent(out ResourceContainer prefab) || prefab.Type != TypeOf(kind) || (!restocking && !Fits(prefab)) ||
                !ResourceDuty.Settings.CanBuy(Rule, product.BuyPrice, player.CashSystem.Cash)) return Stop("purchase permission or funds changed");
            Transform? outlet = GameInternals.ShopAccess.Outlet(shop);
            if (outlet == null) return Stop("shop outlet unavailable");
            if (restocking && (ShipStock() >= stockGoal || PurchasesLeft <= 0)) return Stop("restock quantity reached");
            // A cell may have appeared while Buddy walked to the shop; recheck before spending.
            if (FindCell()) return Vector3.zero;
            int affordable = ResourceDuty.Settings.AffordableQuantity(Rule, product.BuyPrice, player.CashSystem.Cash,
                Math.Min(PurchasesLeft, Rule.BuyQuantity));
            if (affordable == 0) return Stop("no more cells affordable within your limits");
            if (restocking && !PlanStorage(false, product)) return Stop("no clear storage route; purchase cancelled before spending");
            beforePurchase.Clear();
            foreach (ResourceContainer existing in UnityEngine.Object.FindObjectsOfType<ResourceContainer>(true))
                beforePurchase.Add(existing.GetInstanceID());
            int cash = player.CashSystem.Cash;
            bool uncertain = false;
            try { GameInternals.ShopAccess.Buy(shop, leg.Product, player); }
            catch (Exception ex)
            {
                uncertain = true;
                YourBuddyPlugin.Log.LogWarning("[resources] Purchase failed: " + ex.Message);
            }
            long spent = Math.Max(0L, (long)cash - player.CashSystem.Cash);
            ResourceDuty.Settings.Budget = (int)Math.Max(0L, ResourceDuty.Settings.Budget - spent);
            ResourceContainer? bought = null;
            int count = 0;
            foreach (ResourceContainer candidate in UnityEngine.Object.FindObjectsOfType<ResourceContainer>())
            {
                if (beforePurchase.Contains(candidate.GetInstanceID()) || candidate.Type != TypeOf(kind) ||
                    (candidate.transform.position - outlet.position).sqrMagnitude > 4f) continue;
                bought = candidate;
                count++;
            }
            beforePurchase.Clear();
            if (uncertain || spent != product.BuyPrice || count != 1 || bought == null)
            {
                ResourceDuty.Settings.Paused = true;
                return Stop("purchase outcome unclear; duties paused. Check your cash and shop before resuming");
            }
            cell = bought;
            purchasesMade++;
            YourBuddyPlugin.Log.LogInfo($"[resources] Bought {ResourceDutySettings.Label(kind)} cell for {spent}; allowance {ResourceDuty.Settings.Budget}");
            if (!Plan(new Leg(this, Items.ItemTop(bought.GetComponent<Grabbable>()), bought.transform, Phase.Fetch), true))
            {
                ResourceDuty.Settings.Paused = true;
                return Stop("purchased cell is at the shop; duties paused because Buddy cannot reach it");
            }
            return Vector3.zero;
        }

        private Vector3 Stop(string why) { Cancel(why); return Vector3.zero; }
        private enum Phase { Fetch, Insert, Buy, Deliver }
        private sealed class Leg(ResourceErrand job, Vector3 point, Transform own, Phase stage) : ErrandLeg(point, own)
        {
            internal readonly Phase Stage = stage;
            internal Shop? Shop;
            internal int Product;
            internal float InsertUntil;
            internal Transform? Floor;
            internal Vector3 FloorPoint;
            public override string Name => Stage == Phase.Buy ? "resource shop" : Stage == Phase.Fetch ? "resource cell" : Stage == Phase.Deliver ? "ship storage" : "ship loader";
            public override float ReachBelow => 0.5f;
            public override bool Waits => InsertUntil > 0 || job.startedLoading;
            public override Vector3 Approach(out bool wantMove) => job.Approach(this, out wantMove);
            public override string Describe() => (job.restocking ? "restocking " : "refilling ") + ResourceDutySettings.Label(job.kind);
            public override void Defer(float seconds)
            {
                job.skipped.Skip(Own, Math.Max(60f, seconds));
                job.nextCheck = Time.time + Math.Max(5f, seconds);
            }
            public override bool Recover(string why)
            {
                if (Stage == Phase.Deliver && job.cell != null)
                {
                    job.rejectedStorage.Add(TargetPoint);
                    if (job.PlanStorage(true)) return true;
                }
                if (job.purchasesMade > 0 && job.cell != null)
                {
                    ResourceDuty.Settings.Paused = true;
                    why += "; purchased cell needs recovering, duties paused";
                }
                job.Cancel(why);
                return true;
            }
            public override void End() => job.End();
            public override bool Holds(Transform t) => base.Holds(t) ||
                (job.cell != null && t == job.cell.transform) || (job.loader != null && t == job.loader.transform);
        }
    }
}
