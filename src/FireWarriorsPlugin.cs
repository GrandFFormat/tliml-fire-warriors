using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace TLIML.FireWarriors
{
    // ---------------------------------------------------------------------
    // TLIML Fire Warriors
    //
    // Press a hotkey (default F6) to plant a fire in front of the player.
    // The fire summons 10 unarmed warriors who fan out within range
    // (default 100m), pick up every loose item they can reach AND empty
    // every chest/crate/storage container they find, carrying everything
    // straight to the player's home camp (the camp created at the start of
    // the game). Once nothing's left to grab, they walk back into the fire
    // and vanish. If a camp within range is set on fire before they finish,
    // they vanish instantly instead of finishing the job.
    //
    // Built entirely from the game's own public classes/methods - nothing
    // in the game itself is patched or modified, this only calls existing
    // APIs (AINavMeshHumanoid, Entity, ItemBase, ItemContainer, WorldGroup,
    // GameplayEvents) discovered by inspecting Assembly-CSharp.dll.
    // ---------------------------------------------------------------------
    [BepInPlugin(GUID, NAME, VERSION)]
    public class FireWarriorsPlugin : BaseUnityPlugin
    {
        public const string GUID = "local.tliml.firewarriors";
        public const string NAME = "TLIML Fire Warriors";
        public const string VERSION = "7.27.0";

        internal static ManualLogSource Log;

        // BepInEx's own LogOutput.log was observed to stop receiving new
        // lines partway through a session on this game, while everything
        // kept running fine. Kept as a lightweight, independent fallback
        // channel for key events only (not spammy per-frame stuff), written
        // by opening/closing the file each time rather than keeping a
        // handle open for the whole session.
        private static string DebugFilePath;

        internal static void WriteDebug(string msg)
        {
            try
            {
                if (DebugFilePath == null) return;
                File.AppendAllText(DebugFilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch
            {
                // Never let debug file writing itself break anything.
            }
        }

        internal static ConfigEntry<int> WarriorCount;
        internal static ConfigEntry<float> LootRadius;
        internal static ConfigEntry<float> SpawnRingRadius;
        internal static ConfigEntry<float> SpawnDistanceFromPlayer;
        internal static ConfigEntry<bool> DespawnWhenCampBurns;
        internal static ConfigEntry<float> CampBurnCheckRadius;
        internal static ConfigEntry<bool> OnlyItemsMarkedAiPickup;
        internal static ConfigEntry<bool> EnableCraftableCampfireItem;
        internal static ConfigEntry<string> CraftableItemDisplayName;
        internal static ConfigEntry<bool> RequireCraftingCost;
        internal static ConfigEntry<bool> AllowCampWorkerOrders;
        internal static ConfigEntry<bool> UseCustomIngredients;
        internal static ConfigEntry<string> CustomIngredient1Name;
        internal static ConfigEntry<int> CustomIngredient1Amount;
        internal static ConfigEntry<string> CustomIngredient2Name;
        internal static ConfigEntry<int> CustomIngredient2Amount;
        internal static ConfigEntry<bool> CaptureHorses;
        internal static ConfigEntry<bool> TameCapturedHorses;
        internal static ConfigEntry<bool> PrioritizeHorses;
        internal static ConfigEntry<float> WarriorMoveSpeedMultiplier;
        internal static ConfigEntry<bool> BurnCampWhenDone;
        internal static ConfigEntry<float> BurnCampSearchRadius;
        internal static ConfigEntry<float> BurnCampDuration;
        internal static ConfigEntry<float> WarriorMaxLifetime;
        internal static ConfigEntry<float> HorseCaptureRadius;
        internal static ConfigEntry<bool> CaptureWagons;
        internal static ConfigEntry<float> WagonCaptureRadius;
        internal static ConfigEntry<bool> PrioritizeWagons;
        internal static ConfigEntry<bool> UseCampPrefabTemplate;
        internal static ConfigEntry<bool> PreferCampPrefabTemplate;
        internal static ConfigEntry<bool> AllowAnyFactionPrefabTemplate;

        private static readonly List<FireSession> ActiveSessions = new List<FireSession>();
        internal static readonly HashSet<ItemBase> ClaimedItems = new HashSet<ItemBase>();
        internal static readonly HashSet<ItemContainer> ClaimedContainers = new HashSet<ItemContainer>();
        internal static readonly HashSet<AnimalController> ClaimedAnimals = new HashSet<AnimalController>();
        internal static readonly HashSet<WagonControllerEx> ClaimedWagons = new HashSet<WagonControllerEx>();
        internal static bool LoggedEmptySeekDiagnostic = false;
        internal static bool LoggedStallDiagnostic = false;

        private static WorldGroup cachedHomeCampGroup;

        internal static FireWarriorsPlugin Instance;

        private long updateTickCount = 0;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            try
            {
                DebugFilePath = Path.Combine(BepInEx.Paths.PluginPath, "FireWarriors_debug.txt");
                File.WriteAllText(DebugFilePath, "");
                WriteDebug($"=== Fire Warriors v{VERSION} session started ===");
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not set up the plain-text debug file: " + e);
            }

            try
            {
                UnityEngine.Object.DontDestroyOnLoad(this.gameObject);
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not mark plugin GameObject DontDestroyOnLoad: " + e);
            }

            WarriorCount = Config.Bind("General", "WarriorCount", 5,
                "How many unarmed warriors spawn per fire.");
            WarriorMoveSpeedMultiplier = Config.Bind("General", "WarriorMoveSpeedMultiplier", 2.0f,
                "Multiplies how fast summoned warriors walk/run compared to their template's normal speed. 1 = normal speed, 2 = twice as fast.");
            LootRadius = Config.Bind("General", "LootRadius", 100f,
                "How far (meters) around the fire the warriors will search for items/containers, and how far they're allowed to roam from it.");
            SpawnRingRadius = Config.Bind("General", "SpawnRingRadius", 3f,
                "How far around the fire the warriors initially appear.");
            SpawnDistanceFromPlayer = Config.Bind("General", "SpawnDistanceFromPlayer", 4f,
                "How far in front of the player the fire is planted.");
            DespawnWhenCampBurns = Config.Bind("General", "DespawnWhenCampBurns", true,
                "If true, warriors instantly vanish when a nearby camp is set on fire instead of finishing their looting.");
            CampBurnCheckRadius = Config.Bind("General", "CampBurnCheckRadius", 100f,
                "A camp burning further than this from the fire will not affect the summoned warriors.");
            OnlyItemsMarkedAiPickup = Config.Bind("General", "OnlyItemsMarkedAiPickup", false,
                "If true, only pick up loose items the game itself flags as AI-pickable. If false (default), warriors pick up literally everything that can be picked up (containers are always fully emptied either way).");
            EnableCraftableCampfireItem = Config.Bind("Crafting", "EnableCraftableCampfireItem", true,
                "If true, adds a craftable consumable item to the crafting menu that does the exact same thing as the hotkey when used/placed. The hotkey keeps working either way.");
            CraftableItemDisplayName = Config.Bind("Crafting", "CraftableItemDisplayName", "Fire Warrior Campfire",
                "Display name of the craftable item in the crafting menu and inventory.");
            RequireCraftingCost = Config.Bind("Crafting", "RequireCraftingCost", true,
                "If true (default), crafting the item costs the same resources as the real campfire it's cloned from - this exactly matches a known-working recipe's shape, which turned out to matter. If false, it's nearly free (1 of each required resource) instead.");
            AllowCampWorkerOrders = Config.Bind("Crafting", "AllowCampWorkerOrders", false,
                "EXPERIMENTAL, OFF BY DEFAULT: also registers the item into the game's global item database so it can be assigned as a camp work order. This was linked to a game crash during testing - only turn it on if you're comfortable with that risk. The item is always craftable by the player directly either way.");
            UseCustomIngredients = Config.Bind("Crafting", "UseCustomIngredients", true,
                "If true (default), the recipe uses the two ingredients below (default 1 Wood + 1 Flint) instead of the real campfire's full requirement list. If false, falls back to RequireCraftingCost's behavior.");
            CustomIngredient1Name = Config.Bind("Crafting", "CustomIngredient1Name", "Wood",
                "Name of the first required ingredient (must match an existing item's in-game name exactly, case-insensitive). Leave blank to skip.");
            CustomIngredient1Amount = Config.Bind("Crafting", "CustomIngredient1Amount", 1,
                "How many of CustomIngredient1Name are required.");
            CustomIngredient2Name = Config.Bind("Crafting", "CustomIngredient2Name", "Flint",
                "Name of the second required ingredient (must match an existing item's in-game name exactly, case-insensitive). Leave blank to skip.");
            CustomIngredient2Amount = Config.Bind("Crafting", "CustomIngredient2Amount", 1,
                "How many of CustomIngredient2Name are required.");
            CaptureHorses = Config.Bind("General", "CaptureHorses", true,
                "If true (default), warriors also look for horses within LootRadius of the fire (excluding your own personal horse) and bring them to the home camp instead of leaving them behind.");
            TameCapturedHorses = Config.Bind("General", "TameCapturedHorses", true,
                "If true (default), a captured horse is flagged as Tamed once it's delivered to the home camp.");
            PrioritizeHorses = Config.Bind("General", "PrioritizeHorses", true,
                "If true (default), a warrior that spots a capturable horse goes for it immediately instead of finishing nearby items/containers first - this is what makes horses disappear quickly instead of waiting their turn. If false, horses are treated like any other target and picked purely by distance.");
            BurnCampWhenDone = Config.Bind("General", "BurnCampWhenDone", false,
                "OFF BY DEFAULT (was on, but caused the player to occasionally get stuck unable to move during the burn animation - there's a safety watchdog for that now, but this is opt-in going forward). If true, once every warrior from a fire has finished looting/capturing and returned, the mod automatically burns down the nearest enemy camp within BurnCampSearchRadius of that fire, using the game's own burn-settlement effect.");
            BurnCampSearchRadius = Config.Bind("General", "BurnCampSearchRadius", 60f,
                "How far (meters) from the fire to look for a burnable enemy camp once the warriors are done. Your own home camp is never targeted.");
            BurnCampDuration = Config.Bind("General", "BurnCampDuration", 6f,
                "How long (seconds) the burn-down effect takes to play out.");
            WarriorMaxLifetime = Config.Bind("General", "WarriorMaxLifetime", 120f,
                "Hard cap in seconds on how long a single warrior is allowed to exist before it's forced to vanish, no matter what it's doing. This is a safety net against a warrior getting stuck (e.g. bad pathing to an unreachable item) - without it, a stuck warrior would sit there forever and the fire's session would never finish (which also means the camp would never burn, since that only happens once every warrior is done). Default 120 (2 minutes). Set to 0 to disable.");
            HorseCaptureRadius = Config.Bind("General", "HorseCaptureRadius", 10f,
                "How close (in a straight line, meters) a warrior needs to get to a horse to capture it. Capturing is already a teleport, not a physical grab, so a warrior doesn't need to walk right up to the horse - this matters for horses kept in a stable/pen, where the ground under the horse may not be covered by the game's walkable-area data at all, meaning a warrior could never fully arrive there. Default 10.");
            CaptureWagons = Config.Bind("General", "CaptureWagons", true,
                "If true (default), warriors also capture wagons/carts within LootRadius of the fire and bring them back to the home camp, the same way they capture horses. A wagon you're currently driving, and any wagon that already belongs to one of your own camps, are always left alone. The wagon keeps whatever is in its own cargo container - it arrives at your camp loaded.");
            WagonCaptureRadius = Config.Bind("General", "WagonCaptureRadius", 12f,
                "How close (in a straight line, meters) a warrior needs to get to a wagon to capture it. Larger than HorseCaptureRadius by default because a wagon is a big object whose exact centre often sits on ground the game's walkable-area data doesn't cover (inside a camp's fenced yard, for instance), so a warrior may never be able to fully walk up to it.");
            PrioritizeWagons = Config.Bind("General", "PrioritizeWagons", true,
                "If true (default), a warrior that spots a capturable wagon heads straight for it instead of finishing nearby items/containers first - same idea as PrioritizeHorses. If false, wagons are picked purely by distance like anything else.");
            UseCampPrefabTemplate = Config.Bind("Template", "UseCampPrefabTemplate", true,
                "If true (default), and no living friendly NPC can be found to copy, the mod falls back to the character PREFAB your own camps spawn their people from (WorldGroup.EntityPrefabs) - the same prefab the game itself uses. A prefab is an asset, not a streamed-in scene object, so it's available anywhere on the map at any time: this is what removes the old 'you must have been near one of your own people at some point this session' requirement entirely. Turn off only to go back to the old live-NPC-only behaviour.");
            PreferCampPrefabTemplate = Config.Bind("Template", "PreferCampPrefabTemplate", false,
                "If true, use the camp prefab FIRST instead of only as a fallback. Off by default: copying a real, living NPC is the older, more heavily tested path and produces a warrior already carrying that NPC's finished in-game appearance. Turn this on if you'd rather every summon look identical and never depend on who happens to be loaded nearby.");
            AllowAnyFactionPrefabTemplate = Config.Bind("Template", "AllowAnyFactionPrefabTemplate", true,
                "If true (default), and no camp of YOUR faction is loaded to take a prefab from, take one from any camp at all. Warriors are explicitly assigned to your own faction after spawning either way, so this can't produce enemy warriors - only warriors who happen to be dressed like another faction's people. Set to false if you'd rather summon nothing than see that.");

            try
            {
                GameplayEvents.CampWillBeBurned.Subscribe(OnCampWillBeBurned, false);
                GameplayEvents.PlayerCampCreated.Subscribe(OnPlayerCampCreated, false);
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not subscribe to gameplay events: " + e);
            }

            Log.LogInfo($"{NAME} v{VERSION} loaded. Craft/use the '{(CraftableItemDisplayName != null ? CraftableItemDisplayName.Value : "Fire Warrior Campfire")}' item near where you want to gather to summon the fire and warriors.");

            // Hotkey polling is done via a Harmony postfix on the game's own
            // per-frame methods (PlayerActions.Update and
            // AdvancedMonoBehaviourManager.Update) rather than this plugin's
            // own Update()/OnGUI(). On this game, a plain MonoBehaviour
            // Update() attached to this plugin's own GameObject/component
            // never actually got called - piggybacking on methods we know
            // for certain run every frame (the game wouldn't function
            // otherwise) sidesteps that entirely and has proven reliable.
            try
            {
                _harmony = new Harmony(GUID);
                _harmony.PatchAll(typeof(FireWarriorsPlugin).Assembly);
            }
            catch (Exception e)
            {
                Log.LogError("Could not install the Harmony patches: " + e);
                WriteDebug("Could not install the Harmony patches: " + e);
            }
        }

        private Harmony _harmony;

        [HarmonyPatch(typeof(PlayerActions), "Update")]
        private static class PlayerActions_Update_Patch
        {
            static void Postfix()
            {
                // IMPORTANT: plain "Instance != null" is unsafe here - if the
                // underlying GameObject/component was ever destroyed, Unity's
                // overloaded == operator makes a perfectly valid, non-null C#
                // reference COMPARE EQUAL TO NULL, silently skipping this
                // forever with no exception. ReferenceEquals checks true C#
                // nullity instead. (This tripped us up once already - do not
                // "simplify" this back to Instance != null.)
                if (ReferenceEquals(Instance, null)) return;
                try
                {
                    Instance.TickUpdate();
                }
                catch (Exception e)
                {
                    Log?.LogError("PlayerActions.Update postfix threw: " + e);
                    WriteDebug("PlayerActions.Update postfix THREW: " + e);
                }
            }
        }

        // Also hook the game's own master simulation tick
        // (AdvancedMonoBehaviourManager.Update, in the firstpass assembly) as
        // a second, independent trigger point - belt and suspenders in case
        // PlayerActions ever isn't active (e.g. cutscenes).
        [HarmonyPatch(typeof(AdvancedMonoBehaviourManager), "Update")]
        private static class AdvancedMonoBehaviourManager_Update_Patch
        {
            static void Postfix()
            {
                if (ReferenceEquals(Instance, null)) return;
                try
                {
                    Instance.TickUpdate();
                }
                catch (Exception e)
                {
                    Log?.LogError("AdvancedMonoBehaviourManager.Update postfix threw: " + e);
                    WriteDebug("AdvancedMonoBehaviourManager.Update postfix THREW: " + e);
                }
            }
        }

        // Fires when ANY PlaceObjectItem (real campfire kits, tents, etc.) is
        // used. We only act when it's our own injected clone (matched by
        // display Name - the production-list entry's Item is just a
        // template; crafting actually hands the player a separate clone of
        // it, so we can't match by reference identity here). Every other
        // item's placement logic is left completely untouched.
        [HarmonyPatch(typeof(PlaceObjectItem), "Use")]
        private static class PlaceObjectItem_Use_Patch
        {
            static bool Prefix(PlaceObjectItem __instance, Entity owner, bool aiming)
            {
                try
                {
                    if (ReferenceEquals(Instance, null)) return true;
                    if (!EnableCraftableCampfireItem.Value) return true;

                    ItemBase item = __instance;
                    if (item == null || string.IsNullOrEmpty(item.Name)) return true;
                    if (item.Name != CraftableItemDisplayName.Value) return true;

                    Log?.LogInfo("Fire Warrior Campfire item used - summoning instead of normal placement.");
                    WriteDebug("Fire Warrior Campfire item used by an entity - summoning instead of normal placement.");

                    try
                    {
                        int newCount = item.Count - 1;
                        item.Count = newCount;
                        if (newCount <= 0 && item.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(item.gameObject);
                        }
                    }
                    catch (Exception e)
                    {
                        WriteDebug("Failed to consume the Fire Warrior Campfire item stack: " + e);
                    }

                    try
                    {
                        Instance.SummonFire(owner);
                        WriteDebug("SummonFire() from craftable item completed without throwing.");
                    }
                    catch (Exception e)
                    {
                        Log?.LogError("SummonFire (from craftable item) threw: " + e);
                        WriteDebug("SummonFire() from craftable item THREW: " + e);
                    }

                    // Skip the original placement logic entirely - our own
                    // fire visual + warriors already cover it.
                    return false;
                }
                catch (Exception e)
                {
                    Log?.LogError("PlaceObjectItem.Use prefix threw: " + e);
                    WriteDebug("PlaceObjectItem.Use prefix THREW: " + e);
                    return true;
                }
            }
        }

        private int lastProcessedFrame = -1;
        private bool craftItemInjected = false;
        internal static ItemBase InjectedCampfireItem;

        // -----------------------------------------------------------------
        // Crafting menu integration: clone a real placeable item (a
        // campfire-type PlaceObjectItem already known to the game) and add
        // it as a new, always-visible recipe. FeaturesDevelopmentControl
        // (the crafting menu) rebuilds its list from
        // ItemRequirements.Instance.ItemProduction every time it's opened
        // (confirmed via IL inspection - it calls UpdateData() as the last
        // thing OnEnable() does), so simply adding an entry here is enough;
        // no manual "refresh" call is needed. KnownOnSpawn = true is what
        // makes an entry show up regardless of "learned" state.
        // -----------------------------------------------------------------
        // The identity a saved Fire Warrior Campfire uses to find its way back
        // to this item. Fixed, never generated - see the PrefabPath comment in
        // TryInjectCraftableItem.
        private const string CampfireItemPrefabPath = "FireWarriorsCampfireItem_v1";

        // Is our recipe in the CURRENTLY live recipe list? Matched on the item
        // instance, falling back to the display name (which is what the
        // item-use hook matches on anyway).
        private static bool RecipeIsPresent(ItemRequirements reqs)
        {
            var production = reqs.ItemProduction;
            if (production == null) return false;

            string wanted = CraftableItemDisplayName != null ? CraftableItemDisplayName.Value : null;
            for (int i = 0; i < production.Length; i++)
            {
                var entry = production[i];
                if (entry == null || entry.Item == null) continue;
                if (InjectedCampfireItem != null && (object)entry.Item == (object)InjectedCampfireItem) return true;
                if (!string.IsNullOrEmpty(wanted))
                {
                    string n = null;
                    try { n = entry.Item.GetName(); } catch { }
                    if (n == wanted) return true;
                }
            }
            return false;
        }

        // Runs on the periodic tick rather than once per session.
        //
        // ItemRequirements is a MonoBehaviour singleton and WorldState is
        // reloaded from the save file, so BOTH the recipe list we inject into
        // and the "which recipes do I know" registry are replaced wholesale
        // every time a save is loaded - taking our recipe with them. Injecting
        // once and latching a bool (which is what this used to do) therefore
        // meant the item silently vanished from the crafting menu the moment
        // you loaded a save, and only came back if you restarted the game.
        private void EnsureCraftableItemPresent()
        {
            var reqs = ItemRequirements.Instance;
            if (reqs == null || reqs.ItemProduction == null) return;

            if (!RecipeIsPresent(reqs))
            {
                if (craftItemInjected)
                {
                    WriteDebug("EnsureCraftableItemPresent: the Fire Warrior Campfire recipe is gone from ItemRequirements (this happens when a save is loaded - the recipe list is rebuilt from scratch). Re-adding it.");
                }
                TryInjectCraftableItem();
                return;
            }

            // The recipe is listed, but "listed" and "craftable" are separate:
            // being craftable lives in WorldState.KnownItemRequirements, which
            // is restored from the save and so won't know about ours. Re-learn
            // it when needed - but only when needed, since LearnItemRequirement
            // also pops an on-screen "learned" message.
            try
            {
                if (InjectedCampfireItem != null && !MissionExtensions.KnowItemRequirement(InjectedCampfireItem))
                {
                    MissionExtensions.LearnItemRequirement(InjectedCampfireItem);
                    WriteDebug("EnsureCraftableItemPresent: the recipe was listed but no longer marked as known (WorldState was reloaded) - re-learned it.");
                }
            }
            catch (Exception e)
            {
                WriteDebug("EnsureCraftableItemPresent: re-learn check failed (non-fatal): " + e);
            }

            EnsureRegisteredInItemsDb();
        }

        // ItemsDB is a MonoBehaviour too, so it gets rebuilt on a save load in
        // exactly the same way and needs the same treatment.
        private void EnsureRegisteredInItemsDb()
        {
            if (!AllowCampWorkerOrders.Value || InjectedCampfireItem == null) return;
            try
            {
                var itemsDb = ItemsDB.Instance;
                if (itemsDb == null) return;
                var currentItems = itemsDb.Items;
                if (currentItems != null && Array.IndexOf(currentItems, InjectedCampfireItem) >= 0) return;

                var newItems = new ItemBase[(currentItems != null ? currentItems.Length : 0) + 1];
                if (currentItems != null) Array.Copy(currentItems, newItems, currentItems.Length);
                newItems[newItems.Length - 1] = InjectedCampfireItem;
                itemsDb.Items = newItems;
                WriteDebug($"EnsureRegisteredInItemsDb: re-registered the item into ItemsDB.Items (new length = {newItems.Length}).");
            }
            catch (Exception e)
            {
                WriteDebug("EnsureRegisteredInItemsDb failed (non-fatal): " + e);
            }
        }

        // The crafting menu only rebuilds its visible list in its own
        // OnEnable, i.e. when you open it. So if the menu happens to be open
        // at the moment the recipe is (re-)added, the entry exists but isn't
        // drawn until you close and reopen it. Poking the game's own
        // UpdateData() removes that last manual step; it's only called on an
        // actual injection (once per save load at most), never per tick.
        private static void RefreshCraftingMenuIfOpen()
        {
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<FeaturesDevelopmentControl>();
                if (menu == null || !menu.isActiveAndEnabled) return;
                menu.UpdateData();
                WriteDebug("RefreshCraftingMenuIfOpen: the crafting menu was open - refreshed it so the item appears without closing/reopening.");
            }
            catch (Exception e)
            {
                WriteDebug("RefreshCraftingMenuIfOpen failed (non-fatal, just reopen the menu): " + e);
            }
        }

        private void TryInjectCraftableItem()
        {
            var reqs = ItemRequirements.Instance;
            if (reqs == null || reqs.ItemProduction == null)
            {
                WriteDebug("TryInjectCraftableItem: ItemRequirements not ready yet, will retry.");
                return;
            }

            var production = reqs.ItemProduction;

            // Log every candidate once, so if our guess at "what looks like
            // a campfire" is wrong we have full visibility into what was
            // actually available, instead of just silently failing again
            // like earlier attempts at this exact feature did.
            WriteDebug($"TryInjectCraftableItem: scanning {production.Length} existing recipe(s) for a PlaceObjectItem donor.");

            ItemRequirements.ItemProductionDescription donor = null;
            ItemRequirements.ItemProductionDescription firstPlaceable = null;

            foreach (var entry in production)
            {
                if (entry == null || entry.Item == null) continue;
                var placeable = entry.Item as PlaceObjectItem;
                if (placeable == null) continue;

                string itemName = "?";
                try { itemName = entry.Item.GetName(); } catch { }
                WriteDebug($"  candidate PlaceObjectItem: Name='{itemName}' Category='{entry.Item.Category}' Group='{entry.Group}'");

                if (firstPlaceable == null) firstPlaceable = entry;

                string lower = (itemName ?? "").ToLowerInvariant();
                string catLower = (entry.Item.Category ?? "").ToLowerInvariant();
                if (lower.Contains("campfire") || lower.Contains("camp fire") || lower.Contains("fire pit") ||
                    catLower.Contains("campfire") || catLower.Contains("fire"))
                {
                    donor = entry;
                    break;
                }
            }

            if (donor == null) donor = firstPlaceable;

            if (donor == null)
            {
                WriteDebug("TryInjectCraftableItem: no PlaceObjectItem-based recipe found at all yet - will retry.");
                return;
            }

            try
            {
                string donorName = "?";
                try { donorName = donor.Item.GetName(); } catch { }
                WriteDebug($"TryInjectCraftableItem: using donor '{donorName}' (Category='{donor.Item.Category}') to build the Fire Warrior Campfire item.");

                // Reuse the clone we already built if it's still alive. This
                // matters because this method now runs again whenever the
                // recipe goes missing (see EnsureCraftableItemPresent) - and
                // building a second, different clone each time would give the
                // item a new identity on every save load.
                ItemBase clonedItem = InjectedCampfireItem;
                if (clonedItem != null)
                {
                    WriteDebug("TryInjectCraftableItem: reusing the existing Fire Warrior Campfire item - only the recipe entry needs re-adding.");
                }
                else
                {
                    GameObject cloneGo = UnityEngine.Object.Instantiate(donor.Item.gameObject);
                    cloneGo.name = "FireWarriorsCampfireItem_Clone";

                    clonedItem = cloneGo.GetComponent<ItemBase>();
                    if (clonedItem == null)
                    {
                        WriteDebug("TryInjectCraftableItem: clone had no ItemBase component - aborting.");
                        UnityEngine.Object.Destroy(cloneGo);
                        return;
                    }

                    // Without this the clone is destroyed by the next scene
                    // load (which is what loading a save does), taking the
                    // item's identity with it.
                    UnityEngine.Object.DontDestroyOnLoad(cloneGo);
                }

                clonedItem.Name = CraftableItemDisplayName.Value;
                clonedItem.LocaleId = ""; // so GetName() returns Name directly instead of a locale lookup
                clonedItem.BasicDescription = "Plant it to summon 10 fire warriors who gather every loose item and empty every container nearby, delivering it all to your home camp.";
                clonedItem.CanBePickedUp = true;
                clonedItem.CanBeUsed = true;

                // CRITICAL: ItemBase.EqualsTo (used all over the place to look
                // up "which recipe is this item" - FindItemRequirements,
                // MissionExtensions.KnowItemRequirement/LearnItemRequirement)
                // compares by *Prefab reference*, not by component identity or
                // Name. A plain Instantiate() copies PrefabPath verbatim, and
                // ItemBase.Awake() resolves Prefab through a shared,
                // PrefabPath-keyed static cache - so without this, our clone
                // silently resolves to the SAME Prefab as the donor, and the
                // game treats it as literally the same recipe as the donor for
                // lookup purposes (this is very likely why the item showed up
                // in the menu at all - via the donor's already-known state -
                // yet still wasn't actually craftable, and quite possibly why
                // earlier attempts at this exact feature failed too). Give the
                // clone its own distinct Prefab identity so every one of those
                // lookups treats it as its own, separate recipe.
                // This path MUST be stable across sessions, not a fresh GUID
                // each time. ItemBase.Awake() resolves an item's Prefab by
                // looking PrefabPath up in the static pathToPrefab cache, so
                // PrefabPath is effectively the item's persistent identity -
                // a randomly generated one means any Fire Warrior Campfire
                // sitting in your inventory when you save can never be
                // resolved again after a restart, and quietly disappears.
                string uniquePrefabPath = CampfireItemPrefabPath;
                clonedItem.PrefabPath = uniquePrefabPath;
                clonedItem.Prefab = clonedItem;
                try
                {
                    var pathToPrefabField = typeof(ItemBase).GetField("pathToPrefab",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var dict = pathToPrefabField != null
                        ? pathToPrefabField.GetValue(null) as System.Collections.Generic.Dictionary<string, ItemBase>
                        : null;
                    if (dict != null)
                    {
                        // Assign rather than add-if-missing: this cache is
                        // what a saved item uses to find its prefab again, so
                        // it has to point at the clone we're actually using
                        // now, even if a previous one was registered and then
                        // destroyed by a scene load.
                        dict[uniquePrefabPath] = clonedItem;
                    }
                }
                catch (Exception e)
                {
                    WriteDebug("Could not seed ItemBase.pathToPrefab cache for the clone (non-fatal): " + e);
                }

                // The player's personal crafting menu (FeaturesDevelopmentControl)
                // reads straight from ItemRequirements.Instance.ItemProduction,
                // which is all we touched above and is confirmed working. But
                // "order camp members to craft/gather this" (OrderListPanel.
                // GetValidItems) reads from a COMPLETELY SEPARATE master list,
                // ItemsDB.Instance.Items - one of the most heavily-relied-on
                // arrays in the whole game (~590 entries, almost certainly
                // touched by save/load, social trading, UI icon grids, etc.).
                // Resizing it at runtime let camp orders see the item, but
                // caused a crash after some time in a real test - most likely
                // something elsewhere assumes a fixed size or fixed indices
                // into this array that our resize broke. Gated behind an
                // opt-in, OFF-by-default config option rather than enabled
                // unconditionally, since the safe, confirmed-working parts
                // (hotkey + personal crafting) matter more than camp orders
                // working for this one item.
                if (AllowCampWorkerOrders.Value)
                {
                    try
                    {
                        var itemsDb = ItemsDB.Instance;
                        if (itemsDb != null)
                        {
                            var currentItems = itemsDb.Items;
                            if (currentItems == null || Array.IndexOf(currentItems, clonedItem) < 0)
                            {
                                var newItems = new ItemBase[(currentItems != null ? currentItems.Length : 0) + 1];
                                if (currentItems != null) Array.Copy(currentItems, newItems, currentItems.Length);
                                newItems[newItems.Length - 1] = clonedItem;
                                itemsDb.Items = newItems;
                                WriteDebug($"TryInjectCraftableItem: registered clone into ItemsDB.Items (new length = {newItems.Length}) - needed for camp work orders. NOTE: this resize was linked to a crash in testing - AllowCampWorkerOrders is opt-in for a reason.");
                            }
                        }
                        else
                        {
                            WriteDebug("TryInjectCraftableItem: ItemsDB.Instance was null - camp work orders for this item may not work.");
                        }
                    }
                    catch (Exception e)
                    {
                        WriteDebug("TryInjectCraftableItem: failed to register clone into ItemsDB.Items (non-fatal, camp orders for this item may not work): " + e);
                    }
                }
                else
                {
                    WriteDebug("TryInjectCraftableItem: skipping ItemsDB.Items registration (AllowCampWorkerOrders is off by default after it was linked to a crash) - the item will be craftable by the player but not orderable to camp members.");
                }

                var prodDesc = new ItemRequirements.ItemProductionDescription();
                prodDesc.Item = clonedItem;
                prodDesc.Amount = 1;
                prodDesc.Group = donor.Group;
                prodDesc.KnownOnSpawn = true;
                prodDesc.CanBeLearned = false;
                prodDesc.LearnCost = 0;
                prodDesc.SpReward = 0;
                prodDesc.ExtraItems = new ItemBase[0];

                // Preferred path: a short, fixed, easy-to-gather ingredient
                // list (default 1 Wood + 1 Flint) instead of the donor
                // campfire's real (much bigger) requirement list. Looked up
                // by name against ItemsDB.Instance.Items (read-only here -
                // no resizing, so none of the risk the camp-order feature
                // had).
                bool usedCustomIngredients = false;
                if (UseCustomIngredients.Value)
                {
                    var customReqs = new List<ItemRequirements.ItemRequirementDescription>();
                    TryAddCustomIngredient(customReqs, CustomIngredient1Name.Value, CustomIngredient1Amount.Value);
                    TryAddCustomIngredient(customReqs, CustomIngredient2Name.Value, CustomIngredient2Amount.Value);
                    if (customReqs.Count > 0)
                    {
                        prodDesc.Requirements = customReqs.ToArray();
                        usedCustomIngredients = true;
                        WriteDebug($"TryInjectCraftableItem: using {customReqs.Count} custom ingredient(s) instead of the donor's real requirements.");
                    }
                    else
                    {
                        WriteDebug("TryInjectCraftableItem: none of the custom ingredient names matched a real item - falling back to donor-shaped requirements.");
                    }
                }

                // Avoid shipping a truly EMPTY Requirements array - some code
                // paths that check "are there enough of item X" over this
                // array may assume at least one entry and default to "cannot
                // craft" when there are none. If we want it free, keep the
                // same requirement slots as the donor (a real, working
                // recipe) but zero out the amounts, which stays free while
                // matching the shape of a known-good recipe.
                if (usedCustomIngredients)
                {
                    // Requirements already set above.
                }
                else if (RequireCraftingCost.Value && donor.Requirements != null && donor.Requirements.Length > 0)
                {
                    prodDesc.Requirements = donor.Requirements;
                }
                else if (donor.Requirements != null && donor.Requirements.Length > 0)
                {
                    var freeReqs = new ItemRequirements.ItemRequirementDescription[donor.Requirements.Length];
                    for (int i = 0; i < donor.Requirements.Length; i++)
                    {
                        var src = donor.Requirements[i];
                        var copy = new ItemRequirements.ItemRequirementDescription();
                        copy.Item = src.Item;
                        // NOTE: was 0 (truly free) in earlier versions - that
                        // most likely broke the per-item craft/interactable
                        // check in testing (an Amount of 0 is very plausibly
                        // an unhandled edge case, e.g. a divide-by-zero, in
                        // the game's own "how much do you have vs need" UI
                        // code). Use the smallest real amount (1) instead -
                        // still cheap, but matches the shape of every other
                        // working recipe in the game exactly.
                        copy.Amount = 1;
                        copy.ProducedByWorld = src.ProducedByWorld;
                        copy.GatheredFromWorld = src.GatheredFromWorld;
                        freeReqs[i] = copy;
                    }
                    prodDesc.Requirements = freeReqs;
                }
                else
                {
                    // Donor itself had no requirements to copy the shape of -
                    // fall back to an empty array as a last resort.
                    prodDesc.Requirements = new ItemRequirements.ItemRequirementDescription[0];
                }

                var newProduction = new ItemRequirements.ItemProductionDescription[production.Length + 1];
                Array.Copy(production, newProduction, production.Length);
                newProduction[production.Length] = prodDesc;
                reqs.ItemProduction = newProduction;

                // KnownOnSpawn only affects whether the recipe is *listed* -
                // actually being craftable is a separate, save-persisted
                // "known recipes" registry (WorldState.KnownItemRequirements,
                // keyed by array index) that's normally only populated once
                // at world/game start. Since we're injecting well after that
                // point, register it explicitly so the Craft button actually
                // works instead of just showing a locked-looking entry.
                try
                {
                    // Guarded, because LearnItemRequirement also pops an
                    // on-screen "learned" message - and this now runs again
                    // after every save load. In the common case the recipe
                    // lands back at the same array index the save already
                    // recorded, so this is a no-op and stays silent.
                    if (!MissionExtensions.KnowItemRequirement(clonedItem))
                    {
                        MissionExtensions.LearnItemRequirement(clonedItem);
                        WriteDebug("TryInjectCraftableItem: called MissionExtensions.LearnItemRequirement on the clone.");
                    }
                    else
                    {
                        WriteDebug("TryInjectCraftableItem: the recipe was already marked known - no need to re-learn it.");
                    }
                }
                catch (Exception e)
                {
                    WriteDebug("TryInjectCraftableItem: MissionExtensions.LearnItemRequirement failed (non-fatal): " + e);
                }

                InjectedCampfireItem = clonedItem;
                craftItemInjected = true;

                RefreshCraftingMenuIfOpen();

                Log.LogInfo($"Added craftable '{CraftableItemDisplayName.Value}' to the crafting menu (cloned from '{donorName}').");
                WriteDebug($"TryInjectCraftableItem: SUCCESS - injected recipe, new ItemProduction length = {newProduction.Length}.");
            }
            catch (Exception e)
            {
                Log.LogWarning("TryInjectCraftableItem failed: " + e);
                WriteDebug("TryInjectCraftableItem failed: " + e);
            }
        }

        private static void TryAddCustomIngredient(List<ItemRequirements.ItemRequirementDescription> list, string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0) return;
            try
            {
                ItemBase found = FindItemByName(itemName);
                if (found == null)
                {
                    Log.LogWarning($"Fire Warrior Campfire: could not find an item named '{itemName}' to use as an ingredient - skipping it.");
                    WriteDebug($"TryAddCustomIngredient: no item found matching name '{itemName}'.");
                    return;
                }

                var req = new ItemRequirements.ItemRequirementDescription();
                req.Item = found;
                req.Amount = amount;
                req.ProducedByWorld = false;
                req.GatheredFromWorld = true;
                list.Add(req);
                WriteDebug($"TryAddCustomIngredient: using '{itemName}' (matched '{found.GetName()}') x{amount} as an ingredient.");
            }
            catch (Exception e)
            {
                WriteDebug($"TryAddCustomIngredient failed for '{itemName}': " + e);
            }
        }

        // Read-only lookup against the game's global item database - unlike
        // TryInjectCraftableItem's (opt-in, off-by-default) ItemsDB.Items
        // registration, this never modifies that array, so it carries none
        // of that feature's crash risk.
        private static ItemBase FindItemByName(string name)
        {
            try
            {
                var itemsDb = ItemsDB.Instance;
                if (itemsDb == null || itemsDb.Items == null) return null;

                foreach (var it in itemsDb.Items)
                {
                    if (it == null) continue;
                    string n = null;
                    try { n = it.GetName(); } catch { }
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return it;
                    }
                }
            }
            catch (Exception e)
            {
                WriteDebug($"FindItemByName('{name}') failed: " + e);
            }
            return null;
        }

        private void TickUpdate()
        {
            // Guard against double-processing: this can be invoked from both
            // Harmony postfixes in the same rendered frame.
            if (Time.frameCount == lastProcessedFrame) return;
            lastProcessedFrame = Time.frameCount;

            updateTickCount++;

            // Checked continuously, not once: loading a save rebuilds the
            // game's recipe list and known-recipe registry from scratch, which
            // drops our injected item. See EnsureCraftableItemPresent.
            if (EnableCraftableCampfireItem.Value && updateTickCount % 120 == 0)
            {
                try
                {
                    EnsureCraftableItemPresent();
                }
                catch (Exception e)
                {
                    Log.LogWarning("EnsureCraftableItemPresent threw: " + e);
                    WriteDebug("EnsureCraftableItemPresent THREW: " + e);
                }
            }

            // Quietly try to cache the permanent warrior template in the
            // background, without needing an actual K press/item use first.
            // This is what the permanent-clone fix (v7.14.0) needs to have
            // already happened at least once per game session before a
            // summon can work far from any real NPC - checking here means
            // that just walking around near your own camp early on is
            // enough, instead of needing to remember to do a manual "test
            // summon" every time you relaunch the game.
            //
            // As of v7.24.0 this is a convenience rather than a requirement:
            // if it never finds anyone, summoning still works off a camp's
            // character prefab instead. The prefab is grabbed here too, while
            // camps are definitely loaded, so that even a camp unloading
            // later can't take it away.
            if (updateTickCount % 120 == 0 && !HasCachedFriendlyTemplate())
            {
                try
                {
                    TryOpportunisticTemplateCache();
                }
                catch (Exception e)
                {
                    WriteDebug("TryOpportunisticTemplateCache THREW: " + e);
                }
            }

            // NOTE: the K (summon) and L (dismiss-all) hotkeys were removed
            // per request - summoning now only happens through using the
            // craftable item (see TryInjectCraftableItem /
            // "item used by an entity" handling below). DismissAll() is
            // kept as a method in case a way to trigger it is wanted again
            // later, but nothing calls it right now.

            for (int i = ActiveSessions.Count - 1; i >= 0; i--)
            {
                var session = ActiveSessions[i];
                session.Warriors.RemoveAll(w => w == null);

                if (session.SpawningComplete && session.EverHadWarriors && !session.CampBurnTriggered && session.Warriors.Count == 0)
                {
                    session.CampBurnTriggered = true;
                    try
                    {
                        TryBurnNearbyCamp(session);
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning("TryBurnNearbyCamp threw: " + e);
                        WriteDebug("TryBurnNearbyCamp threw: " + e);
                    }
                }

                if (session.IsFinished())
                {
                    ActiveSessions.RemoveAt(i);
                }
            }
        }

        private void TryBurnNearbyCamp(FireSession session)
        {
            if (!BurnCampWhenDone.Value) return;

            var groups = WorldGroup.AllGroups;
            if (groups == null)
            {
                WriteDebug("TryBurnNearbyCamp: WorldGroup.AllGroups was null.");
                return;
            }

            WorldGroup home = cachedHomeCampGroup;
            WorldGroup target = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < groups.Count; i++)
            {
                WorldGroup g = groups[i];
                if (g == null) continue;
                if (home != null && (object)g == (object)home) continue;
                if (g.Mode != WorldGroup.EMode.Camp) continue;
                if (g.BurnType != WorldGroup.EBurnType.Burnable) continue;

                Transform gt = g.cachedTransform != null ? g.cachedTransform : g.transform;
                if (gt == null) continue;

                float dist = Vector3.Distance(gt.position, session.FirePos);
                if (dist > BurnCampSearchRadius.Value) continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = g;
                }
            }

            if (target == null)
            {
                WriteDebug($"TryBurnNearbyCamp: no burnable enemy camp found within {BurnCampSearchRadius.Value}m of the fire at {session.FirePos}.");
                return;
            }

            Entity playerEntity = FindPlayerEntity();
            PlayerActions playerActions = playerEntity != null ? playerEntity.GetComponent<PlayerActions>() : null;
            if (playerActions == null)
            {
                Log.LogWarning("TryBurnNearbyCamp: could not find PlayerActions on the player entity - cannot burn the camp.");
                WriteDebug("TryBurnNearbyCamp: no PlayerActions found - aborting burn.");
                return;
            }

            Log.LogInfo($"The warriors are done - burning down a nearby camp ({bestDist:0.0}m from the fire).");
            WriteDebug($"TryBurnNearbyCamp: burning WorldGroup at ~{bestDist:0.0}m from the fire.");

            GetCoroutineHost().StartCoroutine(BurnCampCoroutine(home, target, playerActions));
        }

        private System.Collections.IEnumerator BurnCampCoroutine(WorldGroup home, WorldGroup target, PlayerActions playerActions)
        {
            try
            {
                GameplayEvents.CampWillBeBurned.Trigger(new GameplayEvents.CampWillBeBurnedEvent { Attacker = home, Target = target });
            }
            catch (Exception e)
            {
                WriteDebug("BurnCampCoroutine: CampWillBeBurned.Trigger threw (continuing anyway): " + e);
            }

            // Run the game's own burn effect and a safety watchdog side by
            // side. Reported once: the player was left stuck, unable to
            // move, after the burn animation - almost certainly because
            // BurnSettlement's own cleanup (which restores movement) never
            // got to run for some reason. The watchdog forcibly restores
            // control after a generous timeout regardless of what happened
            // inside the vanilla coroutine, so this can't leave the player
            // stuck for more than a bounded amount of time again.
            Coroutine burnRoutine = playerActions.StartCoroutine(
                playerActions.BurnSettlement(target, BurnCampDuration.Value, new UnityEngine.ParticleSystem[0]));
            GetCoroutineHost().StartCoroutine(BurnWatchdog(playerActions));

            yield return burnRoutine;

            try
            {
                GameplayEvents.CampBurned.Trigger(new GameplayEvents.CampBurnedEvent { Attacker = home, Position = target.Center });
            }
            catch (Exception e)
            {
                WriteDebug("BurnCampCoroutine: CampBurned.Trigger threw: " + e);
            }

            WriteDebug("BurnCampCoroutine: finished.");
        }

        // Safety net for the real burn-settlement effect: if the player is
        // still locked (interacting, or unable to move/face) a good while
        // after the burn should have finished, force those flags back to
        // normal and call EndInteraction() directly - both are public
        // fields/methods on PlayerActions, so this needs no reflection.
        // This is a no-op almost all the time (the vanilla coroutine
        // cleans up after itself normally); it only kicks in for the
        // "stuck in the animation" case that was reported.
        private System.Collections.IEnumerator BurnWatchdog(PlayerActions playerActions)
        {
            float waitTime = Mathf.Max(1f, BurnCampDuration.Value) + 10f;
            yield return new WaitForSeconds(waitTime);

            try
            {
                if (playerActions != null && (playerActions.interacting || !playerActions.canMoveOrFaceToDirection))
                {
                    Log.LogWarning($"Burn-camp watchdog: player still locked {waitTime:0}s after the burn started - forcing control back on.");
                    WriteDebug("BurnWatchdog: forcing canMoveOrFaceToDirection=true and calling EndInteraction() as a safety recovery.");
                    playerActions.canMoveOrFaceToDirection = true;
                    playerActions.EndInteraction();
                }
            }
            catch (Exception e)
            {
                WriteDebug("BurnWatchdog recovery attempt threw: " + e);
            }
        }

        // forEntity: whoever the fire should appear in front of (e.g. the
        // Entity that used the craftable item). Falls back to the player
        // entity when null, which is what the hotkey path uses.
        private static MonoBehaviour coroutineHost;

        // A dedicated, minimal host for StartCoroutine calls, decoupled
        // from this plugin's own GameObject - whose lifecycle turned out to
        // not always survive whatever this game's scene transitions do,
        // despite DontDestroyOnLoad (StartCoroutine on the plugin's own,
        // by-then-destroyed GameObject threw a NullReferenceException from
        // deep inside Unity, silently breaking every future summon for the
        // rest of that session). This re-creates its host on demand if it's
        // ever found destroyed. NOTE: the plain "== null" check here is
        // deliberate (unlike the ReferenceEquals guards elsewhere in this
        // file) - this one specifically needs Unity's own alive-check to
        // detect a truly destroyed host and recreate it.
        private static MonoBehaviour GetCoroutineHost()
        {
            if (coroutineHost == null)
            {
                var go = new GameObject("FireWarriors_CoroutineHost");
                UnityEngine.Object.DontDestroyOnLoad(go);
                coroutineHost = go.AddComponent<CoroutineHostMarker>();
                WriteDebug("GetCoroutineHost: (re)created the coroutine host GameObject.");
            }
            return coroutineHost;
        }

        private class CoroutineHostMarker : MonoBehaviour { }

        private void SummonFire(Entity forEntity = null)
        {
            Entity playerEntity = forEntity != null ? forEntity : FindPlayerEntity();
            if (playerEntity == null)
            {
                Log.LogWarning("Could not find the player entity (PlayerActions). Aborting summon.");
                return;
            }

            Transform playerT = playerEntity.cachedTransform != null ? playerEntity.cachedTransform : playerEntity.transform;
            Vector3 desired = playerT.position + playerT.forward * SpawnDistanceFromPlayer.Value;

            Vector3 firePos = desired;
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(desired, out navHit, 15f, NavMesh.AllAreas))
            {
                firePos = navHit.position;
            }
            else if (NavMesh.SamplePosition(playerT.position, out navHit, 15f, NavMesh.AllAreas))
            {
                firePos = navHit.position;
            }

            GameObject fireVisual = CreateFireVisual(firePos);

            Entity template = FindWarriorTemplate(playerEntity);
            if (template == null)
            {
                Log.LogWarning("Could not find any humanoid Entity in the scene to use as a warrior template. Aborting summon.");
                if (fireVisual != null) Destroy(fireVisual);
                return;
            }

            var session = new FireSession(firePos, fireVisual);
            ActiveSessions.Add(session);

            // Spawning is spread across frames (one warrior per frame)
            // rather than all at once in the same frame/method call. A
            // camp is a NavMesh-dense area (buildings, fences, other
            // NPCs/agents already there) - creating many NavMeshAgents
            // simultaneously in a busy area is a known way to crash
            // Unity's NavMesh system outright (an engine-level crash, not
            // a catchable C# exception), which matches a report of this
            // crashing specifically when used inside a camp but not
            // out in the open. Spacing the spawns out is the standard,
            // low-risk mitigation for that class of crash.
            // Use a dedicated, self-healing host for the coroutine rather
            // than "this" directly: something in a scene transition once
            // caused this plugin's own GameObject to actually be destroyed
            // mid-session (StartCoroutine on it then throws a
            // NullReferenceException from Unity's own internal alive-check,
            // silently breaking every future summon for the rest of that
            // session). GetCoroutineHost() below re-creates its host on
            // demand if that ever happens again.
            GetCoroutineHost().StartCoroutine(SpawnWarriorsCoroutine(template, playerEntity, firePos, session));
        }

        private System.Collections.IEnumerator SpawnWarriorsCoroutine(Entity template, Entity playerEntity, Vector3 firePos, FireSession session)
        {
            int count = Mathf.Max(1, WarriorCount.Value);
            for (int i = 0; i < count; i++)
            {
                Vector2 offset2d = UnityEngine.Random.insideUnitCircle.normalized *
                                   SpawnRingRadius.Value * (0.4f + 0.6f * UnityEngine.Random.value);
                Vector3 spawnPos = firePos + new Vector3(offset2d.x, 0f, offset2d.y);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(spawnPos, out hit, 10f, NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }

                try
                {
                    SpawnOneWarrior(template, playerEntity, spawnPos, session);
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to spawn warrior #{i}: {e}");
                    WriteDebug($"Failed to spawn warrior #{i}: {e}");
                }

                // A short real-time gap (not just one frame) between each
                // spawn, for extra margin against the same-frame-many-
                // agents NavMesh crash risk noted above.
                yield return new WaitForSeconds(0.05f);
            }

            Log.LogInfo($"Summoned {session.Warriors.Count} warrior(s) at {firePos} (range {LootRadius.Value}m), delivering to the home camp.");
            WriteDebug($"SpawnWarriorsCoroutine: finished spawning, {session.Warriors.Count} warrior(s) at {firePos}.");
            session.SpawningComplete = true;
        }

        private void SpawnOneWarrior(Entity template, Entity playerEntity, Vector3 spawnPos, FireSession session)
        {
            GameObject go = AINavMeshHumanoid.CreateHumanoid(template.gameObject, spawnPos, false, spawnPos);
            if (go == null)
            {
                Log.LogWarning("CreateHumanoid returned null.");
                return;
            }

            // Unity's Instantiate() copies the source's active state - and
            // the permanent template clone created in FindWarriorTemplate
            // is deliberately kept INACTIVE (so it never runs its own AI or
            // shows up in the world). Every warrior cloned from it would
            // otherwise also be born inactive and just silently do nothing.
            // Forcing this explicitly is a no-op when the template happened
            // to be a live, already-active NPC instead.
            if (!go.activeSelf) go.SetActive(true);

            go.name = "FireWarrior_" + session.Warriors.Count;

            Entity entity = go.GetComponent<Entity>();
            AINavMeshHumanoid humanoid = go.GetComponent<AINavMeshHumanoid>();

            // Give it a look and put it on our side. Has to happen after
            // SetActive above, because that's what runs Entity.Awake - and
            // Awake resets the faction from the template's own serialized
            // value, which would undo this if we did it first.
            ApplyWarriorIdentity(go, entity, playerEntity, template);

            try
            {
                if (entity != null)
                {
                    entity.DestroyAllItems();
                    entity.ActiveItem = null;
                }
                if (entity != null && entity.characterWeaponController != null)
                {
                    entity.characterWeaponController.HideOrDropActiveItem(true, false);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not fully disarm a summoned warrior: " + e);
            }

            NavMeshAgent agent = humanoid != null ? humanoid.agent : go.GetComponent<NavMeshAgent>();

            // The cloned NPC still has its OWN AI brain fully running (a
            // Behavior Designer behavior tree driving AINavMeshHumanoid) -
            // it keeps deciding where IT wants to go/stand on its own,
            // fighting our own SetDestination calls every tick. This is
            // very likely why warriors were seen with a perfectly valid,
            // complete path (pathStatus=PathComplete) whose
            // remainingDistance simply never shrank - the original brain
            // kept re-asserting its own destination/stop commands. Reach
            // in (via reflection, since the field is private and its type
            // lives in a Behavior Designer assembly we don't reference)
            // and disable that tree so our own commands are the only ones
            // driving this agent from now on.
            DisableHumanoidBrain(humanoid);

            // A cloned/instantiated agent sometimes fails to auto-snap onto
            // the NavMesh right at spawn (e.g. spawnPos sits a hair off the
            // nearest NavMesh polygon) and is left with isOnNavMesh=false
            // forever after - which means it can NEVER be given a
            // destination and just sits in place doing nothing for the
            // rest of its life. Explicitly sample for the nearest valid
            // point nearby and Warp() onto it so this doesn't silently
            // strand a warrior.
            if (agent != null && !agent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(spawnPos, out hit, 10f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    WriteDebug($"SpawnOneWarrior: agent wasn't on the NavMesh at spawn - warped to nearest point {hit.position} (from {spawnPos}). isOnNavMesh now = {agent.isOnNavMesh}.");
                }
                else
                {
                    WriteDebug($"SpawnOneWarrior: agent wasn't on the NavMesh at spawn and NavMesh.SamplePosition found nothing within 10m of {spawnPos} - this warrior may never be able to move.");
                }
            }

            if (agent != null && WarriorMoveSpeedMultiplier.Value > 0f && WarriorMoveSpeedMultiplier.Value != 1f)
            {
                try
                {
                    agent.speed *= WarriorMoveSpeedMultiplier.Value;
                    agent.acceleration *= WarriorMoveSpeedMultiplier.Value;
                }
                catch (Exception e)
                {
                    Log.LogWarning("Could not adjust a warrior's move speed: " + e);
                }
            }

            FireWarriorAgent controller = go.AddComponent<FireWarriorAgent>();
            controller.Initialize(session.FireTransform, agent, LootRadius, OnlyItemsMarkedAiPickup);

            session.Warriors.Add(controller);
            session.EverHadWarriors = true;
        }

        // Disables the original NPC's own decision-making (its Behavior
        // Designer tree, reached via reflection since we don't reference
        // that assembly) so it stops issuing its own SetDestination/stop
        // commands to the shared NavMeshAgent once we've taken over
        // driving it ourselves. Best-effort: if the field isn't found (or
        // is null, or something throws), this just logs and leaves the
        // original AI running - worst case it behaves the way it did
        // before this fix.
        private static void DisableHumanoidBrain(AINavMeshHumanoid humanoid)
        {
            if (humanoid == null)
            {
                WriteDebug("DisableHumanoidBrain: humanoid was null, nothing to disable.");
                return;
            }
            try
            {
                // Look up the exact field name AND scan for any field whose
                // TYPE lives in the BehaviorDesigner namespace - the exact
                // field name ('behaviorTree') was confirmed against a type
                // dump that may not exactly match the currently-installed
                // game build (e.g. after a Steam update), so name lookup
                // alone isn't reliable. Scanning by field type is more
                // robust to that kind of drift.
                var fields = typeof(AINavMeshHumanoid).GetFields(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                int disabledCount = 0;
                int candidateCount = 0;
                foreach (var f in fields)
                {
                    string ns = f.FieldType.Namespace;
                    bool looksLikeBehaviorDesigner = ns != null && ns.IndexOf("BehaviorDesigner", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool looksLikeBehaviorTreeByName = f.FieldType.Name.IndexOf("BehaviorTree", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksLikeBehaviorDesigner && !looksLikeBehaviorTreeByName) continue;

                    candidateCount++;
                    object value;
                    try { value = f.GetValue(humanoid); }
                    catch (Exception fe) { WriteDebug($"DisableHumanoidBrain: couldn't read field '{f.Name}': {fe}"); continue; }

                    UnityEngine.Behaviour bt = value as UnityEngine.Behaviour;
                    if (bt == null)
                    {
                        WriteDebug($"DisableHumanoidBrain: field '{f.Name}' (type {f.FieldType.FullName}) was null or not a Behaviour - nothing to disable there.");
                        continue;
                    }
                    bt.enabled = false;
                    disabledCount++;
                    WriteDebug($"DisableHumanoidBrain: disabled field '{f.Name}' (type {f.FieldType.FullName}) - this warrior's original AI shouldn't fight our movement commands anymore.");
                }

                if (candidateCount == 0)
                {
                    WriteDebug("DisableHumanoidBrain: found no BehaviorDesigner-typed field on AINavMeshHumanoid at all via reflection - leaving the original AI running.");
                }
                else if (disabledCount == 0)
                {
                    WriteDebug("DisableHumanoidBrain: found BehaviorDesigner-typed field(s) but none held an active Behaviour to disable - leaving the original AI running.");
                }
            }
            catch (Exception e)
            {
                WriteDebug("DisableHumanoidBrain threw (leaving the original AI running): " + e);
            }
        }

        private static GameObject CreateFireVisual(Vector3 position)
        {
            GameObject visual = null;
            try
            {
                FireLightScript existing = UnityEngine.Object.FindObjectOfType<FireLightScript>();
                if (existing != null)
                {
                    visual = UnityEngine.Object.Instantiate(existing.gameObject, position, Quaternion.identity);
                    visual.name = "FireWarriors_Campfire";
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not clone an existing campfire visual, falling back to a simple light: " + e);
            }

            if (visual == null)
            {
                visual = new GameObject("FireWarriors_Campfire");
                visual.transform.position = position;
                Light light = visual.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.55f, 0.15f);
                light.intensity = 3f;
                light.range = 8f;
            }

            return visual;
        }

        private Entity FindPlayerEntity()
        {
            try
            {
                PlayerActions pa = UnityEngine.Object.FindObjectOfType<PlayerActions>();
                if (pa != null && pa.entity != null)
                {
                    return pa.entity;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("FindPlayerEntity failed: " + e);
            }
            return null;
        }

        // Cached once a genuinely friendly template is found, and reused for
        // every future summon. IMPORTANT: without this cache, a second call
        // could fail to find any friendly NPC nearby (e.g. the player moved
        // away from camp) and fall back to copying the FIRST alive humanoid
        // found at all - which can be a hostile NPC, producing enemy-looking
        // "warriors". Always prefer a known-good friendly template over a
        // fresh, unfiltered search.
        private static Entity cachedFriendlyTemplate;

        private static bool HasCachedFriendlyTemplate()
        {
            if (ReferenceEquals(cachedFriendlyTemplate, null) || cachedFriendlyTemplate == null) return false;
            try { return cachedFriendlyTemplate.IsAlive(); }
            catch { return false; }
        }

        // Cheap, silent, background-only version of the friendly-template
        // search used by FindWarriorTemplate: only checks Entity.AllEntities
        // (no scene-wide scan, no physics probe, no warning/diagnostic
        // logging), so it's safe to call every couple of seconds regardless
        // of whether anyone's actually around. The very first time this
        // happens to run while a friendly NPC is nearby (typically almost
        // immediately, since most sessions start at/near the home camp), it
        // caches the same permanent clone a manual summon would - so the
        // player doesn't need to remember to do a "test summon" after every
        // game relaunch for far-away summons to keep working.
        private void TryOpportunisticTemplateCache()
        {
            Entity playerEntity = FindPlayerEntity();
            if (playerEntity == null) return;

            string playerFactionName = playerEntity.Faction != null ? playerEntity.Faction.Name : null;

            // Grab a camp's character prefab while camps are loaded. Cheap,
            // silent, and it makes the fallback immune to camps unloading
            // later on.
            if (UseCampPrefabTemplate.Value && cachedTemplatePrefab == null)
            {
                FindTemplatePrefab(playerFactionName, true);
            }

            if (playerFactionName == null || Entity.AllEntities == null) return;

            Entity friendly = Entity.AllEntities.FirstOrDefault(e =>
                e != null && e != playerEntity && e.gameObject != null &&
                e.GetComponent<AINavMeshHumanoid>() != null &&
                e.GetComponent<FireWarriorAgent>() == null &&
                e.IsAlive() &&
                e.Faction != null && e.Faction.Name == playerFactionName);

            if (friendly != null)
            {
                cachedFriendlyTemplate = CreatePermanentTemplateClone(friendly) ?? friendly;
                Log.LogInfo("Fire Warriors: found a friendly NPC nearby and cached a permanent template in the background - summoning will now work from anywhere for the rest of this session.");
                WriteDebug($"TryOpportunisticTemplateCache: cached '{SafeGetName(friendly)}' as the permanent template.");
            }
        }

        private static Entity FindWarriorTemplate(Entity playerEntity)
        {
            try
            {
                // Note: cachedFriendlyTemplate is (as of the permanent-clone
                // change below) a hidden, inactive, DontDestroyOnLoad clone
                // that this mod itself created and owns - not a reference to
                // some random live NPC. It should never legitimately get
                // destroyed out from under us, but checking Unity's own
                // "!= null" here too (on top of the C#-level ReferenceEquals)
                // means that if it somehow ever does, this notices and just
                // searches/reclones instead of silently misbehaving.
                if (!ReferenceEquals(cachedFriendlyTemplate, null) && cachedFriendlyTemplate != null)
                {
                    bool stillAlive;
                    try { stillAlive = cachedFriendlyTemplate.IsAlive(); }
                    catch { stillAlive = false; }
                    if (stillAlive) return cachedFriendlyTemplate;
                }

                string playerFactionName = playerEntity.Faction != null ? playerEntity.Faction.Name : null;

                // Opt-in: skip the live-NPC search entirely and go straight
                // to the prefab, so every summon is identical and never
                // depends on who happens to be loaded nearby.
                if (PreferCampPrefabTemplate.Value && UseCampPrefabTemplate.Value)
                {
                    Entity preferred = FindTemplatePrefab(playerFactionName);
                    if (preferred != null) return preferred;
                    WriteDebug("FindWarriorTemplate: PreferCampPrefabTemplate is on but no prefab was found - falling back to searching for a live friendly NPC.");
                }

                Func<Entity, bool> isUsableFriendly = e =>
                    e != null && e != playerEntity && e.gameObject != null &&
                    e.GetComponent<AINavMeshHumanoid>() != null &&
                    e.GetComponent<FireWarriorAgent>() == null &&
                    e.IsAlive() &&
                    playerFactionName != null && e.Faction != null && e.Faction.Name == playerFactionName;

                Entity friendly = Entity.AllEntities != null
                    ? Entity.AllEntities.FirstOrDefault(isUsableFriendly)
                    : null;

                if (friendly != null)
                {
                    WriteDebug($"FindWarriorTemplate: found '{SafeGetName(friendly)}' (faction '{(friendly.Faction != null ? friendly.Faction.Name : "(null)")}') via Entity.AllEntities as the template.");
                    cachedFriendlyTemplate = CreatePermanentTemplateClone(friendly) ?? friendly;
                    return cachedFriendlyTemplate;
                }

                // Entity.AllEntities came up empty - this has been observed
                // to happen even with visible camp NPCs around (that list
                // is only ever populated in each Entity's own OnEnable, so
                // if that hasn't run/registered for whatever reason, the
                // list can under-report what's actually standing there).
                // Fall back to a direct scene scan for AINavMeshHumanoid
                // components, which finds the physical GameObjects
                // regardless of whether they ever registered themselves.
                try
                {
                    var allHumanoids = UnityEngine.Object.FindObjectsOfType<AINavMeshHumanoid>();
                    if (allHumanoids != null && allHumanoids.Length > 0)
                    {
                        for (int i = 0; i < allHumanoids.Length; i++)
                        {
                            AINavMeshHumanoid h = allHumanoids[i];
                            if (h == null) continue;
                            Entity e = h.GetComponent<Entity>();
                            if (isUsableFriendly(e))
                            {
                                WriteDebug($"FindWarriorTemplate: found '{SafeGetName(e)}' (faction '{(e.Faction != null ? e.Faction.Name : "(null)")}') via a direct scene scan (Entity.AllEntities missed it) as the template.");
                                cachedFriendlyTemplate = CreatePermanentTemplateClone(e) ?? e;
                                return cachedFriendlyTemplate;
                            }
                        }
                        WriteDebug($"FindWarriorTemplate: scene scan found {allHumanoids.Length} AINavMeshHumanoid(s) total, but none matched (right faction/alive/not-already-a-warrior).");
                    }
                    else
                    {
                        WriteDebug("FindWarriorTemplate: scene scan found 0 AINavMeshHumanoid components at all.");
                    }
                }
                catch (Exception scanEx)
                {
                    WriteDebug("FindWarriorTemplate: scene scan fallback threw: " + scanEx);
                }

                // No living friendly NPC anywhere. That is a completely
                // normal situation in this game rather than a bug: NPCs are
                // streamed in and out by distance, so standing out between
                // camps - exactly the spot you'd want to summon a raiding
                // party - there genuinely is nobody loaded to copy. Instead
                // of giving up, fall back to the character PREFAB a camp
                // spawns its own people from. A prefab is an asset, not a
                // scene object, so it's there regardless of where you are
                // or how long ago you were last near people.
                if (UseCampPrefabTemplate.Value)
                {
                    Entity prefab = FindTemplatePrefab(playerFactionName);
                    if (prefab != null) return prefab;
                }

                // Nothing left to try - do NOT fall back to copying a random
                // (possibly hostile) live humanoid. Better to spawn nothing
                // and say why than to summon enemy lookalikes.
                Log.LogWarning("No friendly NPC of the player's faction found to use as a warrior template - not spawning (to avoid accidentally cloning a hostile NPC).");
                WriteDebug("No friendly NPC found for warrior template - aborting summon rather than risk cloning a hostile NPC.");

                // Diagnostic dump - this has been reported to fail even
                // while standing in a camp with people around, which
                // shouldn't happen. Log every nearby entity and exactly
                // which of the filter's conditions it does/doesn't meet,
                // so the real mismatch (wrong faction name, missing
                // component, not "alive", etc.) shows up directly instead
                // of being guessed at again.
                try
                {
                    WriteDebug($"FindWarriorTemplate diagnostic: player faction = '{(playerFactionName ?? "(null)")}'. Nearby entities (<=60m):");
                    Transform playerT = playerEntity.cachedTransform != null ? playerEntity.cachedTransform : playerEntity.transform;
                    int logged = 0;
                    int total = 0;
                    if (Entity.AllEntities != null)
                    {
                        foreach (var e in Entity.AllEntities)
                        {
                            if (e == null || e == playerEntity) continue;
                            total++;
                            Transform et = e.cachedTransform != null ? e.cachedTransform : e.transform;
                            float dist = (playerT != null && et != null) ? Vector3.Distance(playerT.position, et.position) : -1f;
                            if (et == null || dist > 60f) continue;

                            string eName = SafeGetName(e);
                            string eFaction = e.Faction != null ? e.Faction.Name : "(null)";
                            bool hasHumanoid = e.GetComponent<AINavMeshHumanoid>() != null;
                            bool isFireWarrior = e.GetComponent<FireWarriorAgent>() != null;
                            bool alive = false;
                            try { alive = e.IsAlive(); } catch { }

                            WriteDebug($"  Name='{eName}' Faction='{eFaction}' dist={dist:0.0}m AINavMeshHumanoid={hasHumanoid} IsFireWarrior={isFireWarrior} IsAlive={alive}");
                            logged++;
                            if (logged >= 40) break;
                        }
                    }
                    WriteDebug($"FindWarriorTemplate diagnostic: {logged} nearby (of {total} total Entity.AllEntities).");

                    try
                    {
                        var allHumanoids = UnityEngine.Object.FindObjectsOfType<AINavMeshHumanoid>();
                        int humanoidTotal = allHumanoids != null ? allHumanoids.Length : 0;
                        int humanoidNearby = 0;
                        int loggedScan = 0;
                        if (allHumanoids != null)
                        {
                            for (int i = 0; i < allHumanoids.Length; i++)
                            {
                                AINavMeshHumanoid h = allHumanoids[i];
                                if (h == null) continue;
                                Transform ht = h.transform;
                                float dist = (playerT != null && ht != null) ? Vector3.Distance(playerT.position, ht.position) : -1f;
                                if (dist > 60f) continue;
                                humanoidNearby++;

                                Entity e = h.GetComponent<Entity>();
                                string eName = e != null ? SafeGetName(e) : h.name;
                                string eFaction = e != null && e.Faction != null ? e.Faction.Name : "(no Entity/no faction)";
                                bool inAllEntities = e != null && Entity.AllEntities != null && Entity.AllEntities.Contains(e);
                                WriteDebug($"  [scan] Name='{eName}' Faction='{eFaction}' dist={dist:0.0}m HasEntity={e != null} InAllEntitiesList={inAllEntities}");
                                loggedScan++;
                                if (loggedScan >= 40) break;
                            }
                        }
                        WriteDebug($"FindWarriorTemplate diagnostic: scene scan found {humanoidNearby} AINavMeshHumanoid(s) within 60m (of {humanoidTotal} total in the whole scene) - compare against the {total} total from Entity.AllEntities above.");
                    }
                    catch (Exception scanDiagEx)
                    {
                        WriteDebug("FindWarriorTemplate diagnostic scene-scan dump failed: " + scanDiagEx);
                    }

                    // Last resort: if BOTH of the above found nothing even
                    // though NPCs were reportedly visible on screen, they
                    // must be built from some component we're not checking
                    // for at all. Physically probe for nearby colliders
                    // (any NPC needs one to interact with the world) and
                    // dump every component actually attached to each one's
                    // root object, so the real class name shows up directly
                    // instead of continuing to guess.
                    try
                    {
                        if (playerT != null)
                        {
                            Collider[] nearbyColliders = Physics.OverlapSphere(playerT.position, 80f);
                            WriteDebug($"FindWarriorTemplate diagnostic: root-object probe found {(nearbyColliders != null ? nearbyColliders.Length : 0)} collider(s) within 80m - dumping each root object's components (closest first):");

                            var seenRoots = new HashSet<Transform>();
                            var rootDists = new List<KeyValuePair<Transform, float>>();
                            if (nearbyColliders != null)
                            {
                                foreach (var col in nearbyColliders)
                                {
                                    if (col == null) continue;
                                    Transform root = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform.root;
                                    if (root == null || seenRoots.Contains(root)) continue;
                                    seenRoots.Add(root);
                                    float dist = Vector3.Distance(playerT.position, root.position);
                                    rootDists.Add(new KeyValuePair<Transform, float>(root, dist));
                                }
                            }

                            // Closest first, and skip logging more than 3
                            // objects that share an identical name (piles of
                            // identical bushes/rocks otherwise crowd out a
                            // person standing a bit further away) - so
                            // widening the radius above doesn't just bury
                            // the one object that actually matters.
                            rootDists.Sort((a, b) => a.Value.CompareTo(b.Value));
                            var nameCounts = new Dictionary<string, int>();
                            int rootsDumped = 0;
                            foreach (var kv in rootDists)
                            {
                                Transform root = kv.Key;
                                if (root == null) continue;
                                string rootName = root.name;
                                int seenCount = nameCounts.TryGetValue(rootName, out var c) ? c : 0;
                                nameCounts[rootName] = seenCount + 1;
                                if (seenCount >= 3) continue;

                                Component[] comps = root.GetComponents<Component>();
                                string compList = comps != null
                                    ? string.Join(", ", comps.Where(comp => comp != null).Select(comp => comp.GetType().Name).Distinct().ToArray())
                                    : "(none)";
                                WriteDebug($"  [root] '{rootName}' dist={kv.Value:0.0}m components=[{compList}]");
                                rootsDumped++;
                                if (rootsDumped >= 60) break;
                            }
                            WriteDebug($"FindWarriorTemplate diagnostic: root-object probe dumped {rootsDumped} of {rootDists.Count} unique root object(s).");
                        }
                    }
                    catch (Exception probeEx)
                    {
                        WriteDebug("FindWarriorTemplate diagnostic root-object probe failed: " + probeEx);
                    }
                }
                catch (Exception diagEx)
                {
                    WriteDebug("FindWarriorTemplate diagnostic dump failed: " + diagEx);
                }

                return null;
            }
            catch (Exception e)
            {
                Log.LogWarning("FindWarriorTemplate failed: " + e);
                return null;
            }
        }

        // The moment a genuinely friendly NPC is found, this makes a
        // hidden, permanently-kept clone of it to use as the template from
        // then on, instead of holding onto a reference to the live NPC
        // itself. This directly fixes a real scenario: this game streams
        // NPCs in/out of memory by distance, so a live NPC reference found
        // near your own camp becomes useless (or gets fully destroyed) the
        // moment you walk far enough away - e.g. standing ~200m out between
        // your camp and a raided enemy camp, with nobody currently loaded
        // anywhere nearby. A hidden, inactive, DontDestroyOnLoad clone
        // never gets caught up in that streaming, so once this succeeds
        // once per game session (e.g. the first time you summon at/near
        // your own camp), every future summon works from anywhere,
        // regardless of distance from any actual living NPC.
        private static Entity CreatePermanentTemplateClone(Entity source)
        {
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
                clone.name = "FireWarriors_PermanentTemplate";
                clone.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(clone);

                Entity cloneEntity = clone.GetComponent<Entity>();
                if (cloneEntity == null)
                {
                    WriteDebug("CreatePermanentTemplateClone: clone had no Entity component (unexpected) - falling back to the live NPC reference.");
                    UnityEngine.Object.Destroy(clone);
                    return null;
                }

                WriteDebug("CreatePermanentTemplateClone: created a permanent hidden clone of the friendly template - future summons will work even far from any currently-loaded friendly NPC.");
                return cloneEntity;
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not create a permanent template clone (will keep using the live NPC reference instead, which may stop working if it despawns): " + e);
                WriteDebug("CreatePermanentTemplateClone failed: " + e);
                return null;
            }
        }

        // -----------------------------------------------------------------
        // Prefab-based template (v7.24.0)
        //
        // The whole "you need to have been near one of your own people at
        // some point this session" requirement came from the mod copying a
        // LIVE NPC. This game streams NPCs in and out by distance, so out
        // between camps there is often literally nobody loaded to copy.
        //
        // But the game doesn't build its own camp members from live NPCs
        // either - every WorldGroup (camp) carries an EntityPrefabs array,
        // and its own spawn code picks a random element from it, applies a
        // customization preset, then calls Entity.SetFaction(group.Faction).
        // Those prefabs are assets: they are not streamed, cannot die, and
        // are reachable from any camp object at any distance. Copying one is
        // both closer to what the game itself does and available everywhere,
        // which is what removes the requirement outright.
        // -----------------------------------------------------------------
        private static Entity cachedTemplatePrefab;
        private static string cachedTemplateCustomization;

        private static bool PrefabIsUsableHumanoid(Entity prefab)
        {
            // Deliberately no IsAlive() check here: a prefab has never had
            // Awake() run, so its health is still uninitialised. That's fine
            // - Entity.Awake calls InitializeStatusVariables() the moment an
            // instance is actually created from it.
            return prefab != null
                && prefab.gameObject != null
                && prefab.GetComponent<AINavMeshHumanoid>() != null;
        }

        // Picks a character prefab out of a camp, preferring the player's own
        // faction. Also remembers that camp's customization list, so warriors
        // spawned from the prefab get dressed the same way that camp's own
        // people would be rather than left on the prefab's raw default.
        // quiet: called from the 2-second background tick, where a failure is
        // completely routine and logging it every time would bury the log.
        private static Entity FindTemplatePrefab(string playerFactionName, bool quiet = false)
        {
            try
            {
                // A prefab found earlier is an asset, not a scene object -
                // streaming never touches it and it can't die, so once we
                // hold one it stays valid for the rest of the session even
                // if every camp in the world unloads.
                if (cachedTemplatePrefab != null) return cachedTemplatePrefab;

                List<WorldGroup> groups = WorldGroup.AllGroups;
                if (groups == null || groups.Count == 0)
                {
                    if (!quiet) WriteDebug("FindTemplatePrefab: WorldGroup.AllGroups is empty - no camp to take a character prefab from.");
                    return null;
                }

                // Two passes: the player's own faction first, then (if
                // allowed) anyone at all. Faction is reassigned explicitly on
                // every spawned warrior, so a second-pass prefab only affects
                // how the warriors LOOK, never whose side they're on.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool ownFactionOnly = pass == 0;
                    if (!ownFactionOnly && !AllowAnyFactionPrefabTemplate.Value) break;
                    if (ownFactionOnly && playerFactionName == null) continue;

                    for (int gi = 0; gi < groups.Count; gi++)
                    {
                        WorldGroup g = groups[gi];
                        if (g == null || g.EntityPrefabs == null) continue;
                        if (ownFactionOnly && (g.Faction == null || g.Faction.Name != playerFactionName)) continue;

                        for (int pi = 0; pi < g.EntityPrefabs.Length; pi++)
                        {
                            Entity prefab = g.EntityPrefabs[pi];
                            if (!PrefabIsUsableHumanoid(prefab)) continue;

                            cachedTemplatePrefab = prefab;
                            cachedTemplateCustomization = PickCustomization(g);
                            WriteDebug($"FindTemplatePrefab: using character prefab '{prefab.name}' from camp '{g.name}' (faction '{(g.Faction != null ? g.Faction.Name : "(null)")}', own faction = {ownFactionOnly}), customization '{cachedTemplateCustomization ?? "(none)"}'. Summoning now works anywhere, with or without people nearby.");
                            return prefab;
                        }
                    }
                }

                if (!quiet) WriteDebug($"FindTemplatePrefab: checked {groups.Count} camp(s), none had a usable humanoid character prefab in EntityPrefabs.");
                return null;
            }
            catch (Exception e)
            {
                if (!quiet)
                {
                    Log.LogWarning("FindTemplatePrefab failed: " + e);
                    WriteDebug("FindTemplatePrefab failed: " + e);
                }
                return null;
            }
        }

        private static string PickCustomization(WorldGroup g)
        {
            try
            {
                string[] options = g.AvailableCharacterCustomizations;
                if (options == null || options.Length == 0) return null;
                return options[UnityEngine.Random.Range(0, options.Length)];
            }
            catch { return null; }
        }

        // Mirrors what WorldGroup's own spawn coroutine does to a freshly
        // instantiated character: give it a look, then put it on a side.
        // Applied to every warrior regardless of where the template came
        // from - for a live-NPC template the faction call is a harmless
        // no-op (it's already yours), and it's what makes a prefab from some
        // other camp safe to use.
        private static void ApplyWarriorIdentity(GameObject go, Entity entity, Entity playerEntity, Entity template)
        {
            try
            {
                // Only when this warrior actually came from a camp prefab. A
                // warrior copied from a living NPC already carries that NPC's
                // finished in-game look, and re-rolling a preset over it would
                // change how the old, proven path looks - the background tick
                // can have cached a prefab's customization without that prefab
                // being the template we ended up using.
                bool fromPrefab = template != null && cachedTemplatePrefab != null && template == cachedTemplatePrefab;
                if (fromPrefab && cachedTemplateCustomization != null)
                {
                    CharacterCustomization customization = go.GetComponent<CharacterCustomization>();
                    if (customization != null && customization.HasPreset(cachedTemplateCustomization))
                    {
                        customization.ApplyPreset(cachedTemplateCustomization, new System.Random());
                    }
                }
            }
            catch (Exception e)
            {
                // Purely cosmetic - never let it stop a warrior from working.
                WriteDebug("ApplyWarriorIdentity: could not apply a customization preset (cosmetic only, ignoring): " + e);
            }

            try
            {
                if (entity != null && playerEntity != null && playerEntity.Faction != null &&
                    (entity.Faction == null || entity.Faction.Name != playerEntity.Faction.Name))
                {
                    entity.SetFaction(playerEntity.Faction);
                    WriteDebug($"ApplyWarriorIdentity: assigned a summoned warrior to faction '{playerEntity.Faction.Name}'.");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not set a summoned warrior's faction: " + e);
                WriteDebug("ApplyWarriorIdentity: SetFaction failed: " + e);
            }
        }

        // -----------------------------------------------------------------
        // Wagons (v7.25.0)
        //
        // Unlike items, containers and animals, wagons have no static
        // registry in the game to read (ItemBase.AllItems and friends have
        // one; WagonControllerEx does not), so finding them means
        // FindObjectsOfType - which walks every loaded object and is far too
        // expensive to call from each warrior's per-frame seek tick. Hence
        // one shared, time-throttled scan for all warriors.
        // -----------------------------------------------------------------
        private const float WagonScanInterval = 3f;
        private static readonly List<WagonControllerEx> cachedWagons = new List<WagonControllerEx>();
        private static float lastWagonScanTime = float.NegativeInfinity;

        internal static List<WagonControllerEx> GetKnownWagons()
        {
            if (Time.time - lastWagonScanTime < WagonScanInterval) return cachedWagons;
            lastWagonScanTime = Time.time;

            cachedWagons.Clear();
            try
            {
                WagonControllerEx[] found = UnityEngine.Object.FindObjectsOfType<WagonControllerEx>();
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        if (found[i] != null) cachedWagons.Add(found[i]);
                    }
                }
            }
            catch (Exception e)
            {
                WriteDebug("GetKnownWagons: wagon scan failed: " + e);
            }
            return cachedWagons;
        }

        // Whose wagon is it? The game stores that in WagonControllerEx's own
        // RequestedBy field (its GetWorldGroup() is literally just a cast of
        // it), so that's both how we tell one of ours from a target, and how
        // we hand a captured one over to the home camp.
        internal static WorldGroup GetWagonOwnerGroup(WagonControllerEx wagon)
        {
            try { return wagon.RequestedBy as WorldGroup; }
            catch { return null; }
        }

        private static string SafeGetName(Entity e)
        {
            try { return e.GetName(); } catch { return "?"; }
        }

        // -----------------------------------------------------------------
        // The home camp: the WorldGroup created via GameplayEvents.PlayerCampCreated.
        // Re-resolved defensively (camps get recreated across save loads).
        // -----------------------------------------------------------------
        private void OnPlayerCampCreated(GameplayEvents.PlayerCampCreatedEvent evt)
        {
            if (evt.Group != null)
            {
                cachedHomeCampGroup = evt.Group;
                Log.LogInfo("Captured the player's home camp for Fire Warrior deliveries.");
            }
        }

        internal static WorldGroup ResolveHomeCampGroup(Entity playerEntity)
        {
            if (cachedHomeCampGroup != null) return cachedHomeCampGroup;

            try
            {
                string playerFactionName = playerEntity != null && playerEntity.Faction != null ? playerEntity.Faction.Name : null;
                if (playerFactionName == null || WorldGroup.FriendlyGroups == null) return null;

                WorldGroup found = WorldGroup.FriendlyGroups.FirstOrDefault(g =>
                    g != null && g.Faction != null && g.Faction.Name == playerFactionName &&
                    g.Mode == WorldGroup.EMode.Camp);

                if (found != null)
                {
                    cachedHomeCampGroup = found;
                }
                return found;
            }
            catch (Exception e)
            {
                Log.LogWarning("ResolveHomeCampGroup failed: " + e);
                return null;
            }
        }

        private void OnCampWillBeBurned(GameplayEvents.CampWillBeBurnedEvent evt)
        {
            if (!DespawnWhenCampBurns.Value) return;
            if (evt.Target == null) return;

            Transform targetT;
            try
            {
                targetT = evt.Target.cachedTransform != null ? evt.Target.cachedTransform : evt.Target.transform;
            }
            catch
            {
                return;
            }
            if (targetT == null) return;

            foreach (var session in ActiveSessions)
            {
                if (session.FireTransform == null) continue;
                float dist = Vector3.Distance(session.FireTransform.position, targetT.position);
                if (dist <= CampBurnCheckRadius.Value)
                {
                    Log.LogInfo($"Camp near a summoned fire is being burned ({dist:0.0}m away) - warriors vanish.");
                    session.VanishImmediately();
                }
            }
        }

        private void DismissAll()
        {
            foreach (var session in ActiveSessions)
            {
                session.VanishImmediately();
            }
            Log.LogInfo("Dismissed all summoned fires and warriors.");
        }

        private class FireSession
        {
            public readonly Transform FireTransform;
            public readonly GameObject FireVisual;
            public readonly Vector3 FirePos;
            public readonly List<FireWarriorAgent> Warriors = new List<FireWarriorAgent>();

            // Set once SpawnWarriorsCoroutine has added every warrior it's
            // going to add, so the "all warriors gone -> burn the camp"
            // check below can't fire while warriors are still mid-spawn
            // (Warriors.Count would otherwise briefly read 0).
            public bool SpawningComplete = false;
            public bool CampBurnTriggered = false;
            public bool EverHadWarriors = false;

            public FireSession(Vector3 firePos, GameObject fireVisual)
            {
                FireVisual = fireVisual;
                FirePos = firePos;
                FireTransform = fireVisual != null ? fireVisual.transform : new GameObject("FireWarriors_FirePoint").transform;
                if (fireVisual == null) FireTransform.position = firePos;
            }

            public bool IsFinished()
            {
                Warriors.RemoveAll(w => w == null);
                return Warriors.Count == 0 && FireVisual == null;
            }

            public void VanishImmediately()
            {
                foreach (var w in Warriors)
                {
                    if (w != null) w.ForceVanishImmediate();
                }
                Warriors.Clear();
                if (FireVisual != null) UnityEngine.Object.Destroy(FireVisual);
            }
        }
    }

    // -------------------------------------------------------------------
    // Per-warrior brain: find an item or a container, walk to it, empty
    // it, repeat. When nothing is left in range, walk back to the fire
    // and vanish. Everything collected is delivered to the home camp.
    // -------------------------------------------------------------------
    public class FireWarriorAgent : MonoBehaviour
    {
        private enum State { SeekingItem, MovingToTarget, PickingUp, Returning, Vanishing }
        private enum TargetKind { None, LooseItem, Container, Horse, Wagon }

        private Transform fireTransform;
        private NavMeshAgent agent;
        private ConfigEntry<float> lootRadiusConfig;
        private ConfigEntry<bool> onlyAiPickupConfig;

        private State state = State.SeekingItem;
        private TargetKind targetKind = TargetKind.None;
        private ItemBase currentItem;
        private ItemContainer currentContainer;
        private AnimalController currentAnimal;
        private WagonControllerEx currentWagon;
        private float pickupTimer;
        private float lifeTimer;
        private float stallTimer;
        private float lastRemainingDistance;
        private const float StallTimeout = 6f;
        private bool captureFallbackActive;
        private Vector3 captureFallbackPos;
        private const float CaptureNavSampleRadius = 20f;
        private const float PickupDuration = 0.6f;
        private const float ContainerEmptyDuration = 1.2f;
        private const float HorseCaptureDuration = 0.4f;
        private const float WagonCaptureDuration = 0.8f;
        private const float ArriveDistance = 1.6f;
        private const float ReturnArriveDistance = 1.2f;

        public void Initialize(Transform fire, NavMeshAgent navAgent, ConfigEntry<float> lootRadius, ConfigEntry<bool> onlyAiPickup)
        {
            fireTransform = fire;
            agent = navAgent;
            lootRadiusConfig = lootRadius;
            onlyAiPickupConfig = onlyAiPickup;
        }

        private void Update()
        {
            if (fireTransform == null)
            {
                Destroy(gameObject);
                return;
            }

            // Hard safety cap: no matter what a warrior is doing (stuck on
            // bad pathing to an unreachable target, endlessly recalculating
            // a path, whatever), it never lives longer than this. Without
            // it, one stuck warrior means the fire's session never finishes
            // (Warriors.Count never reaches 0), which also means the camp
            // never burns, since that's gated on every warrior being done.
            float maxLifetime = FireWarriorsPlugin.WarriorMaxLifetime != null ? FireWarriorsPlugin.WarriorMaxLifetime.Value : 120f;
            if (maxLifetime > 0f)
            {
                lifeTimer += Time.deltaTime;
                if (state != State.Vanishing && lifeTimer >= maxLifetime)
                {
                    FireWarriorsPlugin.Log.LogInfo("A warrior hit its max lifetime without finishing (likely stuck) - forcing it to vanish.");
                    FireWarriorsPlugin.WriteDebug($"FireWarriorAgent: hit WarriorMaxLifetime ({maxLifetime}s) in state {state} - forcing vanish.");
                    ForceVanishImmediate();
                    return;
                }
            }

            try
            {
                switch (state)
                {
                    case State.SeekingItem: TickSeeking(); break;
                    case State.MovingToTarget: TickMovingToTarget(); break;
                    case State.PickingUp: TickPickingUp(); break;
                    case State.Returning: TickReturning(); break;
                    case State.Vanishing: break;
                }
            }
            catch (Exception e)
            {
                FireWarriorsPlugin.Log.LogError("FireWarriorAgent error: " + e);
                Destroy(gameObject);
            }
        }

        private void TickSeeking()
        {
            float radius = lootRadiusConfig != null ? lootRadiusConfig.Value : 100f;
            bool onlyAi = onlyAiPickupConfig != null && onlyAiPickupConfig.Value;

            ItemBase bestItem = null;
            float bestItemDist = float.MaxValue;

            var all = ItemBase.AllItems;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    ItemBase item = all[i];
                    if (item == null) continue;
                    if (FireWarriorsPlugin.ClaimedItems.Contains(item)) continue;
                    if (!item.CanBePickedUp) continue;
                    if (onlyAi && !item.AiCanPickup) continue;
                    if (item.HasOwner()) continue;

                    Transform it = item.cachedTransform != null ? item.cachedTransform : item.transform;
                    if (it == null || !item.gameObject.activeInHierarchy) continue;

                    float distFromFire = Vector3.Distance(it.position, fireTransform.position);
                    if (distFromFire > radius) continue;

                    float distFromMe = Vector3.Distance(it.position, transform.position);
                    if (distFromMe < bestItemDist)
                    {
                        bestItemDist = distFromMe;
                        bestItem = item;
                    }
                }
            }

            ItemContainer bestContainer = null;
            float bestContainerDist = float.MaxValue;

            var containers = ItemContainer.AllContainers;
            if (containers != null)
            {
                for (int i = 0; i < containers.Count; i++)
                {
                    ItemContainer c = containers[i];
                    if (c == null) continue;
                    if (FireWarriorsPlugin.ClaimedContainers.Contains(c)) continue;
                    if (c.gameObject == null || !c.gameObject.activeInHierarchy) continue;
                    if (!ContainerHasAnything(c)) continue;

                    Transform ct = c.cachedTransform != null ? c.cachedTransform : c.transform;
                    if (ct == null) continue;

                    float distFromFire = Vector3.Distance(ct.position, fireTransform.position);
                    if (distFromFire > radius) continue;

                    float distFromMe = Vector3.Distance(ct.position, transform.position);
                    if (distFromMe < bestContainerDist)
                    {
                        bestContainerDist = distFromMe;
                        bestContainer = c;
                    }
                }
            }

            AnimalController bestHorse = null;
            float bestHorseDist = float.MaxValue;

            if (FireWarriorsPlugin.CaptureHorses != null && FireWarriorsPlugin.CaptureHorses.Value)
            {
                var animals = AnimalController.AllAnimals;
                if (animals != null)
                {
                    for (int i = 0; i < animals.Count; i++)
                    {
                        AnimalController a = animals[i];
                        if (a == null) continue;
                        if (FireWarriorsPlugin.ClaimedAnimals.Contains(a)) continue;
                        if (IsPersonalHorse(a)) continue;
                        if (!a.IsAlive()) continue;
                        if (!IsCapturableHorse(a)) continue;

                        Transform at = a.cachedTransform != null ? a.cachedTransform : a.transform;
                        if (at == null || !a.gameObject.activeInHierarchy) continue;

                        float distFromFire = Vector3.Distance(at.position, fireTransform.position);
                        if (distFromFire > radius) continue;

                        float distFromMe = Vector3.Distance(at.position, transform.position);
                        if (distFromMe < bestHorseDist)
                        {
                            bestHorseDist = distFromMe;
                            bestHorse = a;
                        }
                    }
                }
            }

            WagonControllerEx bestWagon = null;
            float bestWagonDist = float.MaxValue;

            if (FireWarriorsPlugin.CaptureWagons != null && FireWarriorsPlugin.CaptureWagons.Value)
            {
                var wagons = FireWarriorsPlugin.GetKnownWagons();
                for (int i = 0; i < wagons.Count; i++)
                {
                    WagonControllerEx w = wagons[i];
                    if (w == null) continue;
                    if (FireWarriorsPlugin.ClaimedWagons.Contains(w)) continue;
                    if (!IsCapturableWagon(w)) continue;

                    Transform wt = w.cachedTransform != null ? w.cachedTransform : w.transform;
                    if (wt == null || !w.gameObject.activeInHierarchy) continue;

                    float distFromFire = Vector3.Distance(wt.position, fireTransform.position);
                    if (distFromFire > radius) continue;

                    float distFromMe = Vector3.Distance(wt.position, transform.position);
                    if (distFromMe < bestWagonDist)
                    {
                        bestWagonDist = distFromMe;
                        bestWagon = w;
                    }
                }
            }

            bool prioritizeHorses = FireWarriorsPlugin.PrioritizeHorses == null || FireWarriorsPlugin.PrioritizeHorses.Value;
            bool prioritizeWagons = FireWarriorsPlugin.PrioritizeWagons == null || FireWarriorsPlugin.PrioritizeWagons.Value;

            // Horses and wagons are both "big prizes" that shouldn't sit
            // waiting behind a pile of loose items. When both are prioritised
            // and both are in range, the nearer one wins.
            if (bestWagon != null && prioritizeWagons &&
                !(bestHorse != null && prioritizeHorses && bestHorseDist < bestWagonDist))
            {
                ClaimWagon(bestWagon);
            }
            else if (bestHorse != null && prioritizeHorses)
            {
                // Horses get first pick when this is enabled (default) - a
                // warrior heads straight for a spotted horse instead of
                // finishing whatever nearby item/container happens to be
                // closer first, so horses don't sit around waiting their
                // turn.
                currentAnimal = bestHorse;
                targetKind = TargetKind.Horse;
                FireWarriorsPlugin.ClaimedAnimals.Add(bestHorse);
                state = State.MovingToTarget;
                ResetStallTracking();
                Transform at = bestHorse.cachedTransform != null ? bestHorse.cachedTransform : bestHorse.transform;
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(at.position);
                }
            }
            else if (bestItem != null && (bestContainer == null || bestItemDist <= bestContainerDist) && (bestHorse == null || bestItemDist <= bestHorseDist) && (bestWagon == null || bestItemDist <= bestWagonDist))
            {
                currentItem = bestItem;
                targetKind = TargetKind.LooseItem;
                FireWarriorsPlugin.ClaimedItems.Add(bestItem);
                state = State.MovingToTarget;
                ResetStallTracking();
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(bestItem.cachedTransform.position);
                }
            }
            else if (bestContainer != null && (bestHorse == null || bestContainerDist <= bestHorseDist) && (bestWagon == null || bestContainerDist <= bestWagonDist))
            {
                currentContainer = bestContainer;
                targetKind = TargetKind.Container;
                FireWarriorsPlugin.ClaimedContainers.Add(bestContainer);
                state = State.MovingToTarget;
                ResetStallTracking();
                Transform ct = bestContainer.cachedTransform != null ? bestContainer.cachedTransform : bestContainer.transform;
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(ct.position);
                }
            }
            else if (bestHorse != null && (bestWagon == null || bestHorseDist <= bestWagonDist))
            {
                currentAnimal = bestHorse;
                targetKind = TargetKind.Horse;
                FireWarriorsPlugin.ClaimedAnimals.Add(bestHorse);
                state = State.MovingToTarget;
                ResetStallTracking();
                Transform at = bestHorse.cachedTransform != null ? bestHorse.cachedTransform : bestHorse.transform;
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(at.position);
                }
            }
            else if (bestWagon != null)
            {
                ClaimWagon(bestWagon);
            }
            else
            {
                // Nothing found at all. Once per session, dump global
                // counts for the three lists this search relies on
                // (ItemBase.AllItems / ItemContainer.AllContainers /
                // AnimalController.AllAnimals) - reported once: warriors
                // spawned fine (thanks to the permanent-template fix) but
                // then found nothing to loot/capture at all and walked
                // back within about a minute. Since those lists are each
                // populated the same OnEnable-registration way that turned
                // out to under-report distant/unloaded NPCs earlier, this
                // checks whether the loot itself simply wasn't loaded yet
                // from the fire's position - not a bug in the search logic.
                if (!FireWarriorsPlugin.LoggedEmptySeekDiagnostic)
                {
                    FireWarriorsPlugin.LoggedEmptySeekDiagnostic = true;
                    try
                    {
                        int itemsTotal = ItemBase.AllItems != null ? ItemBase.AllItems.Count : -1;
                        int containersTotal = ItemContainer.AllContainers != null ? ItemContainer.AllContainers.Count : -1;
                        int animalsTotal = AnimalController.AllAnimals != null ? AnimalController.AllAnimals.Count : -1;

                        int itemsNearby = 0, containersNearby = 0, animalsNearby = 0;
                        if (ItemBase.AllItems != null)
                            foreach (var it in ItemBase.AllItems)
                                if (it != null && it.cachedTransform != null && Vector3.Distance(it.cachedTransform.position, fireTransform.position) <= radius) itemsNearby++;
                        if (ItemContainer.AllContainers != null)
                            foreach (var c in ItemContainer.AllContainers)
                            {
                                if (c == null) continue;
                                Transform ct = c.cachedTransform != null ? c.cachedTransform : c.transform;
                                if (ct != null && Vector3.Distance(ct.position, fireTransform.position) <= radius) containersNearby++;
                            }
                        if (AnimalController.AllAnimals != null)
                            foreach (var a in AnimalController.AllAnimals)
                            {
                                if (a == null) continue;
                                Transform at = a.cachedTransform != null ? a.cachedTransform : a.transform;
                                if (at != null && Vector3.Distance(at.position, fireTransform.position) <= radius) animalsNearby++;
                            }

                        FireWarriorsPlugin.WriteDebug($"TickSeeking diagnostic: found NOTHING within {radius}m of the fire. Global totals - ItemBase.AllItems={itemsTotal} (within radius={itemsNearby}), ItemContainer.AllContainers={containersTotal} (within radius={containersNearby}), AnimalController.AllAnimals={animalsTotal} (within radius={animalsNearby}). If the 'within radius' numbers are 0 despite loot/animals being visible nearby in-game, the camp's contents likely weren't loaded yet from the fire's position (same distance-streaming behavior seen with NPCs earlier) - try planting the fire closer to the actual buildings/loot.");
                    }
                    catch (Exception seekDiagEx)
                    {
                        FireWarriorsPlugin.WriteDebug("TickSeeking diagnostic dump failed: " + seekDiagEx);
                    }
                }

                state = State.Returning;
            }
        }

        private static bool IsCapturableHorse(AnimalController a)
        {
            try
            {
                if (a.GetComponent<HorseAppearance>() != null) return true;
                if (a.GetComponentInChildren<HorseAppearance>() != null) return true;
            }
            catch
            {
                // fall through to the name-based fallback below
            }

            try
            {
                string n = SafeGetName(a);
                if (!string.IsNullOrEmpty(n) && n.IndexOf("horse", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch
            {
                // ignore, treat as not a horse
            }

            return false;
        }

        private static string SafeGetName(AnimalController a)
        {
            try { return a.GetName(); } catch { return a.name; }
        }

        private bool IsCapturableWagon(WagonControllerEx w)
        {
            try
            {
                // Note: WagonControllerEx.IsAlive() is a stub that always
                // returns true, so it tells us nothing - bWagonDead is the
                // field that actually tracks a wrecked wagon.
                if (w.bWagonDead) return false;

                // Being driven right now - by the player or by an NPC.
                // CanInteract() is the game's own "nobody is on this" check
                // (it's just Owner == null).
                if (!w.CanInteract()) return false;

                // Already on our side: don't have warriors spend the raid
                // shuffling our own (or an allied) camp's carts around. This
                // is the wagon equivalent of leaving your personal horse
                // alone. Compared by faction rather than just against the
                // home camp, so a second camp of yours isn't treated as a
                // target either. A summoned warrior is always assigned to
                // the player's faction, so its own faction is the one to
                // compare against.
                WorldGroup owner = FireWarriorsPlugin.GetWagonOwnerGroup(w);
                if (owner != null && owner.Faction != null)
                {
                    Entity me = GetComponent<Entity>();
                    if (me != null && me.Faction != null && owner.Faction.Name == me.Faction.Name) return false;
                }

                return true;
            }
            catch
            {
                // If we can't confidently tell, leave it alone.
                return false;
            }
        }

        private void ClaimWagon(WagonControllerEx wagon)
        {
            currentWagon = wagon;
            targetKind = TargetKind.Wagon;
            FireWarriorsPlugin.ClaimedWagons.Add(wagon);
            state = State.MovingToTarget;
            ResetStallTracking();
            Transform wt = wagon.cachedTransform != null ? wagon.cachedTransform : wagon.transform;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(wt.position);
            }
        }

        private static bool IsPersonalHorse(AnimalController a)
        {
            try
            {
                PersonalHorse mine = PersonalHorse.Instance;
                return mine != null && (object)mine == (object)a;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainerHasAnything(ItemContainer c)
        {
            var items = c.AvailableItems;
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].Item != null && items[i].Count > 0) return true;
            }
            return false;
        }

        // True once the NavMeshAgent itself has given up on reaching the
        // current destination (path fully invalid, not just still being
        // computed). Without this, a target that's technically unreachable
        // (e.g. behind geometry the warrior can't path around) would leave
        // the warrior standing there forever "moving to" it, since
        // remainingDistance never shrinks to ArriveDistance on an invalid
        // path. Checking this lets a warrior give up on THAT target and
        // pick a new one immediately instead of waiting for
        // WarriorMaxLifetime to force a full vanish.
        private bool DestinationUnreachable()
        {
            if (agent == null) return true;

            // If the agent never actually attached to the NavMesh (or its
            // async path request never resolves), it will NEVER move -
            // returning "not stuck" here (the old behavior) is backwards,
            // since nothing else would ever catch this and the warrior
            // would just sit in MovingToTarget for the full
            // WarriorMaxLifetime. Count this the same as a stall.
            if (!agent.isOnNavMesh || agent.pathPending)
            {
                stallTimer += Time.deltaTime;
                bool give1 = stallTimer >= StallTimeout;
                if (give1 && !FireWarriorsPlugin.LoggedStallDiagnostic)
                {
                    FireWarriorsPlugin.LoggedStallDiagnostic = true;
                    FireWarriorsPlugin.WriteDebug($"DestinationUnreachable: giving up because agent.isOnNavMesh={agent.isOnNavMesh}, pathPending={agent.pathPending} (agent never attached to the NavMesh or its path request never resolved).");
                }
                return give1;
            }

            if (agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;

            // A NavMeshAgent whose path is only PARTIAL (it computed the
            // closest reachable point instead of the real destination -
            // e.g. an item inside a building interior the navmesh doesn't
            // fully cover) is NOT reported as "invalid" by Unity, so the
            // check above misses it - this was confirmed to be exactly
            // what was happening: warriors sat in MovingToTarget for the
            // full 2-minute WarriorMaxLifetime without ever arriving.
            // Track whether remainingDistance is actually shrinking over
            // time; if it's been stuck (no real progress) for more than
            // StallTimeout seconds, treat the target as unreachable too.
            float rd = agent.remainingDistance;
            if (rd < lastRemainingDistance - 0.15f)
            {
                lastRemainingDistance = rd;
                stallTimer = 0f;
                return false;
            }

            stallTimer += Time.deltaTime;
            bool give2 = stallTimer >= StallTimeout;
            if (give2 && !FireWarriorsPlugin.LoggedStallDiagnostic)
            {
                FireWarriorsPlugin.LoggedStallDiagnostic = true;
                FireWarriorsPlugin.WriteDebug($"DestinationUnreachable: giving up on stall - remainingDistance stuck around {rd:0.00}m (pathStatus={agent.pathStatus}) for {StallTimeout}s without shrinking.");
            }
            return give2;
        }

        // Call this the moment a NEW target is chosen (TickSeeking), so a
        // stall measured against the PREVIOUS target doesn't immediately
        // condemn the new one.
        private void ResetStallTracking()
        {
            stallTimer = 0f;
            lastRemainingDistance = float.MaxValue;
        }

        private void TickMovingToTarget()
        {
            if (targetKind == TargetKind.LooseItem)
            {
                if (currentItem == null || !currentItem.gameObject.activeInHierarchy || currentItem.HasOwner() || DestinationUnreachable())
                {
                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }

                Vector3 targetPos = currentItem.cachedTransform.position;
                if (agent != null && agent.isOnNavMesh && !agent.pathPending)
                {
                    agent.SetDestination(targetPos);
                }

                float dist = Vector3.Distance(transform.position, targetPos);
                bool arrived = agent != null ? (!agent.pathPending && agent.remainingDistance <= ArriveDistance) : dist <= ArriveDistance;
                if (arrived || dist <= ArriveDistance)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                }
            }
            else if (targetKind == TargetKind.Container)
            {
                if (currentContainer == null || currentContainer.gameObject == null ||
                    !currentContainer.gameObject.activeInHierarchy || !ContainerHasAnything(currentContainer) || DestinationUnreachable())
                {
                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }

                Transform ct = currentContainer.cachedTransform != null ? currentContainer.cachedTransform : currentContainer.transform;
                Vector3 targetPos = ct.position;
                if (agent != null && agent.isOnNavMesh && !agent.pathPending)
                {
                    agent.SetDestination(targetPos);
                }

                float dist = Vector3.Distance(transform.position, targetPos);
                bool arrived = agent != null ? (!agent.pathPending && agent.remainingDistance <= ArriveDistance) : dist <= ArriveDistance;
                if (arrived || dist <= ArriveDistance)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                }
            }
            else if (targetKind == TargetKind.Horse)
            {
                if (currentAnimal == null || !currentAnimal.gameObject.activeInHierarchy ||
                    !currentAnimal.IsAlive() || IsPersonalHorse(currentAnimal))
                {
                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }

                Transform at = currentAnimal.cachedTransform != null ? currentAnimal.cachedTransform : currentAnimal.transform;
                Vector3 targetPos = at.position;

                // Capturing a horse is already a teleport, not a physical
                // grab, so a warrior doesn't need to stand right on top of
                // one - getting within HorseCaptureRadius in a straight
                // line is good enough. This matters because horses kept
                // in a stable/pen can sit on ground the NavMesh doesn't
                // cover at all, so a full "arrival" at the horse's exact
                // position may genuinely never be possible.
                float straightLineDist = Vector3.Distance(transform.position, targetPos);
                float captureRadius = FireWarriorsPlugin.HorseCaptureRadius != null ? FireWarriorsPlugin.HorseCaptureRadius.Value : 10f;
                if (straightLineDist <= captureRadius)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                    return;
                }

                Vector3 destPos = captureFallbackActive ? captureFallbackPos : targetPos;
                if (agent != null && agent.isOnNavMesh && !agent.pathPending)
                {
                    agent.SetDestination(destPos);
                }

                bool arrived = agent != null ? (!agent.pathPending && agent.remainingDistance <= ArriveDistance) : straightLineDist <= ArriveDistance;
                if (arrived)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                    return;
                }

                if (DestinationUnreachable())
                {
                    if (!captureFallbackActive && agent != null)
                    {
                        // The horse's exact spot may be unreachable outright
                        // (e.g. inside a stable the NavMesh doesn't cover) -
                        // before giving up on it entirely, try the closest
                        // point the NavMesh CAN actually route to nearby
                        // (just outside the pen, typically) and see if
                        // that's within capture range once reached.
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(targetPos, out hit, CaptureNavSampleRadius, NavMesh.AllAreas))
                        {
                            captureFallbackActive = true;
                            captureFallbackPos = hit.position;
                            ResetStallTracking();
                            FireWarriorsPlugin.WriteDebug($"Horse target unreachable directly - retrying toward the nearest NavMesh point {hit.position} instead (horse at {targetPos}, {Vector3.Distance(hit.position, targetPos):0.0}m away from it).");
                            return;
                        }
                    }

                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }
            }
            else if (targetKind == TargetKind.Wagon)
            {
                if (currentWagon == null || !currentWagon.gameObject.activeInHierarchy ||
                    !IsCapturableWagon(currentWagon))
                {
                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }

                // Same reasoning as the horse case above, only more so: a
                // wagon is a large object and its centre routinely sits on
                // ground the NavMesh doesn't cover (inside a fenced camp
                // yard, up against buildings), so requiring a warrior to
                // literally arrive at it would strand them. Getting within
                // WagonCaptureRadius in a straight line is enough.
                Transform wt = currentWagon.cachedTransform != null ? currentWagon.cachedTransform : currentWagon.transform;
                Vector3 targetPos = wt.position;

                float straightLineDist = Vector3.Distance(transform.position, targetPos);
                float captureRadius = FireWarriorsPlugin.WagonCaptureRadius != null ? FireWarriorsPlugin.WagonCaptureRadius.Value : 12f;
                if (straightLineDist <= captureRadius)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                    return;
                }

                Vector3 destPos = captureFallbackActive ? captureFallbackPos : targetPos;
                if (agent != null && agent.isOnNavMesh && !agent.pathPending)
                {
                    agent.SetDestination(destPos);
                }

                bool arrived = agent != null ? (!agent.pathPending && agent.remainingDistance <= ArriveDistance) : straightLineDist <= ArriveDistance;
                if (arrived)
                {
                    pickupTimer = 0f;
                    state = State.PickingUp;
                    if (agent != null) agent.ResetPath();
                    return;
                }

                if (DestinationUnreachable())
                {
                    if (!captureFallbackActive && agent != null)
                    {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(targetPos, out hit, CaptureNavSampleRadius, NavMesh.AllAreas))
                        {
                            captureFallbackActive = true;
                            captureFallbackPos = hit.position;
                            ResetStallTracking();
                            FireWarriorsPlugin.WriteDebug($"Wagon target unreachable directly - retrying toward the nearest NavMesh point {hit.position} instead (wagon at {targetPos}, {Vector3.Distance(hit.position, targetPos):0.0}m away from it).");
                            return;
                        }
                    }

                    ReleaseTarget();
                    state = State.SeekingItem;
                    return;
                }
            }
            else
            {
                state = State.SeekingItem;
            }
        }

        private void TickPickingUp()
        {
            if (targetKind == TargetKind.LooseItem)
            {
                if (currentItem == null)
                {
                    state = State.SeekingItem;
                    return;
                }

                pickupTimer += Time.deltaTime;
                if (pickupTimer < PickupDuration) return;

                try
                {
                    WorldGroup home = FireWarriorsPlugin.ResolveHomeCampGroup(GetComponent<Entity>());
                    ItemBase templateRef = currentItem.Prefab != null ? currentItem.Prefab : currentItem;
                    int stack = Math.Max(1, currentItem.Count);

                    if (home != null)
                    {
                        home.AddItem(templateRef, stack);
                    }
                    else
                    {
                        FireWarriorsPlugin.Log.LogWarning("No home camp found - could not deliver an item, it will just be removed.");
                    }

                    currentItem.Destroy(0f);
                }
                catch (Exception e)
                {
                    FireWarriorsPlugin.Log.LogWarning("Pickup/delivery failed for an item: " + e);
                }

                ReleaseTarget();
                state = State.SeekingItem;
            }
            else if (targetKind == TargetKind.Container)
            {
                if (currentContainer == null)
                {
                    state = State.SeekingItem;
                    return;
                }

                pickupTimer += Time.deltaTime;
                if (pickupTimer < ContainerEmptyDuration) return;

                try
                {
                    WorldGroup home = FireWarriorsPlugin.ResolveHomeCampGroup(GetComponent<Entity>());
                    if (home == null)
                    {
                        FireWarriorsPlugin.Log.LogWarning("No home camp found - leaving a container full and will retry later.");
                    }
                    else
                    {
                        var items = currentContainer.AvailableItems;
                        if (items != null)
                        {
                            int delivered = 0;
                            for (int i = 0; i < items.Count; i++)
                            {
                                var entry = items[i];
                                if (entry == null || entry.Item == null || entry.Count <= 0) continue;
                                home.AddItem(entry.Item, entry.Count);
                                delivered++;
                            }
                            items.Clear();
                            FireWarriorsPlugin.Log.LogInfo($"Emptied a container ({delivered} item stack(s) delivered to the home camp).");
                        }
                    }
                }
                catch (Exception e)
                {
                    FireWarriorsPlugin.Log.LogWarning("Emptying a container failed: " + e);
                }

                ReleaseTarget();
                state = State.SeekingItem;
            }
            else if (targetKind == TargetKind.Horse)
            {
                if (currentAnimal == null)
                {
                    state = State.SeekingItem;
                    return;
                }

                pickupTimer += Time.deltaTime;
                if (pickupTimer < HorseCaptureDuration) return;

                try
                {
                    WorldGroup home = FireWarriorsPlugin.ResolveHomeCampGroup(GetComponent<Entity>());
                    if (home == null)
                    {
                        FireWarriorsPlugin.Log.LogWarning("No home camp found - could not deliver a captured horse, leaving it where it is.");
                    }
                    else
                    {
                        Vector3 basePos = home.cachedTransform != null ? home.cachedTransform.position : home.Center;
                        Vector2 offset2d = UnityEngine.Random.insideUnitCircle * 5f;
                        Vector3 destPos = basePos + new Vector3(offset2d.x, 0f, offset2d.y);

                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(destPos, out hit, 15f, NavMesh.AllAreas))
                        {
                            destPos = hit.position;
                        }

                        NavMeshAgent horseAgent = currentAnimal.cachedAgent;
                        if (horseAgent != null && horseAgent.isActiveAndEnabled)
                        {
                            horseAgent.Warp(destPos);
                        }
                        else
                        {
                            Transform at = currentAnimal.cachedTransform != null ? currentAnimal.cachedTransform : currentAnimal.transform;
                            at.position = destPos;
                        }

                        if (FireWarriorsPlugin.TameCapturedHorses != null && FireWarriorsPlugin.TameCapturedHorses.Value)
                        {
                            currentAnimal.Tamed = true;
                        }

                        FireWarriorsPlugin.Log.LogInfo("Captured a horse and delivered it to the home camp.");
                        FireWarriorsPlugin.WriteDebug($"Captured horse '{SafeGetName(currentAnimal)}', teleported to {destPos}.");
                    }
                }
                catch (Exception e)
                {
                    FireWarriorsPlugin.Log.LogWarning("Capturing/delivering a horse failed: " + e);
                }

                ReleaseTarget();
                state = State.SeekingItem;
            }
            else if (targetKind == TargetKind.Wagon)
            {
                if (currentWagon == null)
                {
                    state = State.SeekingItem;
                    return;
                }

                pickupTimer += Time.deltaTime;
                if (pickupTimer < WagonCaptureDuration) return;

                try
                {
                    WorldGroup home = FireWarriorsPlugin.ResolveHomeCampGroup(GetComponent<Entity>());
                    if (home == null)
                    {
                        FireWarriorsPlugin.Log.LogWarning("No home camp found - could not deliver a captured wagon, leaving it where it is.");
                    }
                    else
                    {
                        Vector3 basePos = home.cachedTransform != null ? home.cachedTransform.position : home.Center;

                        // Parked further out than a horse would be: a wagon is
                        // long and wide, and dropping one into the middle of a
                        // camp risks landing it on top of tents or people.
                        Vector2 offset2d = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(10f, 16f);
                        Vector3 destPos = basePos + new Vector3(offset2d.x, 0f, offset2d.y);

                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(destPos, out hit, 25f, NavMesh.AllAreas))
                        {
                            destPos = hit.position;
                        }

                        Transform wt = currentWagon.cachedTransform != null ? currentWagon.cachedTransform : currentWagon.transform;

                        // A wagon is a physics body on wheel colliders. Moving
                        // its transform without settling the Rigidbody first
                        // leaves it carrying whatever velocity/spin it had, so
                        // it can launch itself or roll away on arrival.
                        Rigidbody rb = currentWagon.RB;
                        if (rb != null)
                        {
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }

                        wt.position = destPos;
                        // Land it upright and flat, keeping only its heading -
                        // it may have been captured on a slope.
                        wt.rotation = Quaternion.Euler(0f, wt.eulerAngles.y, 0f);
                        if (rb != null) rb.position = destPos;

                        // The game's own notion of which camp a wagon belongs
                        // to (WagonControllerEx.GetWorldGroup() is just a cast
                        // of this field), so this is what actually makes it
                        // yours rather than merely parked nearby.
                        currentWagon.RequestedBy = home;

                        FireWarriorsPlugin.Log.LogInfo("Captured a wagon and delivered it to the home camp.");
                        FireWarriorsPlugin.WriteDebug($"Captured wagon '{currentWagon.name}', teleported to {destPos} and assigned to the home camp.");
                    }
                }
                catch (Exception e)
                {
                    FireWarriorsPlugin.Log.LogWarning("Capturing/delivering a wagon failed: " + e);
                    FireWarriorsPlugin.WriteDebug("Capturing/delivering a wagon failed: " + e);
                }

                ReleaseTarget();
                state = State.SeekingItem;
            }
            else
            {
                state = State.SeekingItem;
            }
        }

        private void TickReturning()
        {
            if (agent != null && agent.isOnNavMesh && !agent.hasPath && !agent.pathPending)
            {
                agent.SetDestination(fireTransform.position);
            }

            if (DestinationUnreachable())
            {
                // Can't path back to the fire at all (e.g. the way is
                // blocked) - vanish right where it is rather than getting
                // stuck forever, which would otherwise also stop the fire's
                // session from ever finishing (so the camp would never
                // burn either).
                FireWarriorsPlugin.WriteDebug("TickReturning: path back to the fire is invalid - vanishing in place instead of getting stuck.");
                StartCoroutine(SinkIntoFireAndDespawn());
                return;
            }

            float dist = Vector3.Distance(transform.position, fireTransform.position);
            bool arrived = agent != null ? (!agent.pathPending && agent.remainingDistance <= ReturnArriveDistance) : dist <= ReturnArriveDistance;
            if (arrived || dist <= ReturnArriveDistance)
            {
                StartCoroutine(SinkIntoFireAndDespawn());
            }
        }

        private System.Collections.IEnumerator SinkIntoFireAndDespawn()
        {
            state = State.Vanishing;
            if (agent != null) agent.ResetPath();

            Vector3 startScale = transform.localScale;
            float t = 0f;
            const float duration = 0.5f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / duration);
                transform.localScale = startScale * k;
                yield return null;
            }
            Destroy(gameObject);
        }

        public void ForceVanishImmediate()
        {
            ReleaseTarget();
            Destroy(gameObject);
        }

        private void ReleaseTarget()
        {
            if (currentItem != null)
            {
                FireWarriorsPlugin.ClaimedItems.Remove(currentItem);
            }
            if (currentContainer != null)
            {
                FireWarriorsPlugin.ClaimedContainers.Remove(currentContainer);
            }
            if (currentAnimal != null)
            {
                FireWarriorsPlugin.ClaimedAnimals.Remove(currentAnimal);
            }
            if (currentWagon != null)
            {
                FireWarriorsPlugin.ClaimedWagons.Remove(currentWagon);
            }
            currentItem = null;
            currentContainer = null;
            currentAnimal = null;
            currentWagon = null;
            targetKind = TargetKind.None;
            captureFallbackActive = false;
        }

        private void OnDestroy()
        {
            ReleaseTarget();
        }
    }
}
