using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pipakin.SkillInjectorMod;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace QOL;

[BepInPlugin(pluginGUID, pluginName, pluginVersion)]
[BepInDependency("com.pipakin.SkillInjectorMod", BepInDependency.DependencyFlags.HardDependency)]
[BepInIncompatibility("VitalityRewrite")]
public class QOL : BaseUnityPlugin
{
    const string pluginGUID = "fortis.mods.qolmod";
    const string pluginName = "FortisQOL";
    const string pluginVersion = "0.0.1";

    private static QOL _instance;
    private static readonly int vitalitySkillId = 638;
    private static readonly int enduranceSkillId = 639;
    private static readonly Skills.SkillType VitalitySkill = (Skills.SkillType)vitalitySkillId;
    private static readonly Skills.SkillType EnduranceSkill = (Skills.SkillType)enduranceSkillId;

    private static ConfigFile QOLConfig;
    private static ConfigSync configSync = new ConfigSync(pluginGUID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

    // Server Sync
    private static ConfigEntry<Toggle> LockConfiguration;

    // Vitality Configurations
    private static ConfigEntry<int> MaxBaseHP;
    private static ConfigEntry<float> HealthRegen;
    private static ConfigEntry<float> VitalitySkillGainMultiplier;
    private static ConfigEntry<float> VitalityWorkSkillGainMultiplier;

    // Endurance Configurations
    private static ConfigEntry<int> MaxBaseStamina;
    private static ConfigEntry<float> BaseWalkSpeed;
    private static ConfigEntry<float> BaseRunSpeed;
    private static ConfigEntry<float> BaseSwimSpeed;
    private static ConfigEntry<float> StaminaRegen;
    private static ConfigEntry<float> StaminaDelay;
    private static ConfigEntry<float> StaminaJump;
    private static ConfigEntry<float> StaminaSwim;
    private static ConfigEntry<int> MaxBaseCarryWeight;
    private static ConfigEntry<float> EnduranceSkillGainMultiplier;

    // Rested Changes
    private static ConfigEntry<float> BaseRestTime;
    private static ConfigEntry<float> RestTimePerComfortLevel;
    private static readonly List<Player> _players;

    // Item Tweaks
    private static Dictionary<string, ConfigEntry<int>> ItemChanges;

    // Synced Config Stuff
    private ConfigEntry<T> CreateSyncedConfig<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = Config.Bind(section, key, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private static ConfigEntry<T> AddConfigToServerSync<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = QOLConfig.Bind(section, key, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    // Skill Factors
    private static float vitalitySkillFactor;
    private static float enduranceSkillFactor;

    private Harmony _harmony;

    // Logger Stuff
    private static readonly ManualLogSource QOLLogger = BepInEx.Logging.Logger.CreateLogSource(pluginName);
    private static readonly string LogPrefix = $"[FortisQOL v{pluginVersion}]";

    static QOL()
    {
        _players = new List<Player>();
        ItemChanges = new Dictionary<string, ConfigEntry<int>>();
    }

    public void Awake()
    {
        QOLLogger.LogInfo($"{LogPrefix} Initializing...");
        QOLConfig = Config;
        _instance = this;
        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginGUID);
        BindConfigs();
        configSync.SourceOfTruthChanged += ConfigSync_SourceOfTruthChanged;
        ClampConfig();
        InjectSkills();
        QOLLogger.LogInfo($"{LogPrefix} Initialized.");
    }

    private void ConfigSync_SourceOfTruthChanged(bool obj)
    {
        if (!configSync.IsSourceOfTruth)
        {
            QOLLogger.LogInfo($"{LogPrefix} Detected Incoming Config Sync From Server. Applying Patches");
        }
    }

    private void BindConfigs()
    {
        // Server Sync
        LockConfiguration = CreateSyncedConfig("1 - Server Sync", "LockConfig", Toggle.On, new ConfigDescription("For Server Admins only, if enabled, enforces server config to all connected player."));
        configSync.AddLockingConfigEntry(LockConfiguration);

        // Endurance Configurations
        MaxBaseStamina = CreateSyncedConfig("2 - Endurance Configurations", "MaxBaseStamina", 200, new ConfigDescription("The max base stamina when Endurance skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        MaxBaseStamina.SettingChanged += SyncedItemConfig_SettingChanged;
        BaseWalkSpeed = CreateSyncedConfig("2 - Endurance Configurations", "BaseWalkSpeedMultiplier", 50f, new ConfigDescription("Increase of base walking speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseWalkSpeed.SettingChanged += SyncedItemConfig_SettingChanged;
        BaseRunSpeed = CreateSyncedConfig("2 - Endurance Configurations", "BaseRunSpeedMultiplier", 50f, new ConfigDescription("Increase of base running speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseRunSpeed.SettingChanged += SyncedItemConfig_SettingChanged;
        BaseSwimSpeed = CreateSyncedConfig("2 - Endurance Configurations", "BaseSwimSpeedMultiplier", 75f, new ConfigDescription("Increase of base swimming speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseSwimSpeed.SettingChanged += SyncedItemConfig_SettingChanged;
        StaminaRegen = CreateSyncedConfig("2 - Endurance Configurations", "StaminaRegen", 72f, new ConfigDescription("Increase of base stamina regeneration in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        StaminaRegen.SettingChanged += SyncedItemConfig_SettingChanged;
        StaminaDelay = CreateSyncedConfig("2 - Endurance Configurations", "StaminaDelay", 50f, new ConfigDescription("Decrease the delay for stamina regeneration after usage in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaDelay.SettingChanged += SyncedItemConfig_SettingChanged;
        StaminaJump = CreateSyncedConfig("2 - Endurance Configurations", "StaminaJump", 25f, new ConfigDescription("Decrease of stamina cost per jump in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaJump.SettingChanged += SyncedItemConfig_SettingChanged;
        StaminaSwim = CreateSyncedConfig("2 - Endurance Configurations", "StaminaSwim", 33f, new ConfigDescription("Decrease of stamina cost while swimming at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaSwim.SettingChanged += SyncedItemConfig_SettingChanged;
        MaxBaseCarryWeight = CreateSyncedConfig("2 - Endurance Configurations", "MaxCarryWeight", 450, new ConfigDescription("Max carry weight at Endurance skill 100.", new AcceptableValueRange<int>(300, 999)));
        MaxBaseCarryWeight.SettingChanged += SyncedItemConfig_SettingChanged;
        EnduranceSkillGainMultiplier = CreateSyncedConfig("2 - Endurance Configurations", "EnduranceSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Endurance skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));
        EnduranceSkillGainMultiplier.SettingChanged += SyncedItemConfig_SettingChanged;

        // Vitality Configurations
        MaxBaseHP = CreateSyncedConfig("3 - Vitality Configurations", "MaxBaseHP", 150, new ConfigDescription("The max base HP when Vitality skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        MaxBaseHP.SettingChanged += SyncedItemConfig_SettingChanged;
        HealthRegen = CreateSyncedConfig("3 - Vitality Configurations", "HealthRegeneration", 100f, new ConfigDescription("Increase of base health regeneration in percent at Vitality skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        HealthRegen.SettingChanged += SyncedItemConfig_SettingChanged;
        VitalitySkillGainMultiplier = CreateSyncedConfig("3 - Vitality Configurations", "VitalitySkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Vitality skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalitySkillGainMultiplier.SettingChanged += SyncedItemConfig_SettingChanged;
        VitalityWorkSkillGainMultiplier = CreateSyncedConfig("3 - Vitality Configurations", "VitalityWorkSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast skill is gained via damage of your tools", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalityWorkSkillGainMultiplier.SettingChanged += SyncedItemConfig_SettingChanged;

        // Rested Tweaks Configurations
        BaseRestTime = CreateSyncedConfig("4 - Rested Tweaks", "BaseRestTime", 600f, new ConfigDescription("The base time in seconds for Rested durations. Default is 480 seconds (8 minutes)", new AcceptableValueRange<float>(0.01f, 36000f)));
        BaseRestTime.SettingChanged += SyncedItemConfig_SettingChanged;
        RestTimePerComfortLevel = CreateSyncedConfig("4 - Rested Tweaks", "RestedTimePerComfortLevel", 180f, new ConfigDescription("The time in seconds to add to rested duration per comfort level. Game Default is 60.0", new AcceptableValueRange<float>(0.01f, 3600f)));
        RestTimePerComfortLevel.SettingChanged += SyncedItemConfig_SettingChanged;
    }

    private static void ClampConfig()
    {
        // This just makes sure that the configuration entries don't cause errors
        if (BaseRestTime.Value < 0.0f)
            BaseRestTime.Value = 0.0f;
        if (BaseRestTime.Value > 36000f)
            BaseRestTime.Value = 36000f;

        if (RestTimePerComfortLevel.Value < 0.0f)
            RestTimePerComfortLevel.Value = 0.0f;
        if (RestTimePerComfortLevel.Value > 3600f)
            RestTimePerComfortLevel.Value = 3600f;
    }

    private void InjectSkills()
    {
        // Inject our skills
        SkillInjector.RegisterNewSkill(vitalitySkillId, "Vitality", "Increase base HP and health regen", 1f, LoadCustomTexture("vitality-icon.png"), Skills.SkillType.None);
        SkillInjector.RegisterNewSkill(enduranceSkillId, "Endurance", "Increase base stamina and carry weight", 1f, LoadCustomTexture("endurance-icon.png"), Skills.SkillType.None);
    }

    // When plugin is unloaded
    private void OnDestroy()
    {
        _instance = null;
        _harmony.UnpatchSelf();
    }

    // ServerSync Patches
    [HarmonyPatch(typeof(ConfigSync))]
    public static class ServerSyncPatches
    {
        // If server toggled configuration lock  on. Sync server config and apply server values to Player joining
        [HarmonyPatch("RPC_FromServerConfigSync")]
        [HarmonyPatch("RPC_FromOtherClientConfigSync")]
        [HarmonyPatch("resetConfigsFromServer")]
        private static void Postfix()
        {
            // Apply Rested Changes
            foreach (SE_Rested effect in GetAllRestedEffects())
            {
                effect.m_baseTTL = BaseRestTime.Value;
                effect.m_TTLPerComfortLevel = RestTimePerComfortLevel.Value;
            }

            if (!ObjectDB.instance.isActiveAndEnabled)
            {
                QOLLogger.LogWarning($"{LogPrefix} ObjectDB Is not ready!");
                return;
            }


            if (ObjectDB.instance.isActiveAndEnabled)
            {
                foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
                {
                    if (!ItemChanges.ContainsKey($"{itemPrefab.name}_max_stack"))
                        continue;

                    var itemConfig = ItemChanges[$"{itemPrefab.name}_max_stack"];
                    itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize = itemConfig.Value;
                }
            }
        }
    }

    // Rested
    [HarmonyPatch(typeof(FejdStartup), "SetupObjectDB")]
    public static class RestedEffectChanges
    {
        [HarmonyPriority(Priority.LowerThanNormal)]
        private static void Postfix()
        {
            SE_Rested effect = GetRestedEffect();
            if (effect != null)
            {
                QOLLogger.LogInfo($"{LogPrefix} Successfully got rested effect. Applying patches");
                effect.m_baseTTL = BaseRestTime.Value;
                effect.m_TTLPerComfortLevel = RestTimePerComfortLevel.Value;
                QOLLogger.LogInfo($"{LogPrefix} Patches applied");
            }
            else
                QOLLogger.LogWarning($"{LogPrefix} Could not load SE_Rested effect object. Rested changes won't work");
        }
    }

    // Create/set Singleplayer item stack sizes
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class ModifyItemStackSize
    {
        private static void Postfix(ObjectDB __instance)
        {
            QOLLogger.LogInfo($"{LogPrefix} Binding Item Configs...");
            foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
            {
                if (itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize > 1)
                {
                    if (!ItemChanges.TryGetValue($"{itemPrefab.name}_max_stack", out ConfigEntry<int> config))
                    {
                        string itemName = $"{itemPrefab.name}_max_stack";
                        var syncedItemConfig = AddConfigToServerSync("5 - Item Tweaks", itemName, itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize, new ConfigDescription("Set the max stack size", new AcceptableValueRange<int>(1, 999)));
                        syncedItemConfig.SettingChanged += SyncedItemConfig_SettingChanged;
                        ItemChanges.Add($"{itemPrefab.name}_max_stack", syncedItemConfig);
                        itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize = syncedItemConfig.Value;
                    }
                    else
                        itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize = config.Value;
                }
            }

            QOLLogger.LogInfo($"{LogPrefix} Item Configs Binded.");
        }
    }

    // Player Patches

    [HarmonyPatch(typeof(Player), "Awake")]
    public static class AddPlayerToCache
    {
        private static void Postfix(Player __instance)
        {
            _players.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDestroy")]
    public static class RemovePlayerFromCache
    {
        private static void Prefix(Player __instance)
        {
            _players.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Load")]
    public static class AttributeOverwriteOnLoad
    {
        private static float m_maxCarryWeight;
        private static float m_jumpStaminaUse;
        private static float m_staminaRegenDelay;
        private static float m_staminaRegen;
        private static float m_swimStaminaDrainMinSkill;
        private static float m_swimStaminaDrainMaxSkill;
        private static float m_swimSpeed;
        private static float m_swimTurnSpeed;

        private static void Postfix(Player __instance, ZPackage pkg)
        {
            AttributeOverwriteOnLoad.m_maxCarryWeight = __instance.m_maxCarryWeight;
            AttributeOverwriteOnLoad.m_jumpStaminaUse = __instance.m_jumpStaminaUsage;
            AttributeOverwriteOnLoad.m_staminaRegenDelay = __instance.m_staminaRegenDelay;
            AttributeOverwriteOnLoad.m_staminaRegen = __instance.m_staminaRegen;
            AttributeOverwriteOnLoad.m_swimStaminaDrainMinSkill = __instance.m_swimStaminaDrainMinSkill;
            AttributeOverwriteOnLoad.m_swimStaminaDrainMaxSkill = __instance.m_swimStaminaDrainMaxSkill;
            AttributeOverwriteOnLoad.m_swimSpeed = __instance.m_swimSpeed;
            AttributeOverwriteOnLoad.m_swimTurnSpeed = __instance.m_swimTurnSpeed;

            ApplyVitalitySkillFactors(__instance);
            ApplyEnduranceSkillFactors(__instance);

            if (__instance.GetPlayerName() != null && __instance.GetPlayerName().Length > 0)
            {
                string baseStats = $"Vitality Skill Factor: {vitalitySkillFactor}" +
                    $"\nVitality Skill Level: {Math.Round((double)__instance.GetSkillLevel(VitalitySkill), 2)}" +
                    $"\nVitality Base HP Bonus: {Math.Round((double)(vitalitySkillFactor * MaxBaseHP.Value))}" +
                    $"\nEndurance Skill Factor: {enduranceSkillFactor}" +
                    $"\nEndurance Skill Level: {Math.Round((double)__instance.GetSkillLevel(EnduranceSkill), 2)}" +
                    $"\nEndurance Base Stamina Bonus: {Math.Round((double)(enduranceSkillFactor * MaxBaseStamina.Value))}" +
                    $"\nEndurance Base Carry Weight Bonus: {Math.Round((double)(enduranceSkillFactor * MaxBaseCarryWeight.Value))}" +
                    $"\nEndurance Base Walk Speed Bonus: {Math.Round((double)(enduranceSkillFactor * BaseWalkSpeed.Value / 100), 2)}" +
                    $"\nEndurance Base Run Speed Bonus: {Math.Round((double)(enduranceSkillFactor * BaseRunSpeed.Value / 100), 2)}" +
                    $"\nEndurance Base Swim Speed Bonus: {Math.Round((double)(1 + enduranceSkillFactor * BaseSwimSpeed.Value / 100), 2)}" +
                    $"\nEndurance Stamina Regen Delay Bonus: {Math.Round((double)(1 - enduranceSkillFactor * StaminaDelay.Value / 100), 2)}" +
                    $"\nEndurance Jump Stamina Cost Bonus: {Math.Round((double)(1 - enduranceSkillFactor * StaminaJump.Value / 100), 2)}" +
                    $"\nEndurance Stamina Regen Bonus: {Math.Round((double)(1 + enduranceSkillFactor * StaminaRegen.Value / 100), 2)}" +
                    $"\nEndurance Min Skill Drain Bonus: {Math.Round((double)(1 - enduranceSkillFactor * StaminaSwim.Value / 100), 2)}" +
                    $"\nEndurance Max Skill Drain Bonus: {Math.Round((double)(1 - enduranceSkillFactor * StaminaSwim.Value / 100), 2)}" +
                    $"\nEndurance Swim Turn Speed Bonus: {Math.Round((double)(1 + enduranceSkillFactor * BaseSwimSpeed.Value / 100), 2)}";

                QOLLogger.LogInfo($"{LogPrefix} Get Load request for player: {__instance.GetPlayerName()}\n{baseStats}");
            }
        }

        public static void ApplyVitalitySkillFactors(Player __instance)
        {
            vitalitySkillFactor = __instance.GetSkillFactor(VitalitySkill);
            float config;

            //string playerName = __instance.GetPlayerName();
            config = MaxBaseHP.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Base HP For Player: {playerName} from {__instance.m_baseHP}");
            __instance.m_baseHP += vitalitySkillFactor * config;
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_baseHP}");
        }

        public static void ApplyEnduranceSkillFactors(Player __instance)
        {
            enduranceSkillFactor = __instance.GetSkillFactor(EnduranceSkill);
            float config;

            //string playerName = __instance.GetPlayerName();
            config = MaxBaseCarryWeight.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Base Carry Weight For Player: {playerName} from {__instance.m_maxCarryWeight}");
            __instance.m_maxCarryWeight = AttributeOverwriteOnLoad.m_maxCarryWeight + enduranceSkillFactor * config;
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_maxCarryWeight}");

            config = StaminaJump.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Base Jump Stamina Cost For Player: {playerName} from {__instance.m_jumpStaminaUsage}");
            __instance.m_jumpStaminaUsage = AttributeOverwriteOnLoad.m_jumpStaminaUse * (1 - enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_jumpStaminaUsage}");

            config = StaminaDelay.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Regen Delay For Player: {playerName} from {__instance.m_staminaRegenDelay}");
            __instance.m_staminaRegenDelay = AttributeOverwriteOnLoad.m_staminaRegenDelay * (1 - enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_staminaRegenDelay}");

            config = StaminaRegen.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Regen For Player: {playerName} from {__instance.m_staminaRegen}");
            __instance.m_staminaRegen = AttributeOverwriteOnLoad.m_staminaRegen * (1 + enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_staminaRegen}");

            config = StaminaSwim.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Swim Min Cost For Player {playerName} from {__instance.m_swimStaminaDrainMinSkill}");
            __instance.m_swimStaminaDrainMinSkill = AttributeOverwriteOnLoad.m_swimStaminaDrainMinSkill * (1 - enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimStaminaDrainMinSkill}");

            //QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Swim Max Cost For Player {playerName} from {__instance.m_swimStaminaDrainMaxSkill}");
            __instance.m_swimStaminaDrainMaxSkill = AttributeOverwriteOnLoad.m_swimStaminaDrainMaxSkill * (1 - enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimStaminaDrainMaxSkill}");

            config = BaseSwimSpeed.Value;
            //QOLLogger.LogInfo($"{LogPrefix} Changing Base Swim Speed For Player {playerName} from {__instance.m_swimSpeed}");
            __instance.m_swimSpeed = AttributeOverwriteOnLoad.m_swimSpeed * (1 + enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimSpeed}");

            //QOLLogger.LogInfo($"{LogPrefix} Changing Base Swim Turn Speed For Player {playerName} from {__instance.m_swimTurnSpeed}");
            __instance.m_swimTurnSpeed = AttributeOverwriteOnLoad.m_swimTurnSpeed * (1 + enduranceSkillFactor * config / 100);
            //QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimTurnSpeed}");
        }
    }

    // Vitality Skill

    [HarmonyPatch(typeof(Player), "UpdateFood")]
    public static class FoodHealthRegenPatches
    {
        private static void Prefix(Player __instance, ref float ___m_foodRegenTimer)
        {
            if (___m_foodRegenTimer == 0)
            {
                float configValue = HealthRegen.Value;
                float regenMp = vitalitySkillFactor * configValue / 100 + 1;
                if (regenMp > 0)
                    ___m_foodRegenTimer = 10 - 10 / regenMp;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "SetMaxHealth")]
    public static class MaxHealth
    {
        private static void Prefix(Player __instance, ref float health, bool flashBar)
        {
            QOLLogger.LogInfo($"{LogPrefix} SetMaxHealth fired");
            float configValue = MaxBaseHP.Value;
            health += vitalitySkillFactor * configValue;
            QOLLogger.LogInfo($"{LogPrefix} New health: {health}");
        }
    }

    [HarmonyPatch(typeof(MineRock5), "DamageArea")]
    public static class MiningBigRocks
    {
        private static void Prefix(MineRock5 __instance, HitData hit, float __state)
        {
            Player player = Player.m_localPlayer;
            if ((player == null) || !(player.GetZDOID() == hit.m_attacker) || hit.m_skill != Skills.SkillType.Pickaxes)
                return;

            float skillGain = 0.04f + hit.m_damage.m_pickaxe * 0.00075f * VitalityWorkSkillGainMultiplier.Value;
            IncreaseVitality(player, skillGain);
        }
    }

    [HarmonyPatch(typeof(Destructible), "Damage")]
    public static class DestroySmallStuff
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if ((player is null) || !(player.GetZDOID() == hit.m_attacker))
                return;

            if (__instance.name.ToLower().Contains("rock") && hit.m_skill == Skills.SkillType.Pickaxes)
            {
                float skillGain = 0.1f + hit.m_damage.m_pickaxe * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
            }
            else if (hit.m_skill == Skills.SkillType.WoodCutting)
            {
                float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
            }
        }
    }

    [HarmonyPatch(typeof(TreeBase), "Damage")]
    public static class WoodCutting
    {
        private static void Prefix(TreeBase __instance, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if ((player is null) || !(player.GetZDOID() == hit.m_attacker))
                return;

            if (hit.m_skill == Skills.SkillType.WoodCutting && hit.m_toolTier >= __instance.m_minToolTier)
            {
                float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
            }
        }
    }

    [HarmonyPatch(typeof(TreeLog), "Damage")]
    public static class WoodCutting_II
    {
        private static void Prefix(TreeLog __instance, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if ((player == null) || !(player.GetZDOID() == hit.m_attacker))
            {
                return;
            }
            if (hit.m_skill == Skills.SkillType.WoodCutting && hit.m_toolTier >= __instance.m_minToolTier)
            {
                float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
            }
        }
    }

    // Endurance Skill

    [HarmonyPatch(typeof(Player), "SetMaxStamina")]
    public static class MaxStamina
    {
        private static void Prefix(Player __instance, ref float stamina, bool flashBar)
        {
            float configValue = MaxBaseStamina.Value;
            stamina += enduranceSkillFactor * configValue;
        }
    }

    [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")]
    public static class EnduranceWalkSpeed
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            float configValue = BaseWalkSpeed.Value;
            __result += enduranceSkillFactor * configValue / 100;
        }
    }

    [HarmonyPatch(typeof(Player), "GetRunSpeedFactor")]
    public static class EnduracneRunSpeed
    {
        private static void Postfix(Player __instance, ref float __result)
        {
           float configValue = BaseRunSpeed.Value;
           __result += enduranceSkillFactor * configValue / 100;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateStats", new Type[] { typeof(float) })]
    public static class LevelSkill
    {
        private static void Prefix(Player __instance, float dt)
        {
            if (!__instance.IsFlying())
            {
                if (__instance.IsRunning() && __instance.IsOnGround())
                {
                    runSwimSkill += 0.1f * dt;
                }
                if (__instance.InWater() && !__instance.IsOnGround())
                {
                    if (stamina != __instance.GetStaminaPercentage())
                    {
                        runSwimSkill += 0.25f * dt;
                    }
                    stamina = __instance.GetStaminaPercentage();
                }

                if (runSwimSkill >= 1.0f)
                {
                    IncreaseEndurance(__instance, runSwimSkill);
                    runSwimSkill = 0.0f;
                }
            }
        }

        private static float stamina;
        private static float runSwimSkill = 0.0f;
    }

    [HarmonyPatch(typeof(Player), "OnSkillLevelup")]
    public static class LevelupSkillApplyValues
    {
        private static void Postfix(Player __instance, Skills.SkillType skill, float level)
        {
            if (skill == EnduranceSkill)
            {
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "OnJump")]
    public static class EnduranceSkillOnJump
    {
        private static void Prefix(Player __instance)
        {
            float num = __instance.m_jumpStaminaUsage - __instance.m_jumpStaminaUsage * __instance.GetEquipmentJumpStaminaModifier();
            bool b = __instance.HaveStamina(num * Game.m_moveStaminaRate);
            if (b)
                IncreaseEndurance(__instance, 0.14f);
        }
    }

    // Terminal Patches

    [HarmonyPatch(typeof(Terminal), "InputText")]
    private static class InputText_Patch
    {
        private static void Postfix(Terminal __instance)
        {
            string text = __instance.m_input.text;
            if (text.ToLower().Contains("raiseskill vitality") && (Player.m_localPlayer != null))
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(Player.m_localPlayer);
            if (text.ToLower().Contains("raiseskill endurance") && (Player.m_localPlayer != null))
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(Player.m_localPlayer);
        }
        private static bool Prefix(Terminal __instance)
        {
            string text = __instance.m_input.text;
            if (text.ToLower().Equals("qol reload"))
            {
                _instance.Config.Reload();
                foreach (var player in Player.GetAllPlayers())
                    AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(player);
                Traverse.Create(__instance).Method("AddString", new object[]
                {
                            text
                }).GetValue();
                Traverse.Create(__instance).Method("AddString", new object[]
                {
                            "QOL config reloaded"
                }).GetValue();
                return false;
            }
            else if (text.ToLower().Equals("qol apply"))
            {
                foreach (var player in Player.GetAllPlayers())
                    AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(player);
                Traverse.Create(__instance).Method("AddString", new object[]
                {
                            text
                }).GetValue();
                Traverse.Create(__instance).Method("AddString", new object[]
                {
                            "QOL config applied"
                }).GetValue();
                return false;
            }

            return true;
        }
    }


    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class TerminalInitConsole_Patch
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("qol", "with keyword 'reload': Reload config of QOL. With keyword 'apply': Apply changes done in-game (Configuration Manager)", null);
        }
    }

    private static void IncreaseVitality(Player player, float value)
    {
        player.RaiseSkill(VitalitySkill, value * VitalitySkillGainMultiplier.Value);
        AttributeOverwriteOnLoad.ApplyVitalitySkillFactors(player);
    }

    private static void IncreaseEndurance(Player player, float value)
    {
        player.RaiseSkill(EnduranceSkill, value * EnduranceSkillGainMultiplier.Value);
        AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(player);
    }

    private static void SyncedItemConfig_SettingChanged(object sender, EventArgs e)
    {
        if (sender is ConfigEntry<int> configEntry)
        {
            if (configSync.IsSourceOfTruth)
                QOLLogger.LogInfo($"{LogPrefix} ServerSync is applying local config entry {configEntry.Definition.Key} new value {configEntry.Value}");
            else
                QOLLogger.LogInfo($"{LogPrefix} ServerSync is applying server config entry {configEntry.Definition.Key} new value {configEntry.Value}");
        }

        if (sender is ConfigEntry<float> configEntry2)
        {
            if (configSync.IsSourceOfTruth)
                QOLLogger.LogInfo($"{LogPrefix} ServerSync is applying local config entry {configEntry2.Definition.Key} new value {configEntry2.Value}");
            else
                QOLLogger.LogInfo($"{LogPrefix} ServerSync is applying server config entry {configEntry2.Definition.Key} new value {configEntry2.Value}");
        }
    }

    private static IEnumerable<SE_Rested> GetAllRestedEffects()
    {
        SE_Rested effect = GetRestedEffect();
        if (effect != null)
            yield return effect;

        foreach (Player player in _players)
        {
            SE_Rested playerEffect = GetRestedEffect(player);
            if (playerEffect != null)
                yield return playerEffect;
        }
    }

    private static SE_Rested GetRestedEffect() => (SE_Rested)ObjectDB.instance?.GetStatusEffect(SEMan.s_statusEffectRested);
    private static SE_Rested GetRestedEffect(Player player) => (SE_Rested)player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);

    // Skill Icons
    private static Sprite LoadCustomTexture(string fileName)
    {
        Stream manifestResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QOL.icons." + fileName);
        byte[] array = new byte[manifestResourceStream.Length];
        manifestResourceStream.Read(array, 0, (int)manifestResourceStream.Length);
        Texture2D texture2d = new Texture2D(2, 2);
        ImageConversion.LoadImage(texture2d, array);
        texture2d.Apply();
        return Sprite.Create(texture2d, new Rect(0f, 0f, texture2d.width, texture2d.height), new Vector2(0f, 0f), 50f);
    }

    private enum Toggle
    {
        On = 1,
        Off = 0
    }
}