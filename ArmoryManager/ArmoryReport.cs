using System.Text.Encodings.Web;
using System.Text.Json;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;

namespace ArmoryManager;

// Read-only: writes the armory to a JSON file so builds can be studied outside
// the game. It only calls the getters the game's own tooltips use.
internal static class ArmoryReport
{
    const float FirstReportDelaySeconds = 30f;

    static float _firstSeenAt = -1f;
    static bool _writtenThisLaunch;
    static int _errors;

    internal static void Update(WeaponsManager manager)
    {
        if (_firstSeenAt < 0f) _firstSeenAt = Time.unscaledTime;

        bool firstReportDue = !_writtenThisLaunch &&
            Time.unscaledTime - _firstSeenAt >= FirstReportDelaySeconds;
        if (!firstReportDue && !Input.GetKeyDown(Plugin.Config.ArmoryReportKey.Value))
            return;

        _writtenThisLaunch = true;
        try
        {
            Write(manager);
        }
        catch (Exception e)
        {
            Plugin.Logger.Warning($"[Armory] Report failed: {e.Message}");
        }
    }

    static void Write(WeaponsManager manager)
    {
        _errors = 0;
        var owned = Weapons(manager, manager.currentItemsByUid);
        var keyItems = Weapons(manager, manager.currentKeyItemsByUid);
        var catalogue = Catalogue();
        var report = new Dictionary<string, object>
        {
            ["writtenAt"] = DateTime.Now.ToString("s"),
            ["gameVersion"] = Application.version,
            ["requirementPerLevelSetting"] = Plugin.Config.RequirementPerLevel.Value,
            ["manager"] = new Dictionary<string, object>
            {
                ["excellentChance"] = manager.excellentChance,
                ["optionChance"] = manager.optionChance,
                ["skillChance"] = manager.skillChance,
                ["minRandomLevel"] = manager.minRandomlevel,
                ["maxRandomLevel"] = manager.maxRandomLevel,
                ["maxSlots"] = Try(() => manager.GetMaxSlots()),
                ["divinitiesUpgradeCost"] = manager.divinitiesUpgradeCost,
                ["divinitiesCostIncrementPerLevel"] = manager.divinitiesCostIncrementPerLevel,
                ["isAutoLoadoutActive"] = manager.isAutoLoadoutActive,
            },
            ["owned"] = owned,
            ["keyItems"] = keyItems,
            ["loadouts"] = Loadouts(),
            ["catalogue"] = catalogue,
        };

        string folder = Path.Combine(MelonEnvironment.UserDataDirectory, "ArmoryReport");
        Directory.CreateDirectory(folder);
        string fileName = $"armory-report-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        File.WriteAllText(Path.Combine(folder, fileName), JsonSerializer.Serialize(report, options));
        Plugin.Logger.Msg(
            $"[Armory] Report written: {fileName}; owned={owned.Count}, key={keyItems.Count}, " +
            $"catalogue={catalogue.Count}, read errors={_errors}");
    }

    static List<object> Weapons(
        WeaponsManager manager,
        Il2CppSystem.Collections.Generic.Dictionary<uint, WeaponItemInventory> items)
    {
        var result = new List<object>();
        if (items == null) return result;
        foreach (var pair in items)
        {
            if (pair.Value != null)
                result.Add(Weapon(manager, pair.Value));
        }
        return result;
    }

    static object Weapon(WeaponsManager manager, WeaponItemInventory weapon)
    {
        int level = weapon.currentLevel;
        WeaponItem item = weapon.item;
        Power equipment = item != null ? item.forEquipment : null;
        return new Dictionary<string, object>
        {
            ["uid"] = weapon.uid,
            ["item"] = item != null ? item.name : null,
            ["displayName"] = Try(() => weapon.GetName(false, false)),
            ["equipment"] = equipment != null ? equipment.name : null,
            ["equipmentDisplayName"] = equipment != null ? equipment.localizedName : null,
            ["equipmentLevel"] = equipment != null ? equipment.level : null,
            ["level"] = level,
            ["excellent"] = weapon.isExcellent,
            ["slot"] = weapon.slot,
            ["equipped"] = Try(() => weapon.IsEquipped()),
            ["inLoadout"] = Try(() => manager.IsItemInAnyLoadout(weapon.uid)),
            ["isKeyItem"] = Try(() => manager.IsKey(weapon)),
            ["requirement"] = Try(() => weapon.GetLevelRequirement(level)),
            ["requirementNextLevel"] = Try(() => weapon.GetLevelRequirement(level + 1)),
            ["meetsRequirement"] = Try(() => weapon.HasRequirements(level)),
            ["meetsRequirementNextLevel"] = Try(() => weapon.HasRequirements(level + 1)),
            ["scrapValue"] = Try(() => weapon.GetScrapValue()),
            ["divinityRefund"] = Try(() => weapon.CalculateDivinityPointsRefund()),
            ["rawOptions"] = Benefits(weapon.currentOptions),
            ["skills"] = Skills(weapon.currentSkills),
            ["description"] = Try(() => weapon.GetDescription(level, false, false, false, false)),
            ["effectsNow"] = Effects(weapon, level),
            ["effectsNextLevel"] = Effects(weapon, level + 1),
        };
    }

    static object Effects(WeaponItemInventory weapon, int level) => new Dictionary<string, object>
    {
        ["permanent"] = Try(() => Benefits(weapon.GetPermanentBenefitsWithLevel(level))),
        ["options"] = Try(() => Benefits(weapon.GetOptionBenefitsWithLevel(level))),
        ["permanentSkills"] = Try(() => Skills(weapon.GetPermanentSkillsWithLevel(level))),
    };

    static List<object> Catalogue()
    {
        var result = new List<object>();
        foreach (WeaponItem item in Resources.FindObjectsOfTypeAll<WeaponItem>())
        {
            if (item == null) continue;
            KeyWeaponItem keyItem = item.TryCast<KeyWeaponItem>();
            result.Add(new Dictionary<string, object>
            {
                ["item"] = item.name,
                ["displayName"] = item.localizedName,
                ["equipment"] = item.forEquipment != null ? item.forEquipment.name : null,
                ["levelRequirement"] = item.levelRequirement,
                ["levelRequirementIncreasePerLevel"] = item.levelRequirementIncreasePerLevel,
                ["corrupted"] = item.corrupted,
                ["permanentOptions"] = Benefits(item.permanentOptions),
                ["permanentSkills"] = Skills(item.permanentSkills),
                ["possibleOptions"] = Benefits(item.possibleOptions),
                ["possibleSkills"] = Skills(item.possibleSkills),
                ["keyUpgrades"] = keyItem != null ? KeyUpgrades(keyItem) : null,
            });
        }
        return result;
    }

    static List<object> KeyUpgrades(KeyWeaponItem keyItem)
    {
        var result = new List<object>();
        if (keyItem.upgrades == null) return result;
        foreach (var upgrade in keyItem.upgrades)
        {
            if (upgrade == null) continue;
            var requirements = new List<object>();
            if (upgrade.requirements != null)
            {
                foreach (MaterialRequirement requirement in upgrade.requirements)
                {
                    if (requirement == null) continue;
                    requirements.Add(new Dictionary<string, object>
                    {
                        ["material"] = requirement.material != null ? requirement.material.name : null,
                        ["amount"] = requirement.amount,
                        ["divinityPoints"] = requirement.divinityPoints,
                    });
                }
            }
            result.Add(new Dictionary<string, object>
            {
                ["name"] = upgrade.name,
                ["displayName"] = upgrade.localizedName,
                ["permanentOptions"] = Benefits(upgrade.permanentOptions),
                ["permanentSkills"] = Skills(upgrade.permanentSkills),
                ["requirements"] = requirements,
            });
        }
        return result;
    }

    static List<object> Loadouts()
    {
        var result = new List<object>();
        foreach (LoadoutType loadout in Resources.FindObjectsOfTypeAll<LoadoutType>())
        {
            if (loadout == null) continue;
            var uids = new List<object>();
            if (loadout.currentItems != null)
            {
                for (int i = 0; i < loadout.currentItems.Count; i++)
                    uids.Add(loadout.currentItems[i]);
            }
            AscensionSkill skill = loadout.requiredAscensionSkill;
            result.Add(new Dictionary<string, object>
            {
                ["name"] = loadout.name,
                ["displayName"] = loadout.localizedName,
                ["description"] = loadout.localizedDescription,
                ["canBeAutoEquipped"] = loadout.canBeAutoEquipped,
                ["isAutoEquipped"] = loadout.isAutoEquipped,
                ["requiredAscensionSkill"] = skill != null ? skill.name : null,
                ["requiredAscensionSkillUnlocked"] = skill != null ? skill.unlocked : null,
                ["items"] = uids,
            });
        }
        return result;
    }

    static List<object> Benefits(Il2CppSystem.Collections.Generic.List<BenefitReferences> benefits)
    {
        var result = new List<object>();
        if (benefits == null) return result;
        for (int i = 0; i < benefits.Count; i++)
            result.Add(Benefit(benefits[i]));
        return result;
    }

    static List<object> Benefits(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BenefitReferences> benefits)
    {
        var result = new List<object>();
        if (benefits == null) return result;
        foreach (BenefitReferences benefit in benefits)
            result.Add(Benefit(benefit));
        return result;
    }

    static object Benefit(BenefitReferences benefit)
    {
        if (benefit == null) return null;
        return new Dictionary<string, object>
        {
            ["benefit"] = benefit.benefit != null ? benefit.benefit.name : null,
            ["value"] = benefit.value,
            ["value2"] = benefit.value2,
            ["target"] = Target(benefit),
            ["text"] = Try(() => benefit.GetDescription(false)),
        };
    }

    static string Target(BenefitReferences benefit)
    {
        if (benefit.map != null) return "map:" + benefit.map.name;
        if (benefit.enemyType != null) return "enemyType:" + benefit.enemyType.name;
        if (benefit.enemy != null) return "enemy:" + benefit.enemy.name;
        if (benefit.equipment != null) return "equipment:" + benefit.equipment.name;
        if (benefit.minion != null) return "minion:" + benefit.minion.name;
        if (benefit.character != null) return "character:" + benefit.character.name;
        if (benefit.stoneOfTime != null) return "stoneOfTime:" + benefit.stoneOfTime.name;
        if (benefit.npc != null) return "npc:" + benefit.npc.name;
        if (benefit.craftableItem != null) return "craftable:" + benefit.craftableItem.name;
        if (benefit.drop != null) return "drop:" + benefit.drop.name;
        if (benefit.loadoutType != null) return "loadout:" + benefit.loadoutType.name;
        return null;
    }

    static List<object> Skills(Il2CppSystem.Collections.Generic.List<WeaponItemSkill> skills)
    {
        var result = new List<object>();
        if (skills == null) return result;
        for (int i = 0; i < skills.Count; i++)
            result.Add(Skill(skills[i]));
        return result;
    }

    static List<object> Skills(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<WeaponItemSkill> skills)
    {
        var result = new List<object>();
        if (skills == null) return result;
        foreach (WeaponItemSkill skill in skills)
            result.Add(Skill(skill));
        return result;
    }

    static object Skill(WeaponItemSkill skill)
    {
        if (skill == null) return null;
        return new Dictionary<string, object>
        {
            ["name"] = skill.name,
            ["displayName"] = skill.localizedName,
            ["description"] = skill.localizedDescription,
        };
    }

    // One unreadable getter must not lose the rest of the report.
    static object Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            _errors++;
            return null;
        }
    }
}
