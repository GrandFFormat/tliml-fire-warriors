TLIML Fire Warriors — BepInEx mod (v7)
=======================================

What it does
------------
Craft and use the "Fire Warrior Campfire" item (see the crafting menu -
usually free to craft, see EnableCraftableCampfireItem/RequireCraftingCost
below) wherever you want to gather. A fire appears where you used it, and
5 unarmed warriors show up around it (twice as fast on their feet as a
normal NPC by default). They spread out and, within 100m of the fire,
pick up every loose item they can reach, empty out every chest, crate and
other storage container they find, capture every horse they find (except
your own personal horse, always left alone), AND capture every wagon they
find (except one you're driving, or one that already belongs to your own
camp) - everything, every horse and every wagon gets brought straight to
your home camp (the camp you started the game with), no need to be near
it or even know where it is. Horses and wagons are grabbed first, before
nearby items/containers, so they don't sit around waiting their turn.
Once nothing's left to grab, empty or capture within range, each warrior
walks back to the fire and sinks into it, vanishing - and once every
warrior is back, the mod can automatically burn down the nearest enemy
camp near that fire, using the game's own burn-settlement effect (this is
off by default now, see BurnCampWhenDone below).

If a camp within range of the fire is set ablaze (the game's own "burn
settlement" ability) before the warriors finish, they vanish instantly
instead of finishing the job.

NOTE: earlier versions also had K (summon) and L (dismiss-all) hotkeys as
an alternative to the craftable item. Those have been removed per request
- using the item is now the only way to summon, and there's currently no
in-game way to force-dismiss everything early.

Everything is adjustable in:
  BepInEx/config/local.tliml.firewarriors.cfg
(created/updated the first time you run the game with the mod installed)

  WarriorCount            - how many warriors spawn (default 5)
  WarriorMoveSpeedMultiplier - multiplies how fast warriors walk/run
                            compared to a normal NPC (default 2.0 = twice
                            as fast). 1 = normal speed.
  LootRadius              - how far around the fire they'll look for
                            items/containers, and how far they're allowed
                            to roam from it, in meters (default 100)
  SpawnRingRadius         - how far around the fire they initially
                            appear (default 3)
  SpawnDistanceFromPlayer - how far in front of you the fire itself is
                            placed (default 4)
  DespawnWhenCampBurns    - vanish instantly if a nearby camp starts
                            burning (default true)
  CampBurnCheckRadius     - how far a burning camp can be and still
                            trigger that (default 100)
  OnlyItemsMarkedAiPickup - if true, only take items the game itself
                            flags as AI-pickable; if false (default),
                            they take everything that can be picked up.
  EnableCraftableCampfireItem - if true (default), adds the craftable
                            "Fire Warrior Campfire" item to the crafting
                            menu (see "How it's built" for how it's found/
                            added). This is currently the ONLY way to
                            summon, so leave this true unless you want the
                            mod fully inert.
  CraftableItemDisplayName - the name shown for the craftable item in the
                            crafting menu and inventory (default
                            "Fire Warrior Campfire").
  RequireCraftingCost     - if true, crafting it costs the same resources
                            as whatever real campfire-type item it was
                            cloned from. If false (default), it's free.
  AllowCampWorkerOrders   - EXPERIMENTAL, off by default. If true, also
                            makes the item assignable as a camp work
                            order. This was linked to a game crash during
                            testing - see "Known limitations" below.
  UseCustomIngredients    - if true (default), the recipe costs just the
                            two ingredients below instead of the real
                            campfire's full requirement list. If false,
                            falls back to RequireCraftingCost's behavior.
  CustomIngredient1Name / CustomIngredient1Amount - first ingredient and
                            amount (default: Wood x1). Name must match an
                            existing item's in-game name (case-insensitive).
  CustomIngredient2Name / CustomIngredient2Amount - second ingredient and
                            amount (default: Flint x1).
  CaptureHorses           - if true (default), warriors also look for
                            horses within LootRadius of the fire (your own
                            personal horse is always left alone) and bring
                            them back to the home camp instead of ignoring
                            them.
  TameCapturedHorses      - if true (default), a captured horse is flagged
                            as tamed once it arrives at the home camp.
  PrioritizeHorses        - if true (default), a warrior that spots a
                            capturable horse goes for it immediately
                            instead of finishing nearby items/containers
                            first. If false, horses are picked purely by
                            distance like everything else.
  BurnCampWhenDone        - if true (default), once every warrior from a
                            fire has finished and returned, the mod
                            automatically burns down the nearest enemy
                            camp within BurnCampSearchRadius of that fire
                            (using the game's own burn-settlement effect -
                            the same one you'd trigger manually). Your own
                            home camp is never targeted.
  BurnCampSearchRadius    - how far (meters) from the fire to look for a
                            burnable enemy camp once the warriors are done
                            (default 60).
  BurnCampDuration        - how long (seconds) the burn-down effect takes
                            to play out (default 6).
  WarriorMaxLifetime      - hard cap in seconds on how long a single
                            warrior can exist before it's forced to
                            vanish no matter what it's doing (default 120
                            = 2 minutes). Safety net against a warrior
                            getting stuck - see v7.11.0 below. Set to 0 to
                            disable.
  CaptureWagons           - if true (default), warriors also capture
                            wagons/carts within LootRadius and bring them
                            back to the home camp, the same way they do
                            horses. A wagon you're driving, and any wagon
                            already belonging to one of your own camps,
                            are always left alone (matched by faction, so an
                            allied camp's carts are safe too). See v7.25.0.
  WagonCaptureRadius      - how close (straight line, meters) a warrior
                            needs to get to a wagon to take it (default
                            12 - bigger than HorseCaptureRadius because a
                            wagon is a big object often parked on ground
                            the game's walkable-area data doesn't cover).
  PrioritizeWagons        - if true (default), a warrior that spots a
                            wagon goes for it immediately instead of
                            finishing nearby items/containers first, same
                            as PrioritizeHorses.
  UseCampPrefabTemplate   - if no living friendly NPC can be found to copy,
                            build warriors from the character prefab your
                            camps spawn their own people from instead
                            (default true). This is what makes summoning
                            work anywhere on the map with nobody around -
                            see v7.24.0 below.
  PreferCampPrefabTemplate - use that prefab FIRST instead of only as a
                            fallback (default false).
  AllowAnyFactionPrefabTemplate - if no camp of your own faction is loaded,
                            take the prefab from any camp at all (default
                            true). Affects only how warriors look; they are
                            always assigned to your own faction.

v7.4.0: by default the recipe now just costs 1 Wood + 1 Flint (via
UseCustomIngredients above) instead of the real campfire's full, much
bigger requirement list - RequireCraftingCost/the donor's real cost is
only used as a fallback if that's turned off or the ingredient names
don't match anything.

CORRECTION about the earlier crash: it turned out to NOT be caused by the
ItemsDB.Items registration (camp work orders) after all - it was actually
triggered by USING the item (summoning the fire/warriors) while standing
inside a camp specifically, not by ordering camp members to craft it.
Likely cause: summoning 10 warriors spawns 10 NavMeshAgents essentially
in the same instant, and a camp is a much busier, more obstacle-dense
NavMesh area (buildings, fences, other NPCs) than open terrain - creating
many navigation agents at once in a busy area is a known way to crash
Unity's own NavMesh system outright (this Unity version is 2018.2, where
that's a known rough edge), which lines up with it working fine in the
open but crashing specifically in camp. v7.5.0 spreads the 10 warriors'
spawning out over about half a second (one every ~50ms) instead of all
in one instant, which should avoid that. AllowCampWorkerOrders is still
off by default out of caution, but since it wasn't actually the cause of
the crash, it may be safe to try now if you want that feature - your call.

v7.6.0: found and fixed a real bug where, at some point mid-session
(likely during a scene transition the game does internally), this
plugin's own GameObject was actually destroyed even though it should have
been protected from that - every summon after that point would silently
fail (caught, so no crash, but nothing would happen at all and
FireWarriors_debug.txt would show a "SummonFire() THREW:
NullReferenceException" from deep inside Unity's StartCoroutine). Fixed
by hosting the warrior-spawning coroutine on a separate, dedicated object
that re-creates itself if it's ever found destroyed, instead of relying
on the plugin's own GameObject staying alive for the whole session. If
summoning ever silently does nothing again, closing and reopening the
game is the workaround until you can send over the debug log.

v7.7.0: "no friendly NPC found" was reported even while standing in a
camp with people around, which shouldn't happen. Added a diagnostic dump
to FireWarriors_debug.txt for that specific case - it now lists every
entity within 60m and exactly which condition it does/doesn't meet
(faction name, has the right component, alive or not), so the real
mismatch shows up directly in the log instead of needing another guess.

v7.8.0: warriors now also capture horses. While seeking a target, each
warrior also scans for nearby horses (identified by their cosmetic
HorseAppearance component, or by "horse" appearing in their name as a
fallback) within LootRadius of the fire, always skipping your own
personal horse (matched against the game's own PersonalHorse singleton,
so it's never touched no matter where it is). Whichever target (item,
container or horse) is closest gets picked. When a warrior reaches a
horse, instead of destroying it like an item, it's teleported (NavMesh-
aware, so it lands on walkable ground) to a random spot near your home
camp, and - unless TameCapturedHorses is turned off - flagged as tamed.
New config: CaptureHorses (on by default) and TameCapturedHorses (on by
default). This is a proximity-based heuristic (there's no explicit
"ownership" flag on regular horses to check besides the personal-horse
singleton), so if a horse ever seems to get skipped or wrongly grabbed,
FireWarriors_debug.txt logs every capture with the horse's name and
where it was teleported to - send that over and it can be tightened up.

v7.9.0: three changes based on feedback that horses were taking too long
(around 2 minutes) to disappear and that things felt slow overall:
  - WarriorCount default dropped from 10 to 5 (still fully adjustable).
  - Warriors now move at 2x their normal speed by default
    (WarriorMoveSpeedMultiplier) - most of that 2-minute wait was almost
    certainly just walk time across LootRadius (100m default), not the
    actual capture logic.
  - Horses are now grabbed BEFORE nearby items/containers instead of
    whichever's closest winning (PrioritizeHorses, on by default) - a
    warrior that spots one heads straight for it instead of finishing
    other loot first. The in-place "capture" pause once a warrior reaches
    a horse was also shortened (1.0s -> 0.4s).
  - New: once every warrior from a fire has finished and walked back in,
    the mod now automatically burns down the nearest enemy camp near
    that fire (BurnCampWhenDone, on by default, searches
    BurnCampSearchRadius=60m) using the exact same burn-settlement effect
    the game itself uses for the manual "burn camp" action - not a
    custom/fake fire effect. Note: this briefly takes over player control
    the same way it would if you burned it yourself (a few seconds of
    "interacting" while the burn animation plays), and if you have TWO
    fires going near each other (within CampBurnCheckRadius, default
    100m), finishing one can trigger DespawnWhenCampBurns and instantly
    vanish the OTHER fire's still-working warriors - lower
    CampBurnCheckRadius or turn off DespawnWhenCampBurns if you like to
    run multiple raids close together at once.

v7.10.0: root-caused a real bug behind "nothing happens at all" reports,
including using the item right in a populated home camp (once empty, once
with 6 people) and getting zero warriors both times. The debug log showed
the diagnostic dump reporting "0 total Entity.AllEntities" - meaning the
game's own global registry of active characters was empty at that moment,
even though people were visibly there. That registry (Entity.AllEntities)
only gets an NPC added when that NPC's own script runs OnEnable - if that
hasn't happened (or the NPC uses a path that doesn't trigger it reliably),
the list under-reports what's actually standing right there. Fixed by
adding a fallback: if the normal Entity.AllEntities search comes up empty,
the mod now also does a direct scene scan for AINavMeshHumanoid components
(the physical NPCs themselves, found directly through Unity rather than
through that registry) and tries again from there. The diagnostic dump
also now logs this scene-scan's counts side by side with
Entity.AllEntities's, so if this ever fails again the log will show
exactly which of the two disagrees and by how much - please send over
FireWarriors_debug.txt if "no friendly NPC found" still happens after
this update.

Note (as written at the time - SUPERSEDED BY v7.24.0 below): trying it with
NOBODY at all in the camp and getting no warriors was expected back then,
not a bug - the mod deliberately refused to clone a random hostile NPC as a
"friendly" warrior if it couldn't find a genuinely friendly one nearby. As
of v7.24.0 it no longer needs anyone around at all.

v7.11.0: fixed a warrior occasionally getting permanently stuck (reported
as: horses left fine, everyone else looted fine, but the camp never
burned and one NPC just stood there). The camp not burning was a direct
side effect of that - burning only happens once every warrior from a fire
is done, and a warrior that never finishes means that never happens.
Two fixes:
  - Warriors now give up on a target the instant the game's own
    pathfinding reports it as genuinely unreachable (not just "still
    calculating"), instead of standing there forever waiting to "arrive"
    at a spot they can never path to - they just pick something else.
    The same check applies on the walk back to the fire itself.
  - New WarriorMaxLifetime safety net (default 120s / 2 minutes, exactly
    as requested): no matter what a warrior is doing, it's forced to
    vanish once it's been alive this long. This guarantees a fire's
    session can always finish - and therefore that the camp-burn trigger
    can always fire - even if some other, not-yet-seen way to get stuck
    shows up later.

v7.12.0: PURE DIAGNOSTIC BUILD, no behavior changes. Confirmed with more
testing that "no friendly NPC found" can still happen even with NPCs
CLEARLY visible on screen at the exact moment the item is used - and this
time even the raw scene-wide scan for AINavMeshHumanoid components (added
in v7.10.0) found zero of them anywhere, not just nearby. Since
AINavMeshHumanoid is the only humanoid AI class this game has (checked
directly in the game's own code), that's a real mystery - it means those
visible NPCs aren't being found by either method, for some reason not yet
understood. Added one more, more aggressive diagnostic: it now physically
probes a 30m sphere around the player for colliders (anything solid
enough to interact with the world must have one) and dumps the FULL list
of every component actually attached to each nearby object's root -
whatever class those visible NPCs really use, its name will show up
directly in FireWarriors_debug.txt this way, instead of continuing to
guess. Please test once (with NPCs visibly on screen when you use the
item) and send over FireWarriors_debug.txt - the fix will follow directly
from what that dump shows.

v7.13.0: the v7.12.0 dump came back and gave a clear answer - in all 3
tests, the root-object probe found the player, 3 wild horses, some
bushes and terrain within 30m, and literally nothing else. No humans of
any kind, friendly or hostile. So the mod was behaving correctly - there
really was nobody around at those exact spots (most likely testing near
wild horses rather than right next to camp people). Widened the probe to
80m and made it sort by distance and cap repeated identical objects
(so a stray pile of bushes can't crowd out an actual person a bit
further away) - if you test again standing right next to someone this
time and it still says nobody's there, that dump will now be much more
telling.

v7.14.0: mystery solved for real. Turns out the test spot (~200m between
home camp and an already-cleared-out enemy camp) genuinely had nobody
loaded there - this game streams NPCs in and out of memory by distance,
so once you're far enough from any camp (and the enemy camp's defenders
are all dead), there's briefly nothing anywhere nearby for the mod to
copy as a "friendly warrior template", exactly like the diagnostic
showed. That's not a bug, but it made the mod pretty fragile for exactly
the raiding playstyle it's meant for (stand between camps, summon
warriors to grab loot from a spot that isn't necessarily near your own
people). Real fix: the very first time the mod finds a genuine friendly
NPC (e.g. the first time you summon at/near your own camp), it now
immediately makes a hidden, permanent, inactive clone of it and uses THAT
as the template forever after, instead of holding a reference to the
live NPC. That clone is excluded from the game's distance-based
streaming (it's not a normal wandering/camp NPC, just an inert copy this
mod keeps around), so every summon after the first successful one works
from anywhere on the map for the rest of that game session, regardless
of how far you are from any actual living NPC. You still need at least
ONE successful summon near real people first (to have something to
clone) - so if you're about to go raid somewhere remote, do one test
summon at home camp first.

Minor note: this leaves one extra hidden, inactive template object in
the world for the rest of the session (harmless, but technically visible
to anything that enumerates every GameObject) - not expected to cause
issues, but mentioning it for transparency.

v7.15.0: the permanent template from v7.14.0 only lives in memory for the
current game process - it's not part of your save file, so it's gone
every time you fully close and relaunch the game, meaning that "one
successful summon near real people first" requirement came back every
single relaunch. Fixed by making the mod look for a nearby friendly NPC
by itself in the background every couple of seconds (very cheap check,
no player action needed) until it finds one and caches it - so just
spawning into the game near your own camp (which is normal anyway) is
enough, without needing to remember to press K or use the item as a
"test" first. You'll see a one-time log line "found a friendly NPC
nearby and cached a permanent template" once it happens each session.

v7.16.0: fixed the player getting stuck, unable to move, during the
burn-camp effect (BurnCampWhenDone). The real burn-settlement coroutine
is supposed to restore movement control itself once it finishes, but
that clean-up apparently doesn't always run. Added a safety watchdog that
runs alongside the burn effect: if the player is still locked (or the
game still thinks it's "interacting") a good while after the burn should
have finished, it forces movement control back on directly - both the
relevant flag and the game's own EndInteraction() call are public, so no
workarounds/reflection needed. This is a no-op almost all the time; it
only kicks in for the stuck case. If you ever get stuck again before
updating, try opening and closing your inventory or the map screen first
(that sometimes clears a stuck interaction state on its own) before
resorting to a restart.

v7.17.0: PURE DIAGNOSTIC BUILD (plus one behind-the-scenes safety
improvement above), no behavior changes to looting/capturing itself.
Reported: warriors now spawn reliably (the permanent-template fix is
working), but a run found nothing to loot or capture at all and everyone
walked back within about a minute. Likely explanation: ItemBase.AllItems,
ItemContainer.AllContainers and AnimalController.AllAnimals - the three
lists this search relies on - are each populated the same
OnEnable-registration way that under-reported distant NPCs earlier, so
if the fire is planted far from the actual camp buildings, their loot/
horses may simply not be loaded into the game yet, the same way distant
NPCs weren't. Added a one-time diagnostic dump to FireWarriors_debug.txt
for exactly this case: the first time a warrior finds nothing at all, it
logs the total count of items/containers/animals in the WHOLE game
world, plus how many of those are within LootRadius of the fire. If the
totals are healthy but "within radius" is 0, that confirms it's a
distance/loading issue (plant the fire closer to the actual camp
buildings next time); if even the totals look wrong, that points to a
different bug and the fix will follow from there. Please test once and
send over FireWarriors_debug.txt.

v7.18.0: BurnCampWhenDone is now OFF by default (per request - the game
turns out to already have its own native order/option to auto-burn a
camp once it's been cleared, which is simpler and safer to use than this
mod reimplementing it). The feature/code is still here and works (with
the stuck-player watchdog fix from v7.16.0) if you ever want to turn it
back on, but it's opt-in now rather than on by default.

v7.19.0: fixed warriors sometimes getting permanently stuck walking
toward a target (an item, a container, or a horse) until the 2-minute
WarriorMaxLifetime safety cap kicked in and forced them to vanish - a
debug log confirmed this exact scenario, all 5 warriors stuck in
"MovingToTarget" for the full 120 seconds. Root cause: the game's
NavMesh can compute a "partial path" - it walks the warrior as close as
it can get to a target that isn't fully reachable (something inside a
building interior not fully covered by the navmesh, for example) but
never actually arrives, and the old check only caught the case where the
path was flat-out invalid, not this "gets close but never arrives" case.
Fixed with a general stall timer: if a warrior's remaining distance to
its target hasn't meaningfully shrunk in 6 seconds, the target is now
treated as unreachable and the warrior gives up on it and moves on,
instead of waiting the full 2 minutes. Note: if you use the game's own
fast-forward/time-skip feature while warriors are working, very high
time scale can make NavMesh movement jumpy/unstable in general (that's a
game engine limitation, not something this mod controls) - this fix
should make warriors recover from that much faster than before either
way, since the 6-second stall timer runs on the same (scaled) game clock
as everything else.

v7.20.0: the v7.19.0 stall timer turned out not to be enough - a fresh
test log showed warriors STILL stuck the full 2 minutes. Found the real
cause: a summoned warrior's NavMeshAgent can fail to actually attach to
the NavMesh right at spawn (its spawn point can end up just off the
nearest walkable surface), and once that happens the agent can NEVER be
given a destination - it just sits frozen where it appeared, doing
nothing, for its whole life. The v7.19.0 stall timer never caught this
because it was written to skip its check entirely whenever the agent
wasn't on the NavMesh, which is backwards - that's the worst case, not a
safe one to ignore. Two fixes: (1) right after a warrior is spawned, if
its agent isn't on the NavMesh yet, it's now explicitly snapped onto the
nearest valid point nearby; (2) the stall timer now also counts time
spent off the NavMesh (or with a path request that never resolves)
toward the same 6-second give-up, instead of waiting forever. Also added
a one-time debug log entry the first time a warrior gives up this way,
recording exactly which condition triggered it (off NavMesh / pending
path / stuck remaining distance) - if this still doesn't fully fix it,
that log line will say precisely why.

v7.21.0: that new debug log line paid off - the next test showed
"remainingDistance stuck around 9.26m (pathStatus=PathComplete)", which
means the NavMesh path was totally fine, but the warrior simply wasn't
moving along it. Real cause, found by digging into the game's own NPC
class: a summoned warrior is a full clone of a real NPC, which means it
still has its OWN AI brain fully intact and running underneath - a
"behavior tree" that keeps deciding on its own where that NPC should
walk or stand, completely independently of this mod. That original brain
was still fighting for control of the same NavMeshAgent this mod is also
commanding, which is almost certainly why some warriors would get a
perfectly valid path and then just stop making progress along it - the
two "brains" were fighting over the steering wheel. Fixed by reaching in
and switching off that original brain the moment a warrior is created,
so this mod's own commands are the only ones driving it from then on.
This is a deeper, more likely-to-actually-fix-it change than the last
two, but exactly because of that, please test it thoroughly (a couple of
fires, a few different spots) and send over the debug log either way -
if a warrior still stalls, the log will say whether it's the same brain-
fighting issue resurfacing in a different form or something new.

v7.22.0: good news/bad news from the latest test. Good news: all the
loot got picked up this time. Bad news: the horses didn't get captured
at all, and the debug log showed exactly why - "remainingDistance stuck
around Infinitym (pathStatus=PathComplete)", which (unlike the 9.26m
case from before) means the game couldn't compute ANY path to the horse
whatsoever, not even a partial one. Very likely explanation (matches
what was reported - horses in "a sort of stable"): a horse kept in a
stable or pen can be standing on ground that the game's walkable-area
data doesn't cover at all, so no warrior could ever fully walk up to it
no matter what. Since capturing a horse is already a teleport effect and
not a physical grab, there's no real need to stand right on top of one -
so warriors now capture a horse as soon as they get within
HorseCaptureRadius (10m by default, adjustable) of it in a straight
line, and if the direct path to a horse turns out to be completely
impossible, a warrior now retries toward the closest point outside the
pen that it actually CAN reach instead of just giving up. Separately,
also made the reflection-based fix from v7.21 (switching off a warrior's
original AI brain) more robust - the debug log showed it wasn't
actually finding the field it was looking for by name (possibly a
different game version/build than the one this was originally checked
against), so it now searches more broadly by field type instead of by
an exact hardcoded name.

v7.23.0: everything worked in the last test (both horses captured, all
loot picked up, no more stuck warriors) - v7.22.0 stands as the last
functional fix. This version is a request-only change: removed the K
(summon) and L (dismiss-all) hotkeys entirely. The craftable "Fire
Warrior Campfire" item is now the only way to summon - there's currently
no in-game way to dismiss everything early if something looks stuck (use
WarriorMaxLifetime as the safety net, or just wait).

v7.24.0: you no longer need to have any of your own people nearby to
summon. This was the last real fragility left in the mod: warriors were
built by copying a living friendly NPC, and this game loads NPCs in and
out by distance - so standing out between camps (exactly where you want
to plant a raiding fire) there is often genuinely nobody loaded anywhere
near you to copy, and the summon would do nothing. Every previous
attempt at this (v7.10 through v7.15) worked from the same angle -
hunt harder for a live NPC, or hold onto one once found - and none of
them removed the requirement itself.

The fix comes from how the game itself creates camp members: it does
NOT copy a living person either. Every camp carries the character
prefabs its people are built from, and the game picks one of those,
gives it a look, and assigns it to that camp's side. The mod now does
the same thing. A prefab is part of the game's data rather than
something standing in the world, so it is available everywhere on the
map, at any distance, from the moment you load a save - no test summon,
no walking past your camp first, no waiting.

Summoned warriors are now also explicitly assigned to your faction
after spawning, instead of just inheriting it from whoever they were
copied from. That is what makes the above safe: even if the only camp
the mod can reach is an enemy one, its prefab produces warriors on YOUR
side who merely happen to be dressed like that faction's people.

The old behaviour is still first in line: if a living friendly NPC is
around, that is still what gets copied, exactly as before. The prefab
is the fallback for when there is nobody. New settings, all under a new
[Template] section in the config:
  UseCampPrefabTemplate   - master switch for the fallback above
                            (default true). Turn off to go back to the
                            old live-NPC-only behaviour.
  PreferCampPrefabTemplate - if true, use the prefab FIRST rather than
                            as a fallback (default false). Turn on if
                            you would rather every summon look
                            identical and never depend on who happens
                            to be loaded nearby.
  AllowAnyFactionPrefabTemplate - if no camp of your own faction is
                            loaded, take a prefab from any camp at all
                            (default true). Affects appearance only,
                            never whose side the warriors are on.

v7.25.0: warriors now take wagons too, as requested. Any wagon/cart within
LootRadius of the fire gets brought back to the home camp exactly the way
horses already were, and it arrives still holding whatever was in its own
cargo container. Two wagons are always left alone: one you're currently
driving, and any wagon that already belongs to one of your own camps (so
warriors don't spend the raid shuffling your own carts around).

How ownership works: the game tracks which camp a wagon belongs to in the
wagon's own "RequestedBy" field - its GetWorldGroup() is literally just
that field cast to a camp. So a captured wagon isn't merely parked next to
your camp, it's handed over to it using the game's own mechanism.

Two things worth knowing:
  - A wagon is a physics object on real wheel colliders, not a simple prop
    like a horse. It's set down upright and stationary (its momentum and
    spin are cleared first, or it would arrive still rolling), and parked
    10-16m out from the camp centre rather than right on top of it, since
    a wagon is long enough to land on tents or people otherwise. If one
    ever arrives clipped into something anyway, that's the thing to report.
  - Unlike items, chests and animals, the game keeps no master list of
    wagons anywhere - the only way to find them is a full scan of loaded
    objects, which is far too slow to run per-warrior per-frame. The mod
    therefore does one shared scan every 3 seconds for all warriors at
    once. In practice this only means a wagon that appears mid-raid can
    take up to 3 seconds to be noticed.

New settings: CaptureWagons, WagonCaptureRadius, PrioritizeWagons (all
listed up top, all on/default sensible).

v7.26.0: the Fire Warrior Campfire now stays in the game instead of
needing the game restarted to get it back. Two separate bugs, both with
the same shape - the mod set something up once and assumed it stayed set
up:

  1. THE RECIPE VANISHED WHEN YOU LOADED A SAVE. The game's recipe list
     (ItemRequirements) is a scene object and its "which recipes do I
     know" registry lives in the save file itself, so loading a save
     rebuilds both from scratch and throws away anything added to them.
     The mod injected the recipe once per game launch and then latched a
     flag saying "done", so it never noticed and never put it back. It
     now re-checks a couple of times a second that the recipe is actually
     still there, and silently re-adds it if it isn't - so it survives
     loading a save, loading a different save, dying and reloading, and
     so on. It also re-marks it as "known" only when the game has
     actually forgotten it, so you shouldn't get a "learned a new recipe"
     popup every time you load.

  2. CRAFTED CAMPFIRES DISAPPEARED FROM YOUR INVENTORY AFTER A RESTART.
     The game identifies an item across a save/load by its PrefabPath,
     and the mod was generating a brand new random one every single
     launch - so an item saved yesterday was looking for an identity that
     no longer existed today and couldn't be restored. The path is now a
     fixed value, and the item object itself is kept alive across scene
     loads instead of being destroyed by the first one.

IMPORTANT, PLEASE READ: fix 2 changes how the item is identified, so any
Fire Warrior Campfire already sitting in your inventory from an older
version is still keyed to one of those old random identities and will not
survive the upgrade. Just craft a new one (1 Wood + 1 Flint) - from this
version on, they stick around.

Also worth knowing: the game stores known recipes as positions in its
recipe list, not by name. That works fine while the mod is installed
(the recipe lands back in the same position every time), but it does mean
a save made with this mod has a "known recipe" entry pointing at a slot
that won't exist if you later play without the mod. It hasn't caused a
problem in testing, but it's the reason to keep a save backup.

v7.27.0: the crafting menu now refreshes itself the moment the recipe is
(re-)added. v7.26.0 made the item come back on its own after a save load,
but the menu only ever rebuilds its visible list when you open it - so if
the menu happened to already be open, the entry was there without being
drawn, and you had to close and reopen it. The mod now calls the game's
own refresh on the open menu. That was the last step still needing you to
do something by hand.

To be clear about what "permanent" can mean here: the recipe list belongs
to the game and is rebuilt from the game's own data every time a save
loads, so nothing a mod adds to it can be baked in permanently - not by
this mod, not by any mod. What v7.26/v7.27 do instead is notice within a
couple of seconds and put it back with no action from you. The end result
is the same in practice: the item is simply always there.

Installation
------------
Drop FireWarriors.dll into BepInEx/plugins/ (already done for you if I
placed it) and launch the game normally through Steam, then craft and
use the "Fire Warrior Campfire" item where you want to gather.

How it's built
---------------
No existing game files or code are modified on disk, and no existing
game behaviour is changed - the mod only adds new objects to the world
when you use the item. The one exception, explained below, is a small,
non-destructive Harmony hook used purely to keep the mod's own hotkey
checking running every frame; it does not alter what the hooked methods
do in any way.

  - Hotkey checking runs via a Harmony postfix attached to two of the
    game's own per-frame methods (PlayerActions.Update and
    AdvancedMonoBehaviourManager.Update) rather than a plain Unity
    Update() on the mod's own object. On this game, a freshly added
    MonoBehaviour's own Update()/OnGUI() never actually got invoked by
    the engine (even brand new persistent objects created purely for
    this), for reasons that weren't fully pinned down - piggybacking on
    methods proven to run every frame (the game wouldn't work otherwise)
    sidesteps that reliably. The hooked methods run exactly as they
    normally would; the mod's check just also runs right after them.
  - Warriors are spawned via the game's own AINavMeshHumanoid.CreateHumanoid.
    The thing being copied is, in order of preference: a living friendly NPC
    of your own faction if one is loaded nearby (found once and reused for
    the rest of the session), otherwise the character prefab one of your
    camps spawns its own people from - the same prefab the game itself
    picks from when it populates a camp. That prefab is game data rather
    than something standing in the world, so it works at any distance from
    anyone, which is what removed the old "you must have been near your own
    people at some point this session" requirement.
  - Every spawned warrior is then explicitly assigned to your faction with
    the game's own Entity.SetFaction, rather than just inheriting whatever
    the template had. This has to happen after the warrior is activated,
    because activating it runs the game's own Entity.Awake, which re-applies
    the template's original faction and would otherwise overwrite it.
  - Collected items are handed to your home camp's WorldGroup via its own
    AddItem(...) method - the same storage system camps already use.
  - Storage containers are found via the game's own ItemContainer.AllContainers
    list (every chest/crate in the loaded world); a warrior "empties" one by
    delivering every stack in its AvailableItems list to the home camp and
    then clearing it, using the container's own data structures directly.
  - Wagons are the one thing with no master list in the game to read, so
    they're found with a scan of loaded objects, shared between all
    warriors and repeated at most once every 3 seconds. A wagon is skipped
    if it's wrecked (its own bWagonDead flag), if someone is currently on
    it (the game's own CanInteract check), or if it already belongs to your
    home camp. Capturing one clears its Rigidbody's momentum, sets it down
    upright on sampled walkable ground near the camp, and assigns the camp
    to the wagon's RequestedBy field - the same field the game's own
    GetWorldGroup() reads.
  - The home camp itself is identified from the game's own
    GameplayEvents.PlayerCampCreated event (captured live), with a
    fallback scan of your faction's camps if the mod loads into an
    already-running save.
  - Camp-burning is detected via the game's own
    GameplayEvents.CampWillBeBurned event.
  - The craftable item is created by finding an existing real, working
    placeable item in the crafting menu's own recipe list
    (ItemRequirements.Instance.ItemProduction - preferring one that looks
    like a campfire, otherwise the first placeable item found), cloning it
    with Unity's own Instantiate (so it inherits a real, working icon,
    model and placement behaviour), renaming the clone, and adding it as a
    new recipe entry with KnownOnSpawn = true (so it shows up right away,
    no "learning" needed). The crafting menu (FeaturesDevelopmentControl)
    rebuilds its visible list from that same array every time it's opened,
    so nothing else needs to be told to "refresh".
  - When that specific item is used (matched by its exact display name,
    since crafting hands you a fresh copy each time rather than the same
    object added to the recipe list), a small Harmony hook intercepts it
    before the game's own placement logic runs, consumes one from the
    stack, and calls the exact same summon logic as the K hotkey instead.
    Every other placeable item in the game (real campfires, tents, etc.)
    is completely unaffected - the hook only ever acts on this one item.

Known limitations / please read before relying on this
--------------------------------------------------------
- Please keep testing on a SAVE BACKUP, as a general precaution with any
  mod. If anything looks wrong, two log sources are available:
    BepInEx/LogOutput.log - search for "Fire Warrior" (note: on this game,
      this file has been observed to stop updating partway through a
      session in some cases, even though the game keeps running fine)
    BepInEx/plugins/FireWarriors_debug.txt - a second, independent plain
      text log this mod writes itself, specifically because of the above;
      check this one if LogOutput.log looks stale.
- Warriors pick up items that are physically lying in the world (dropped
  loot, harvestable resources, etc.) AND empty every storage container
  (chests, crates, a camp's stockpile boxes) within range - both are
  emptied straight into your home camp's storage.
- Wagon capture is new in v7.25.0 and is the least-tested part of the mod.
  A wagon is a physics object rather than a simple prop, so if one ever
  arrives at camp clipped into a building, tipped over, or rolling away,
  that's the thing to report - FireWarriors_debug.txt logs every capture
  with the wagon's name and exactly where it was put down.
- The warriors' appearance comes from whatever template was used (a nearby
  friendly NPC if there was one, otherwise a camp's character prefab), so
  they may not all look identical between sessions. If the only camp the
  mod can reach belongs to another faction, they'll be dressed like that
  faction's people - they are still yours either way. Set
  AllowAnyFactionPrefabTemplate to false if you'd rather summon nobody than
  see that.
- If you haven't created your home camp yet, or the mod can't find it,
  collected items are currently just removed instead of delivered (also
  logged) rather than lost silently - let me know if you hit this and
  I'll adjust the fallback behaviour (e.g. drop them at the fire instead).
- The craftable item needs the crafting menu to be opened at least once
  after loading a save before it appears (the mod re-adds the recipe in
  the background within a couple of seconds of loading, but the menu only
  ever rebuilds its list when you open it - so if you don't see it, close
  and reopen the crafting menu). As of v7.26.0 it re-adds itself for the
  rest of the session too, so this is a "wait a second and reopen the
  menu" thing, not a "restart the game" thing. If it doesn't show up at
  all, both log files will have "TryInjectCraftableItem" /
  "EnsureCraftableItemPresent" lines explaining exactly what was
  found/tried - please send that over if so, it'll save a lot of guessing.
- v7.0.0 had a bug where the item would show up in the menu but couldn't
  actually be crafted (no working Craft button). Root cause: the game
  tracks which recipes you've actually "learned" separately from which
  ones are merely visible, and cloning an item the naive way also
  accidentally made the clone look like the exact same recipe as whatever
  it was cloned from for lookup purposes. v7.1.0 fixes both: the clone now
  gets its own distinct identity, and the mod explicitly marks it as known
  right after adding it.
- v7.1.0 fixed crafting it yourself, but ordering camp members to craft it
  still didn't work (a real campfire could be ordered, this item couldn't).
  Reason: that's a THIRD, separate system again - "order camp members to
  craft/gather X" reads from its own master item list (ItemsDB), not the
  same recipe list the player's own crafting menu uses.
- v7.2.0 registered the item into that ItemsDB list too, which did make it
  orderable - but this caused a real game crash in testing. ItemsDB.Items
  is one of the most heavily-used arrays in the whole game (about 590
  entries - almost certainly relied on by saving/loading, social trading,
  UI, etc.), and resizing it at runtime is clearly riskier than it looked.
  As of v7.3.0, this is OFF BY DEFAULT (AllowCampWorkerOrders in the
  config, under [Crafting]) - crafting the item yourself and using it
  works exactly as before with no crash risk, but you can no longer
  (by default) assign it as a work order to camp members. If you want to
  try that anyway knowing it previously crashed, you can flip
  AllowCampWorkerOrders to true, but I'd recommend just crafting/using it
  yourself for now while this gets sorted out properly - it's not worth
  losing progress in a save over.
