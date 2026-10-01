# Anomalies - when the buddy is not quite itself

Now and then the buddy does something funny, strange or frightening. The aim is doubt: is this still
the friend you woke up with? `AnomalyDirector.cs` decides when and how far; `BuddyBehaviour.Anomaly.cs`
acts it out. Config section `Anomalies`: `Anomalies` (on), `AnomalyDifficulty` (`Game`),
`AnomalyFrequency` (1).

---

## 1. How often and how far

The game has no single "danger level". Its own random events use two numbers, and so do these:

| Input | Read from | Values |
|---|---|---|
| events frequency | `SceneLoader.Instance.GameData.Settings.eventsFrequency` | Harmless 0, Normal 1, Expert 2, Custom its slider |
| event tier | `GameManager.Instance.EventSystem.Tier` | 0-3; rises as the story is reported, back to 0 for a quiet stretch |
| story progress | `GameManager.Instance.SequenceHandler.CompletedTasksCount` | tasks completed in this save; only rises |

`AnomalyDifficulty` = `Harmless` / `Normal` / `Expert` replaces the frequency with 0 / 1 / 2.

**What is allowed** (`AnomalyDirector.Allows`):

| Severity | Allowed when |
|---|---|
| funny | always |
| strange | frequency above 0 |
| scary | frequency above 0, and `NormalScaryTasks` (1) story task done; on Expert `ExpertAllTasks` (1) |
| extreme | frequency above 0, and `NormalExtremeTasks` (3) story tasks done; on Expert `ExpertAllTasks` (1) |

So Harmless only ever gets funny moments. Normal escalates with the story: strange from the start,
scary after the first task, extreme after the third. Expert starts strange and allows scary and
extreme together after the first task. Progress, not the tier, gates them: the tier drops back to 0 for a quiet
stretch, which would make the buddy tamer halfway through the story. The tier still raises the chance
and leans the draw scarier. `Chatter` is too silly for anything but Harmless (`HarmlessOnly`).

**When.** Every `CheckSeconds` (60 s), after `FirstCheckSeconds` (300 s) and outside the cooldown, one
roll with chance `(0.05 + 0.07 f + 0.03 t max(0.5, f)) x AnomalyFrequency` (f frequency, t tier):
0.05 on Harmless, 0.12-0.21 on Normal, 0.19-0.37 on Expert. The cooldown after one is
`CooldownSeconds` (600 s) / (1 + 0.25 t) / (1.5 on Expert) / `AnomalyFrequency`. On Normal that is
roughly one every 15-20 minutes.

**Which.** A random buddy that is free (`AnomalyReady`: awake, inside, calm, not hiding, talked to, on
an errand or a goto). Allowed kinds are drawn by their own weight times a severity weight that leans
scarier with the tier. The last three kinds are not drawn again. A kind that does not fit here (its
start conditions below) is dropped and the draw repeats. If none fits, the next roll comes in
`RetrySeconds` (20 s).

Never while the talk window is open, you are outside, the Breathless is within `MonsterClearance`
(25 m) of you, or another buddy is acting one out.

---

## 2. The anomalies

| Kind | Severity | Starts when | What happens | Ends |
|---|---|---|---|---|
| `Spin` | funny | you watch it, within 10 m | two turns on the spot | 1.8 s |
| `PeekABoo` | funny | out of your sight, 4 m+ | hides in a closet, jumps out with "Boo!" | see below |
| `Chatter` | funny, Harmless only | following you, within 8 m | says something silly ([§3](#3-what-it-says)) | at once |
| `Whisper` | strange | within 8 m, same vessel | says something unsettling | at once |
| `FakeCommand` | strange | the window is not open on it | its talk log gains an order you never typed | at once |
| `WrongName` | strange | - | the talk window opens under the wrong name, once | when next opened, or 15 min |
| `Vanish` | strange | out of your sight, 6 m+ | gone; comes back elsewhere | see below |
| `Noises` | strange | behind you, 1.2-7 m, unseen | clicks, a wet squelch; scary and worse add the monster's sounds, cut short | 2-3 sounds, or you turn round |
| `WindowStare` | strange | a window within 20 m on its vessel | walks to it and stares out | 50-140 s |
| `WallStare` | strange | a wall within 4 m | walks up to it and faces it | 50-140 s |
| `Bloody` | scary | out of your sight, 3 m+, no suit | its suit is spattered with blood; it acts normal, errands and orders included | see below |
| `ClosetAmbush` | scary | as PeekABoo | jumps out with a shriek | see below |
| `ShutDoors` | scary | you and it aboard, 2+ doors open | walks a round of the open doors, nearest next, and shuts each behind itself | the round, at most 6 doors |
| `Statue` | scary | 3-30 m, Follow or Wander | follows you, but only while you are not looking; freezes, staring, when you do | 50-90 s, once unseen |
| `Stalker` | extreme | out of your sight, no suit | `Bloody` and `Statue` together, with sounds at your back; ignores you | 50-90 s, once unseen |
| `BehindYou` | extreme | out of your sight, 6 m+ | vanishes, then stands right behind you and speaks | when you turn round, or 15 s |

An order, or a task you give it, ends most of them. It does not end:

- `Vanish`, `Stalker` and `BehindYou`: it is not listening. These, and `Statue`, also refuse the talk
  window (`IgnoresYou`).
- `Spin`: the order waits the 1.8 s for it to finish.
- `Bloody`: only a look (`AnomalyIsLooks`). It carries out the order, bloody.

Fear ends any of them but a vanish and the blood ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)).
So does deadly air ([survival-outranks-an-order](invariants.md#survival-outranks-an-order)). Only a
running anomaly that is not just a look keeps the decider standing down.

### Vanish

The renderers it had on are switched off, its controller stops colliding, and the agent is `Asleep`:
no AI, no catch, no air damage. `BuddyAgentSettings.ShowOnLifecare` turns false, so the scanner loses it
too. After 40-110 s it comes back at a ground node on your vessel, 6-16 m from you, that you cannot see.
If there is none, it waits, then comes back where it vanished once you look away. Its sidecar position
is where it vanished; a load brings it back there.

### Bloody

`BuddyGore` copies the texture the body wears (through a render target, so it need not be readable) and
paints 16 dark red blots on its painted texels. The same buddy gets the same stains each time. The blood
lasts `BloodySeconds` (150 s), and every second you look at it uses up `BloodySeenRate` (6) seconds of
that: the more you stare, the sooner it is gone, as if it wanted to hide it from you. It never goes in
front of you: only once you have not seen the buddy for `BloodyUnseenSeconds` (5 s). A suit going on
ends it; the suit's skin wins. Blood that something else wiped off the materials is painted back.

### PeekABoo and ClosetAmbush

The hide is [fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)'s, with `hidePrank` set. Inside, it
jumps out once you come within `PrankTriggerDist` (1.5 m) of the spot on its deck, or open the door. The
doors open and it steps out at once. After `PrankMaxSeconds` (240 s) it comes out quietly. The Breathless
turns it into a real hide, and it stays in.

### FakeCommand

Through NPC.Core's `NpcInteraction.AddLine`, two lines go into its log: `$ let him in` drawn as yours,
and its reply. You find them the next time you open the window. The pool darkens with the severity
allowed ([§3](#3-what-it-says)).

### WrongName

The window's `Title` reads "Buddy 2", "B-UDDY 02" or similar, and it says nothing about it. The next
open is normal again.

### WindowStare and WallStare

A window is a renderer named `Glass*`: every ship and station window block has one. It stands on the
ground node nearest the pane, 0.8-4 m from it, and faces the pane's centre. A wall stare casts eight
rays at chest height and walks straight to the nearest wall that is not a body or an item. The walk
gives up after a time limit.

Standing still, the agent turns an idle body after the brain's `OverrideMovement` (`TryIdleFacing`).
So an anomaly sets where it looks there (`AnomalyFaces`), not in the override, or the turn to you in
Follow undoes it.

### ShutDoors

The open room doors aboard, airlocks, locked and password doors left out, at most `MaxDoors` (6),
nearest to the buddy next. For each door it plans to a ground node `DoorPassDist` (1.6 m) past the
doorway, on the side away from it, and hands the door to the agent with NPC.Core's `CloseBehind`. The
agent shuts it once the buddy has walked through and is clear of the doorway, and waits while you stand
in it ([close-only-what-you-walked-through](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#close-only-what-you-walked-through)).
Past the door it stands facing it until it shuts, at most `DoorWaitSeconds` (4 s), then goes on. The
doorway's axis is whichever of the gate's own two walks clear both ways from its middle, 1.6 m out,
else 1.1 m. A door it cannot plan through is skipped with a level-1 line naming it and why; a leg gives
up after `DoorLegSeconds` (25 s). Once through the list it goes round once more for the doors still
open. `CloseBehind` arms each door afresh from where the buddy stands, so a close owed from an earlier
walk cannot leave it waiting for a crossing that already happened the other way.

---

## 3. What it says

`AnomalyLines.cs` holds the pools: spoken lines by severity, fake orders and their replies, wrong names.
A spoken line goes through NPC.Core's `NpcInteraction.Speak`: a speech panel in the talk window's
look, low in the middle of the screen, wrapped, for 3-8 s by length. The line also goes into the talk
log, and the station robot's talk blips play at the buddy as its voice: one per three letters, 4-16 of
them, `BlipGapMin`..`BlipGapMax` (0.07-0.12 s) apart, as its own typewriter plays them. Lines lean toward the worst
severity allowed.

---

## 4. Being seen

`PlayerView.Sees`: within `ViewHalfAngle` (55°) of the camera's forward, within `ViewRange` (40 m), and a
clear `NavProbe.CanSee` from the camera. Shut doors block it
([sight-stops-at-a-shut-door](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#sight-stops-at-a-shut-door)).
The buddy counts as seen when its chest or its head is. Behind you means more than `BehindAngle` (115°)
off the view. The running anomaly samples this every 0.1 s.

---

## 5. Sounds

FMOD events borrowed from the game, read off the first instance of their owner in the scene
(`GameInternals.ScareSoundAccess`) and kept until the world resets:

| `ScareSound` | Events |
|---|---|
| `Voice` | `AssistanceBot.talkSound` |
| `Click` | the robot's talk blips, `Gate.closeFailSound` |
| `Wet` | `Cleanable.cleanSound` |
| `Creature` | `Breathless.movingSound`, `BreathlessActivity.sound`, `RandomSound.sound`, `BackgroundSound.scarySound` |
| `Shriek` | `Breathless.screechSound`, `UnsealScream.screamSound` |

A category found empty is looked for again after 60 s, and is silent until then. `Creature` and
`Shriek` can run for many seconds, so they are cut short with a fade after `SoundCapSeconds` (2.5 s).

---

## 6. The player's stress

`AnomalyDirector.Startle` adds stress through one `StressSource` of the mod's own, once per anomaly:
ambush 35, behind you 30, stalker 25 (close), shriek 25, statue 15 (close), creature sounds 8. It never sets threat, which is what can kill the player.

---

## 7. Testing

| Command | Does |
|---|---|
| `buddy_anomaly [@who]` | the director's state, the sounds found, the buddy's current one |
| `buddy_anomaly list` | every kind, and whether this difficulty and tier allow it |
| `buddy_anomaly <kind> [@who]` | that one now, whatever the chance, cooldown and severity; still says why when it does not fit |
| `buddy_anomaly end [@who]` | ends the running one |
| `buddy_anomaly roll` | a draw now, as the director would, with a certain hit |

Log tag `[anomaly]`: starts, ends and lines at level 1; draws that did not fit, the chance and the sounds
found at level 2.

```
[anomaly] Buddy: vanishing (strange, 9.4m from you, out of sight)
[anomaly] Buddy is still gone - waiting for a spot out of your sight
[anomaly] Buddy is back, 11.2m from you
[anomaly] Buddy: vanishing over - back
```

---

## 8. Known limitations

- **The blood lands anywhere on the atlas**, face included. There is no bloody smile.
- **A statue does not hurry.** The agent caps its speed factor at 1, so unseen it walks at its usual pace.
