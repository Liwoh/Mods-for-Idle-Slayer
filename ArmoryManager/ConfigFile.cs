using IdleSlayerMods.Common.Config;
using MelonLoader;
using UnityEngine;

namespace ArmoryManager;

internal sealed class ConfigFile(string configName) : BaseConfig(configName)
{
    internal MelonPreferences_Entry<int> LowerBoundOfWeapons;
    internal MelonPreferences_Entry<int> CurrentNumberOfWeaponsToKeep;
    internal MelonPreferences_Entry<int> RequirementPerLevel;

    internal MelonPreferences_Entry<bool> BreakWeaponsEnabled;
    internal MelonPreferences_Entry<bool> BreakDupesEnabled;
    internal MelonPreferences_Entry<bool> BreakLowLevelEnabled;
    internal MelonPreferences_Entry<bool> KeepBestUniqueWeapons;

    internal MelonPreferences_Entry<KeyCode> DecreaseWeaponsToKeep;
    internal MelonPreferences_Entry<KeyCode> IncreaseWeaponsToKeep;

    protected override void SetBindings()
    {
        BreakWeaponsEnabled = Bind("Break Weapons Enabled", true,
            "Enable autobreaking of weapons in Armory");
        BreakDupesEnabled = Bind("Break Dupe Weapons", true,
            "Enable breaking of duplicate weapons in Armory");
        BreakLowLevelEnabled = Bind("Break Low Level Weapons", true,
            "Enable breaking of low level weapons in Armory");
        KeepBestUniqueWeapons = Bind("Keep Best Unique Weapons", true,
            "Only break a weapon when another weapon of the same equipment covers it: same or better excellence, all its options, all its skills, and the same or a higher level. Unique weapons are kept regardless of level and keep count; the game's always-break slots still apply.");

        RequirementPerLevel = Bind("RequirementPerLevel", 15,
            "Armory requirement per level only in 15-50");
        LowerBoundOfWeapons = Bind("LowerBoundOfWeapons", 5,
            "Minimum number of weapons to keep in Armory Slots");

        DecreaseWeaponsToKeep = Bind("Decrease Weapons To Keep", KeyCode.Minus,
            "Key to decrease the number of weapons to keep in Armory");
        IncreaseWeaponsToKeep = Bind("Increase Weapons To Keep", KeyCode.Equals,
            "Key to increase the number of weapons to keep in Armory");
        CurrentNumberOfWeaponsToKeep = Bind("CurrentNumberOfWeaponsToKeep", 15,
            "Do Not Change - Stored Value of Weapons to Keep");
    }
}