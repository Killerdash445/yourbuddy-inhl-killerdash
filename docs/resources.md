# Resource duties

Talk to a buddy, choose **Commands > Resources** from the main menu, then a resource. Resources is not repeated in Movement, Jobs or Suit & travel. Choose **Refilling** for
percentages or **Restocking** for purchase controls. Reopen Commands after
choosing a button to see its controls. Click Start or Target, type an exact whole percentage in the conversation box and press Enter.
A trailing percent sign is optional. Invalid values leave the editor open; type `cancel` to discard the edit. **Orders** returns to ordinary orders. Buttons use short labels such as **Start: 30%** and
**Allow buying**; the full typed commands below remain available.

| Conversation input | Setting |
|---|---|
| `resources oxygen on` / `off` | Enable or disable oxygen reserve refilling |
| `resources oxygen start 30` | Start at or below 30% of tank capacity |
| `resources oxygen target 80` | Continue towards 80% |
| `resources oxygen buy on` / `off` | Allow or forbid purchases |
| `resources oxygen stock 2` | Restock when fewer than two usable cells are aboard |
| `resources oxygen quantity 3` | Buy up to three cells per restock run |
| `resources budget 500` | Set the shared **remaining** spending allowance to 500 |
| `resources reserve 100` | Keep at least 100 cash in the player's wallet |
| `resources pause` / `resume` | Pause or resume all resource duties |
| `resources` | Show settings and the latest duty status |

Replace `oxygen` with `fuel` or `energy`. All duties and purchase permissions start off;
the default start/target values are 30%/80%, with zero purchase allowance. Start must be lower
than target, and both must be within 0-100. A zero start triggers only on an empty tank.
Click **Stock < 1**, **Buy up to 1**, **Set budget** or **Keep cash** to enter exact counts or amounts.
Stock threshold and purchase quantity accept 1-100; both default to one.
Oxygen means the ship's stored oxygen, not the room atmosphere.

Settings and the remaining allowance belong to the save and are stored in its `.buddy` sidecar
when game save support is enabled. Resume does not replenish the allowance. Loading an older
save restores its own settings and allowance along with the game's cash. In-progress routes
are not restored; Buddy checks the start threshold again after loading.

## Work and interruptions

The scheduler can start aboard the player's ship or from an interior while the ship is docked,
while autonomy is enabled and Buddy is free. **Start duties** enables autonomy, resumes duties,
releases this buddy's standing order, and requests a fresh check. It can end an ordinary errand
but preserves survival and fear priorities. Other buddies retain their orders.
**State** includes the current task, order blocker, or last resource result.
Resource duties run ahead of ordinary idle choices;
the existing [order and survival priorities](behaviour.md#3-the-decider) still apply.
Opening the conversation holds walking without discarding the route. Permission and threshold
changes apply to the active run; **Pause duties** stops it.

A refill run fetches a loose usable cell, carries it to the ship's loader and uses the normal insertion
trigger and loading switch. Partly spent cells are preferred. Buddy waits at the loader until
the target is reached, the cell empties, or the game stops loading. Used cells are ejected, not
discarded. Another cell can be fetched after the retry interval if the target still needs work.

The game transfers resources in fixed cell increments. A target can be exceeded by one increment;
100% may be unreachable until enough capacity is available for another increment.

Only supplies and shops in accessible rooms are considered. Candidate rooms are loaded through the existing room system. Closed containers are not opened by
this routine. Cells staged in item detectors, held by the player or claimed by another buddy
are left alone. Unreachable or temporarily skipped supplies do not block a reachable shop purchase.
Buddy does not take over a cell already in the loader.

Restocking is independent of the tank percentage. It counts matching cells with contents remaining
aboard the player's ship, including accessible room contents and an inserted cell. Empty cells and
cells on a station do not count as ship stock. Counts are cells, not their combined contents.

When ship stock is below the configured count, a restock run recovers reachable loose station
supplies before buying. Purchased cells are carried aboard one at a time to clear floor storage,
separate from the loader's insertion point. Storage candidates follow active ship navigation nodes
and room positions for each upgrade stage; routes still use the existing navigation graph.
Buddy checks the carried cell's footprint for level floor support and clear space, excluding
airlock chambers and placement-restriction volumes. Placement is checked again on arrival;
blocked destinations can be replaced, with at most three delivery plans per cell.
Preflight checks use the available cell or shop product's measured size where available,
then recheck the carried cell before release. Solid blockers use the item's collision layers;
placement restrictions and item-detector zones remain excluded. Failed checks distinguish occupied
storage from an unreachable approach, with detailed scan counts and blockers in the log.
Loose floor storage does not insert cells into cupboards or create new paths through furniture.
The run buys no more than its configured quantity and stops earlier if supplies replenish the ship
or cash, allowance or purchase permission no longer permit another cell. If stock remains below the
threshold after a run, a later check can start another run. Lower quantities therefore make smaller trips.

A shop run walks to the shop before buying and rechecks stock, permission, price and wallet limits.
It invokes the game's purchase method and checks the charge and newly dispensed cell. An unclear
purchase result, or an unreachable newly purchased cell, pauses the duties for inspection.
Shop use waits while the player controls an interface. The shop's selected item is restored.

Failed runs wait at least 30 seconds before trying again, including from the docked station.
Failed targets are skipped for at least 60 seconds so another cell or shop can be tried.
A route failure while recovering a purchased cell pauses duties for inspection.
Each delivery or refill has a four-minute deadline.
On interruption Buddy looks for a checked floor position nearby. If none is clear, duties pause
and the cell stays in his hands. Move him to open floor and choose **Start duties** to retry.
The conversation reports the current reason; `[resources]` logs changes with throttling.

## Implementation

`Resources/ResourceDutySettings.cs` owns validation and spending policy; `ResourceDutyMenu.cs`
uses the existing conversation UI. `ResourceDuty.cs` coordinates one active job across buddies.
`ResourceErrand.cs` executes reach/carry legs through `IErrandBody`; `ResourceStorage.cs` owns
floor support and clearance checks. Game field access stays in
`GameInternals.ResourceAccess` and `ShopAccess`. Persistence uses `BuddySaveFile.ResourceDuties`.
See [resource ownership and spending](invariants.md#resource-duties-own-only-their-cell).
