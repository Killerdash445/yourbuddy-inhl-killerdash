# Dialog - giving the buddy orders

`BuddyConversation.cs` (what the buddy says through NPC.Core's talk window), `BuddyDialogCommands.cs`
(parsing), `BuddyRooms.cs` (room names) and `BuddyCommands.cs` (the orders). Config: `Dialog` (General,
default on). There is no hotkey; the game's Interact binding is the only way in.

---

## 1. Opening it

Look at the buddy and press **Interact**. The window, which NPC answers, the sight and range checks
and the input handover are NPC.Core's ([interaction.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/interaction.md#1-opening-it)).
A buddy can be talked to (`BuddyConversation.CanTalk`) while `Dialog` is on and it is neither asleep
in its capsule, hidden, nor ignoring you in an anomaly ([anomalies.md](anomalies.md#2-the-anomalies));
NPC.Core adds that it lives and is loaded.

Opening the window makes that buddy the **focus**, so console commands without a target go to it too
([reference.md §2](reference.md#2-debug-commands)). While it is open the buddy stands still and faces
the player (`InDialog`). The title is the buddy's name - once in a while the wrong one ([anomalies.md](anomalies.md#wrongname)) -
and the first line "Standing by.". An anomaly may add lines to the log while the window is shut.

---

## 2. The panel

NPC.Core draws it ([interaction.md §2](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/interaction.md#2-the-panel)). The commands page lists
`BuddyDialogCommands.NamesFor(buddy)`: inside, Follow, Wander, Stay, Hide, Outside, Unsuit (only while suited), Tidy,
Sell, Play, Snack, Goto, Decide, Password; outside, Follow, Wander, Stay, Inside, Goto, Decide; floating,
Follow, Stay, Inside, Decide ([eva.md §7](eva.md#7-floating)).

---

## 3. The orders

`BuddyDialogCommands.Run` keyword-matches like the game's `AssistanceBot` ("follow" and "follow me"
both work). A bare number is a door code. Everything calls `BuddyCommands`, the same code the console
uses, for the buddy being talked to.

**Everyone.** "everyone", "everybody", "all of you" or "both of you" anywhere in the text gives the
order to every living, awake buddy, one reply line each ("everyone follow me"). The group word is
removed before matching. A password is told once: codes are shared
([door-knowledge-is-shared](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#door-knowledge-is-shared)).

Matching order matters - it is a substring test ("trash box" contains "trash"). The one exception
is a goto that names a room, which is tried right after "decide" ("go to the workshop" contains
"work"):

| Word | Console | Effect |
|---|---|---|
| decide / yourself / your call | `buddy_manage auto on` | `RevokeOrder` ([behaviour.md](behaviour.md)); matched **first** |
| unsuit / take off the suit / remove the suit | `buddy_order suit off` | take the worn suit off - never outside or in an airlock ([eva.md §5](eva.md#5-suiting-up-for-deadly-air)); before the airlock orders |
| inside / come in / back in | `buddy_order inside` | from outside, walk into an airlock's chamber and wait for your cycle ([eva.md §4](eva.md#4-through-the-airlock)); before the outside order |
| outside / eva / space walk | `buddy_order outside` | suit up if a spare is free, then wait in an airlock (a station's, or the ship's own while undocked) for your cycle ([eva.md §4](eva.md#4-through-the-airlock)); before the room goto |
| hide / closet / locker / conceal | `buddy_order hide` | hide and stay until the next order ([fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)) |
| follow / come | `buddy_order follow` | `ApplyOrder(Follow)` |
| wander / job | `buddy_order wander` | `ApplyOrder(Wander)` |
| stay / wait / stop | `buddy_order stay` | `ApplyOrder(Stay)` |
| sell / trash box / money / cash | `buddy_order sell` | sell nearby trash boxes ([items.md §4](items.md#4-selling-trash-boxes)); before tidy |
| tidy / clean / trash / rubbish / garbage / litter / bin | `buddy_order tidy` | a tidying round ([items.md §3](items.md#3-tidying)) |
| play / toy | `buddy_order play` | a play session ([items.md §5](items.md#5-idle-play)) |
| snack / eat / food / hungry | `buddy_order snack` | eat or drink something nearby ([snacks.md](snacks.md)) |
| goto *room* | `buddy_dev goto <i>` (a node, not a room) | `FindPath` + `ApplyRouteOrder` ([below](#goto-by-room)) |
| password *nnnn* | `buddy_order password <code>` | adds the code to the codes every NPC knows ([NPC.Core's password doors](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#password-doors)) |

**Follow, Wander, Stay, Goto and "decide" are orders.** They are recorded as the order in force
([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)). Given during a flee, an order waits
until the flee ends, and the reply says so ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)).

**Hide, Sell, Tidy, Play and Snack are tasks.** They start now, skipping schedule and config switch, and the
buddy returns to its order afterwards. They refuse while asleep, scared, busy or on a goto. Hide is
the exception: it works while Alert, and it interrupts an errand the buddy chose itself
([a-command-outranks-an-errand](invariants.md#a-command-outranks-an-errand)). An ordered hide ends
only on the next order ([an-ordered-hide-ends-only-on-an-order](invariants.md#an-ordered-hide-ends-only-on-an-order)).

`Stay` holds position, but still steps out of a doorway it blocks
([step-off-applies-in-every-mode](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#step-off-applies-in-every-mode)).

### Goto by room

The dialog sends the buddy to a **room**, not a node number; `buddy_dev goto` keeps node indices for
debugging. Rooms are those of the station the ship is docked to (`SpaceStation.rooms`), under the
names the debug HUD shows (`YardLibrary`, `OxygenKitchen`). Case, spaces and the station's common
prefix are ignored, and so is a partial name that is unique: "goto library", "go to the Yard Library".
A partial name that fits several rooms is answered with the candidates, and "goto" alone (the
**Goto** button too) lists the rooms.

A room has no volume ([game-model.md §2](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#2-there-are-no-room-volumes)) and a station's
floors are not under its rooms, so `BuddyRooms` gives each node to the room whose furniture (every
transform under the `Room`) is nearest to it. The room's nodes are tried nearest its middle first, up
to four, so one dead-end node does not strand it. A room with no node is not listed. The
`[nav] Rooms at <station>` line names the first node chosen for each room, by the index `buddy_dev goto`
takes, so a wrong pick can be tried from the console.

Orders and mode are not saved; a loaded buddy starts in Follow with no order. Door codes are saved.
The password reply says whether any door in the scene uses that code, so typos show at once.
