using HarmonyLib;
using IdleSlayerMods.Common.Extensions;
using Il2Cpp;
using MelonLoader;
using MelonLoader.TinyJSON;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
//using AutoAscendMod;
using System.Linq;
using System.Reflection;
using Il2CppSystem.IO;

namespace ArmoryManager
{

    public class BreakWeapons : MonoBehaviour
    {
        public static BreakWeapons Instance { get; private set; }

        WeaponsManager _weaponsManager;
        PlayerInventory _playerInventory;
        DropsManager _dropsManager;
        Coroutine _runningFor30Seconds;
        Coroutine _breaker;

        static readonly Divinity x2 = Divinities.list.DuplicateNextPick;
        static readonly Divinity x2x2 = Divinities.list.x2x2;
        static readonly AscensionSkill ChestInAChest = AscensionSkills.list.ChestInAChest;

        static Color _initialSlotColor;

        int _keep;
        static int minimumBreak;
        int oldMinimumBreak;

        bool lastIsRunner;
        static bool triggerBreakCheck;
        static bool AscendCalled = false;
        readonly HashSet<uint> _loggedKeptUids = new();

        public void Awake()
        {
            Instance = this;

            _weaponsManager = WeaponsManager.instance;
            _playerInventory = PlayerInventory.instance;
            _dropsManager = DropsManager.instance;
            _keep = Plugin.Config.CurrentNumberOfWeaponsToKeep.Value;

            minimumBreak = SetMinWeaponBreak();
        }

        bool _initialized;
        float _nextInitAttempt;

        public void Start() => TryInitialize();

        // After a cloud-save reload the armory slots may not exist yet when
        // Start runs (NullReferenceException on 2026-10-31); LateUpdate retries.
        private void TryInitialize()
        {
            if (_weaponsManager == null) _weaponsManager = WeaponsManager.instance;
            if (_playerInventory == null) _playerInventory = PlayerInventory.instance;
            if (_dropsManager == null) _dropsManager = DropsManager.instance;
            var slots = _weaponsManager != null ? _weaponsManager.allSlots : null;
            if (slots == null || slots.Length == 0 || slots[0] == null) return;
            var slotImage = slots[0].GetComponent<Image>();
            if (slotImage == null) return;
            _initialSlotColor = slotImage.color;
            _initialized = true;

            if (Plugin.Config.LowerBoundOfWeapons.Value > _weaponsManager.GetMaxSlots())
                Plugin.Config.LowerBoundOfWeapons.Value = (int)_weaponsManager.GetMaxSlots() - 10;

            if (Plugin.Config.RequirementPerLevel.Value < 15)
                Plugin.Config.RequirementPerLevel.Value = 15;
            else if (Plugin.Config.RequirementPerLevel.Value > 50)
                Plugin.Config.RequirementPerLevel.Value = 50;

            SetWeaponLevels();

            Plugin.ModHelperInstance.CreateSettingsToggle("Break Low Level Weapons", Plugin.Config.BreakLowLevelEnabled,
                _ => {
                    Plugin.Config.BreakLowLevelEnabled.SaveEntry();
                    triggerBreakCheck = true;
                });

            Plugin.ModHelperInstance.CreateSettingsToggle("Break Dupe Weapons", Plugin.Config.BreakDupesEnabled,
                _ => {
                    Plugin.Config.BreakDupesEnabled.SaveEntry();
                    triggerBreakCheck = true;
                });

            Plugin.ModHelperInstance.CreateSettingsToggle("Break Weapons Enabled", Plugin.Config.BreakWeaponsEnabled,
                _ => {
                    Plugin.Config.BreakWeaponsEnabled.SaveEntry();
                    HighlightSlots();
                    triggerBreakCheck = true;
                });

        }

        public static int SetMinWeaponBreak()
        {
            if (!ChestInAChest.unlocked)
                return 1;

            if (x2x2.unlocked)
                return 4;

            if (x2.unlocked)
                return 2;

            return 1;
        }

        private void SetWeaponLevels()
        { 
            var keyWeapons = _weaponsManager.currentKeyItemsByUid;
            var weapons = _weaponsManager.currentItemsByUid;

            foreach (var weapon in weapons.Values)
                SetWeaponLevel(weapon);

            foreach (var weapon in keyWeapons.Values)
                SetWeaponLevel(weapon);
        }
        
        private static void SetWeaponLevel(WeaponItemInventory weapon)
        {
            if (weapon != null && weapon.item != null)
                weapon.item.levelRequirementIncreasePerLevel = Plugin.Config.RequirementPerLevel.Value;
        }

        private IEnumerator ResetBreakerDuringRunner()
        {
            yield return new WaitForSeconds(30f);

            if (triggerBreakCheck)
            {
//                Plugin.Logger.Msg("Resetting Breaker during Runner due to triggerBreakCheck.");
                triggerBreakCheck = false;
            }
            if (_breaker != null)
            {
//                Plugin.Logger.Msg("Stopping Breaker Coroutine after 30 seconds.");
                MelonCoroutines.Stop(_breaker);
                _breaker = null;
            }
        }

        public void LateUpdate()
        {
            if (!_initialized)
            {
                if (Time.unscaledTime < _nextInitAttempt) return;
                _nextInitAttempt = Time.unscaledTime + 1f;
                TryInitialize();
                if (!_initialized) return;
                Plugin.Logger.Msg("[Armory] Initialised after a delayed start.");
            }

            if (_playerInventory.armoryItemAscendingHeightsChance < 100)
                _playerInventory.armoryItemAscendingHeightsChance = 100;
            if (_playerInventory.increaseBonusStageArmoryChestsChance < 100)
                _playerInventory.increaseBonusStageArmoryChestsChance = 100;

            if ((AscendCalled) && _breaker != null)
            {
                MelonCoroutines.Stop(_breaker);
                _breaker = null;
            }

            if (GameState.IsRunner() && (triggerBreakCheck || !lastIsRunner) && !AscendCalled)
            {
                _breaker ??= (Coroutine)MelonCoroutines.Start(RunBreaker());

                _runningFor30Seconds = (Coroutine)MelonCoroutines.Start(ResetBreakerDuringRunner());
            }

            lastIsRunner = GameState.IsRunner();


            if (!GameState.IsRunner() && _runningFor30Seconds != null)
            {
                MelonCoroutines.Stop(_runningFor30Seconds);
                _runningFor30Seconds = null;
            }

            double before = _keep;

            int maxSlots = (int)_weaponsManager.GetMaxSlots();
            int lowerBound = Plugin.Config.LowerBoundOfWeapons.Value;
            int newMaxKeep = maxSlots - minimumBreak;      // upper clamp
            int oldMaxKeep = maxSlots - oldMinimumBreak;   // previous upper

            if (oldMinimumBreak != minimumBreak
                && (_keep > newMaxKeep || (_keep == oldMaxKeep && newMaxKeep > oldMaxKeep)))
            {
                _keep = newMaxKeep;
            }

            if (Input.GetKeyUp(Plugin.Config.DecreaseWeaponsToKeep.Value))
                _keep = Math.Max(lowerBound, _keep - 1);

            if (Input.GetKeyUp(Plugin.Config.IncreaseWeaponsToKeep.Value))
                _keep = Math.Min(newMaxKeep, _keep + 1);

            if (before != _keep)
            {
                Plugin.Config.CurrentNumberOfWeaponsToKeep.Value = _keep;
                HighlightSlots();
                triggerBreakCheck = true;
            }

            oldMinimumBreak = minimumBreak;
        }

        private IEnumerator RunBreaker()
        {
            triggerBreakCheck = false;

            if (Plugin.Config.BreakWeaponsEnabled.Value &&
                (Plugin.Config.BreakDupesEnabled.Value || Plugin.Config.BreakLowLevelEnabled.Value))
            {
                yield return MelonCoroutines.Start(BreakLevelThreeAndLowerAndDupes());
            }

            yield return MelonCoroutines.Start(BreakAllExcessWeaponsFromSlots());

            _breaker = null;
        }

        public void HighlightSlots()
        {
            var slots = _weaponsManager.allSlots;

            for (int i = 0; i < slots.Count; i++)
            {
                var img = slots[i].GetComponent<Image>();
                if (img != null)
                {
                    if (i < _keep)
                        img.color = _initialSlotColor; // reset to initial color for slots we want to keep
                    else if (!Plugin.Config.BreakWeaponsEnabled.Value && i < (int)_weaponsManager.GetMaxSlots() - minimumBreak)
                        img.color = new Color(1f, 0.3f, 0.3f, 0.75f * _initialSlotColor.a); // red for slots we want to break
                    else
                        img.color = new Color(0.6f, 0f, 0f, _initialSlotColor.a); // red for slots we want to break
                }
            }
        }
        IEnumerator BreakAllExcessWeaponsFromSlots()
        {
            var slots = _weaponsManager.allSlots;

            for (int i = slots.Count - 1; i >= _keep; i--)
            {
                if (i < slots.Count - minimumBreak && !Plugin.Config.BreakWeaponsEnabled.Value) break; // only break excess weapons if enabled

                var slot = slots[i];
                if (slot == null) continue;

                // grab the ItemObject if there is one anywhere under this slot
                var io = slot.GetComponentInChildren<ItemObject>();

                // Keep-count slots spare unique weapons; the game's
                // always-break slots must still be cleared.
                bool alwaysBreakSlot = i >= slots.Count - minimumBreak;
                if (io != null && io.currentItem != null && !alwaysBreakSlot &&
                    Plugin.Config.KeepBestUniqueWeapons.Value)
                {
                    var cover = FindCoveringWeapon(io.currentItem);
                    if (cover == null)
                    {
                        if (_loggedKeptUids.Add(io.currentItem.uid))
                            Plugin.Logger.Msg($"[Armory] Kept beyond keep count {_keep} (slot {i + 1}): unique {Describe(io.currentItem)}");
                        continue;
                    }
                    Plugin.Logger.Msg($"[Armory] Broke (slot {i + 1}, beyond keep count {_keep}): {Describe(io.currentItem)}; covered by {Describe(cover)}");
                }
                else if (io != null && io.currentItem != null)
                {
                    Plugin.Logger.Msg(alwaysBreakSlot
                        ? $"[Armory] Broke (slot {i + 1}, game always-break slot): {Describe(io.currentItem)}"
                        : $"[Armory] Broke (slot {i + 1}, beyond keep count {_keep}): {Describe(io.currentItem)}");
                }

                if (io != null && io.currentItem != null)
                {
                    ProcessBreakItem(io.currentItem);
                    yield return new WaitUntil(new System.Func<bool>(() =>
                    {
                        // if the slot itself is gone, treat it as “empty” and break out
                        if (slot == null)
                            return true;

                        // now it’s safe to ask for childCount
                        return slot.transform.childCount == 0;
                    }));

                    triggerBreakCheck = true;
                    break;
                }
            }
        }

        IEnumerator BreakLevelThreeAndLowerAndDupes()
        {
            var slots = _weaponsManager.allSlots;

            for (int i = slots.Length - 1; i >= 0; i--)
            {

                var slot = slots[i];
                if (slot == null) continue;

                var io = slot.GetComponentInChildren<ItemObject>();
                if (io == null) continue;

                var toBreak = io.currentItem;

                bool shouldBreak = false;

                if (toBreak.currentLevel > 4) continue; // skip items above level 4

                if (Plugin.Config.KeepBestUniqueWeapons.Value)
                {
                    var cover = FindCoveringWeapon(toBreak);
                    shouldBreak = cover != null &&
                                  (Plugin.Config.BreakDupesEnabled.Value ||
                                   (Plugin.Config.BreakLowLevelEnabled.Value && toBreak.currentLevel <= 3));
                    if (shouldBreak)
                        Plugin.Logger.Msg($"[Armory] Broke (slot {i + 1}): {Describe(toBreak)}; covered by {Describe(cover)}");
                    else if (cover == null && toBreak.currentLevel <= 3 &&
                             Plugin.Config.BreakLowLevelEnabled.Value &&
                             _loggedKeptUids.Add(toBreak.uid))
                        Plugin.Logger.Msg($"[Armory] Kept low level (slot {i + 1}): unique {Describe(toBreak)}");
                }
                // 1) Break all level-3 and lower
                else if (toBreak.currentLevel <= 3 && Plugin.Config.BreakLowLevelEnabled.Value)
                    shouldBreak = true;

                else if (Plugin.Config.BreakDupesEnabled.Value)
                {

                    // 2) Break true dupes (same name + same-or-better skills)
                    var currentItems = _weaponsManager.CurrentItems().ToArray();

                    foreach (var compare in currentItems)
                    {
                        bool identical = true;

                        if (compare.slot == toBreak.slot)
                            continue;

                        if (compare.item.localizedName != toBreak.item.localizedName)
                            continue;

                        if (!compare.isExcellent && toBreak.isExcellent)
                            continue;

                        bool allOptionsMatch = true;

                        if (compare.currentOptions.Count < toBreak.currentOptions.Count) continue;

                        else if (compare.currentOptions.Count >= toBreak.currentOptions.Count)
                        {
                            if (compare.currentOptions.Count > toBreak.currentOptions.Count) identical = false;

                            // check every option in toBreak is in compare
                            foreach (var option in toBreak.currentOptions)
                            {
                                if (!compare.currentOptions.Contains(option))
                                {
                                    allOptionsMatch = false;
                                    break;
                                }
                            }

                            if (!allOptionsMatch) continue;
                        }

                        if (compare.currentSkills.Count >= toBreak.currentSkills.Count)
                        {
                            if (compare.currentSkills.Count > toBreak.currentSkills.Count) identical = false;

                            // check every skill in toBreak is in compare
                            bool allMatch = true;
                            foreach (var skill in toBreak.currentSkills)
                            {
                                if (!compare.currentSkills.Contains(skill))
                                {
                                    allMatch = false;
                                    break;
                                }
                            }
                            if (allMatch)
                                shouldBreak = true;
                        }
                        else if (toBreak.currentSkills.Count == 0)
                            shouldBreak = true;

                        if (shouldBreak)
                        {
                            if (identical && compare.currentLevel < toBreak.currentLevel)
                                shouldBreak = false;

                            break;
                        }
                    }
                }

                if (shouldBreak)
                {
                    if (!Plugin.Config.KeepBestUniqueWeapons.Value)
                        Plugin.Logger.Msg($"[Armory] Broke (slot {i + 1}, low level or duplicate): {Describe(toBreak)}");
                    ProcessBreakItem(toBreak);

                    // wait for the ItemObject to be removed from this slot
                    yield return new WaitUntil(new System.Func<bool>(() =>
                    {
                        // if the slot itself is gone, treat it as “empty” and break out
                        if (slot == null)
                            return true;

                        // now it’s safe to ask for childCount
                        return slot.transform.childCount == 0;
                    }));

                    triggerBreakCheck = true;
                    break;
                }
            }
        }
        // True when another weapon of the same equipment does everything this
        // one does: same or better excellence, every option and skill, and the
        // same or a higher level. Exact copies at the same level keep one.
        private WeaponItemInventory FindCoveringWeapon(WeaponItemInventory weapon)
        {
            foreach (var other in _weaponsManager.CurrentItems().ToArray())
            {
                if (other == null || other.uid == weapon.uid) continue;
                if (other.item.name != weapon.item.name) continue;
                if (weapon.isExcellent && !other.isExcellent) continue;
                if (other.currentLevel < weapon.currentLevel) continue;
                if (!ContainsAll(other.currentOptions, weapon.currentOptions)) continue;
                if (!ContainsAll(other.currentSkills, weapon.currentSkills)) continue;

                bool sameConfig = other.isExcellent == weapon.isExcellent &&
                                  other.currentOptions.Count == weapon.currentOptions.Count &&
                                  other.currentSkills.Count == weapon.currentSkills.Count;
                if (!sameConfig || other.currentLevel > weapon.currentLevel || other.uid < weapon.uid)
                    return other;
            }
            return null;
        }

        private static string Describe(WeaponItemInventory weapon)
        {
            var options = new List<string>();
            foreach (var option in weapon.currentOptions)
                options.Add(option.benefit != null ? $"{option.benefit.name} {option.value:0.##}" : "?");
            var skills = new List<string>();
            foreach (var skill in weapon.currentSkills)
                skills.Add(skill != null ? skill.name : "?");
            string equipment = weapon.item.forEquipment != null ? weapon.item.forEquipment.name : "?";
            return $"{weapon.item.name} [{equipment}] +{weapon.currentLevel}" +
                   (weapon.isExcellent ? " excellent" : string.Empty) +
                   $"; options=[{string.Join(", ", options)}]; skills=[{string.Join(", ", skills)}]";
        }

        private static bool ContainsAll<T>(
            Il2CppSystem.Collections.Generic.List<T> container,
            Il2CppSystem.Collections.Generic.List<T> required)
        {
            foreach (var entry in required)
                if (!container.Contains(entry)) return false;
            return true;
        }

        private void ProcessBreakItem(WeaponItemInventory currentItem)
        {
            // 1) Calculate refunds
            int dpRefund = currentItem.CalculateDivinityPointsRefund();
            int scrapValue = currentItem.GetScrapValue();
            var rewards = new List<string>();

            // 2) Refund Divinity Points
            if (dpRefund > 0)
            {
                _playerInventory.divinityPoints += dpRefund;
                rewards.Add(string.Format("+{0:N0}<sprite name=\"dp\">", dpRefund));
            }

            // 3) Spawn Scrap Drops
            if (scrapValue > 0)
            {
                DropsManager.instance.AddDrop(scrapValue, Drops.list.Scrap);
                rewards.Add(string.Format("+{0:N0}<sprite name=\"drop_scrap\">", scrapValue));
            }

            // 4) Show Notification via ModHelper
            if (rewards.Count > 0)
            {
                Plugin.ModHelperInstance.ShowNotification(
                    string.Join("<br>", rewards),
                    /* shine = */ true
                );
            }

            // 5) Persist Divinity Points
            SaveManager.SetString(
                "Divinity Points",
                _playerInventory.divinityPoints.ToString()
            );

            // 7) Remove weapon & save
            _weaponsManager.currentItemsByUid.Remove(currentItem.uid);
            _weaponsManager.SaveWeapons();
            _playerInventory.CalculateValues();

            // 8) Refresh the UI list
            _weaponsManager.RefreshList();

            // 9) Track broken‐weapon count & achievements
            SaveManager.SetString(
                "Weapons Broken",
                _playerInventory.weaponsBroken.ToString()
            );

            AudioManager.instance.Play("Item Broke");
        }

        [HarmonyPatch(typeof(WeaponsManager), "RefreshList")]
        public class RefreshPopupPatch
        {
            public static void Postfix()
            {
                BreakWeapons.Instance?.HighlightSlots();
            }
        }

        [HarmonyPatch(typeof(ArmoryItemChest), "OnTriggerEnter2D")]
        public class ArmoryItemChestPatch
        {
            public static void Postfix()
            {
                triggerBreakCheck = true;
            }
        }

        [HarmonyPatch(typeof(ItemObject), "OnEndDrag")]
        public class ItemObjectPatch
        {
            public static void Postfix()
            {
                triggerBreakCheck = true;
            }
        }

        [HarmonyPatch(typeof(WeaponsManager))]
        [HarmonyPatch(nameof(WeaponsManager.GenerateRandomWeapon), [typeof(WeaponItem), typeof(bool)])]
        static class Patch_GenerateRandomWeapon_Array
        {
            // runs _after_ the array overload
            static void Postfix(WeaponItemInventory __result)
            {
                // your logic here, __result is the generated inventory item
                SetWeaponLevel(__result);
            }
        }

        [HarmonyPatch(typeof(DivinitiesManager), nameof(DivinitiesManager.ActivateDivinity))]
        static class Patch_ActivateDivinity
        {
            static void Postfix(string id)
            {
                if (x2.id == id || x2x2.id == id)
                {
                    minimumBreak = SetMinWeaponBreak();
                    triggerBreakCheck = true;
                }
            }
        }

        // Patch DeactivateDivinity → fires immediately after .unlocked has been set to false
        [HarmonyPatch(typeof(DivinitiesManager), nameof(DivinitiesManager.DeactivateDivinity))]
        static class Patch_DeactivateDivinity
        {
            static void Postfix(string id)
            {
                if (x2x2.id == id)
                {
                    minimumBreak = SetMinWeaponBreak();
                    triggerBreakCheck = true;
                }
            }
        }

        // Patch ResetDivinities → runs after everything is reset, so read the current .unlocked
        [HarmonyPatch(typeof(DivinitiesManager), nameof(DivinitiesManager.ResetDivinities))]
        static class Patch_ResetDivinities
        {
            static void Postfix()
            {
                minimumBreak = SetMinWeaponBreak();
                triggerBreakCheck = true;
            }
        }

/*        [HarmonyPatch(typeof(AutoAscend), "DelayAndAscend")]
        static class Patch_DelayAndAscend
        {
            static void Postfix()
            {
                Plugin.Logger.Msg("AutoAscend: DelayAndAscend called");
                AscendCalled = true;
            }
        }
*/
        [HarmonyPatch(typeof(AscensionManager), "Ascend")]
        static class Patch_Ascend
        {
            static void Postfix()
            {
//                Plugin.Logger.Msg("AscensionManager: Ascend called");
                if (ResetAscendCoroutine == null)
                    ResetAscendCoroutine = (Coroutine)MelonCoroutines.Start(ResetAscendCalled());
            }
        }

        static Coroutine ResetAscendCoroutine;
        
        private static IEnumerator ResetAscendCalled()
        {
            yield return new WaitForSeconds(5f);
//            Plugin.Logger.Msg("Resetting AscendCalled after 5 seconds.");
            AscendCalled = false;
            triggerBreakCheck = true;
            ResetAscendCoroutine = null;
        }
    }
}
