# Resource duties

The normal orders stay on one page. Choose **Resources** for six buttons:

- **oxygen**, **fuel**, **energy**: toggle each duty.
- **Buying**: allow or forbid purchases for enabled duties.
- **Limit**: cycle the remaining shared spending allowance through 0, 100, 250, 500 and 1000.
- **Orders**: return to the flat orders page.

Buttons update in place with NPC.Core's optional command-page interface. Replies are one line;
Back shows the conversation log. There is no number editor: typed orders and door codes keep
working. Duties and buying default off; the spending allowance defaults to zero.

## Policy and saves

An enabled duty refills at or below 30% towards 80%. Oxygen means stored ship oxygen,
not room atmosphere. The game's fixed transfer increments can overshoot the target.
With Buying on and fewer than one usable matching cell aboard, Buddy recovers a loose
station cell or buys one. Existing supplies are preferred; closed containers are not opened.
A run brings back one cell. Every purchase must fit both the remaining allowance and the
player's cash. Spending reduces the allowance; toggling Buying does not replenish it.

Settings belong to the save and are stored in the `.buddy` sidecar. A new game or a save
without settings starts with defaults. A world reset cancels runtime work but preserves
settings already read from a sidecar. In-progress routes are not restored.

## Errands and interruptions

`ResourceErrand` participates in the ordinary decider alongside other errands, using its
planning, skip list and deferral helpers. It requires autonomy and respects existing orders,
fear and survival priorities. Resource buttons do not change the Autonomy configuration or
release a standing order. **Decide** on the normal orders page uses the existing autonomy order.

Only one buddy owns the shared loader duty. It never takes over the player's inserted cell;
interruption ejects only its own cell. Purchases and deliveries are rechecked before acting.
An uncertain purchase pauses duties; interrupted purchase verification turns Buying off.
Inspect the shop and cash before enabling work again.

## Scans and storage

Resource and shop searches use `SceneScan.ThisFrame`, sharing each frame's snapshot.
After buying, verification waits for the next frame's snapshot instead of forcing a new scan.
Storage uses active ship nodes and the carried item's footprint. A shared budget permits at
most four candidate probes per frame; a pending search holds its errand while continuing
from its cursor. Exhausted searches are cached briefly. Placement is checked again on arrival.

Cells are placed on supported clear floor, outside airlocks and restricted item zones.
Storage does not put cells inside cupboards or create paths through furniture. If no safe
local drop exists on interruption, Buddy holds the item and pauses duties. Move it to clear
floor before toggling a resource to retry.

See [the ownership invariant](invariants.md#resource-duties-own-only-their-cell).
Resource progress and blocked jobs are logged with the `[resources]` prefix; ordinary decider
failures use the existing `[mind]` retry trace.
