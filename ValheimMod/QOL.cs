using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pipakin.SkillInjectorMod;
using QOL.Items;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
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

    private static ConfigSync configSync = new ConfigSync(pluginGUID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

    // Config File so ItemManager can Bind individual item configs
    public static ConfigFile Configs;

    // Server Sync
    private static ConfigEntry<Toggle> LockConfiguration;

    // General
    private static ConfigEntry<bool> EnableMod;

    // Vitality Configurations
    private static ConfigEntry<bool> EnableVitality;
    private static ConfigEntry<int> MaxBaseHP;
    private static ConfigEntry<float> HealthRegen;
    private static ConfigEntry<float> VitalitySkillGainMultiplier;
    private static ConfigEntry<float> VitalityWorkSkillGainMultiplier;

    // Endurance Configurations
    private static ConfigEntry<bool> EnableEndurance;
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
    private static ConfigEntry<bool> EnableRestedChanges;
    private static ConfigEntry<float> BaseRestTime;
    private static ConfigEntry<float> RestTimePerComfortLevel;
    private static readonly List<Player> _players;

    // Item Changes
    private static ConfigEntry<bool> EnableItemChanges;
    public static Dictionary<string, ConfigEntry<int>> _itemChanges;

    // Synced Config Stuff
    public ConfigEntry<T> CreateSyncedConfig<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = Config.Bind(section, key, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    public ConfigEntry<T> CreateSyncedConfig<T>(string section, string key, T value, string description, bool synchronizedSetting = true) => CreateSyncedConfig(section, key, value, new ConfigDescription(description), synchronizedSetting);

    // Skill Factors
    private static float vitalitySkillFactor;
    private static float enduranceSkillFactor;

    private Harmony _harmony;

    // Logger Stuff
    public static readonly ManualLogSource QOLLogger = BepInEx.Logging.Logger.CreateLogSource(pluginName);
    public static readonly string LogPrefix = $"[FortisQOL v{pluginVersion}]";

    static QOL()
    {
        _players = new List<Player>();
        _itemChanges = new Dictionary<string, ConfigEntry<int>>();
    }

    public void Awake()
    {
        QOLLogger.LogInfo($"{LogPrefix} Initializing...");
        Configs = Config;
        _instance = this;
        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginGUID);
        BindConfigs();
        configSync.SourceOfTruthChanged += ConfigSync_SourceOfTruthChanged;
        ClampConfig();
        if (!EnableMod.Value)
        {
            QOLLogger.LogInfo($"{LogPrefix} Mod Disabled, Stopping Initialization");
            return;
        }
        InjectSkills();
        QOLLogger.LogInfo($"{LogPrefix} Initialized.");
    }

    private void ConfigSync_SourceOfTruthChanged(bool obj)
    {
        if (!configSync.IsSourceOfTruth)
        {
            QOLLogger.LogInfo($"{LogPrefix} Detected Incoming Config Sync From Server. Applying Patches");
            DedicatedServerPatches();
            QOLLogger.LogInfo($"{LogPrefix} Server Patches Applied");
        }
    }

    private void BindConfigs()
    {
        // Server Sync
        LockConfiguration = CreateSyncedConfig("1 - Server Sync", "LockConfig", Toggle.On, new ConfigDescription("For Server Admins only, if enabled, enforces server config to all connected player."));
        configSync.AddLockingConfigEntry(LockConfiguration);

        // General Binds
        EnableMod = CreateSyncedConfig("2 - General", "EnableMod", true, new ConfigDescription("Enable the entire mod. Default is true"));

        // Endurance Configurations
        EnableEndurance = CreateSyncedConfig("3 - Endurance Configurations", "EnableEndurance", true, new ConfigDescription("Enables the Endurance skill. Default is true"));
        MaxBaseStamina = CreateSyncedConfig("3 - Endurance Configurations", "MaxBaseStamina", 200, new ConfigDescription("The max base stamina when Endurance skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        BaseWalkSpeed = CreateSyncedConfig("3 - Endurance Configurations", "BaseWalkSpeedMultiplier", 50f, new ConfigDescription("Increase of base walking speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseRunSpeed = CreateSyncedConfig("3 - Endurance Configurations", "BaseRunSpeedMultiplier", 50f, new ConfigDescription("Increase of base running speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseSwimSpeed = CreateSyncedConfig("3 - Endurance Configurations", "BaseSwimSpeedMultiplier", 75f, new ConfigDescription("Increase of base swimming speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaRegen = CreateSyncedConfig("3 - Endurance Configurations", "StaminaRegen", 72f, new ConfigDescription("Increase of base stamina regeneration in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        StaminaDelay = CreateSyncedConfig("3 - Endurance Configurations", "StaminaDelay", 50f, new ConfigDescription("Decrease the delay for stamina regeneration after usage in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaJump = CreateSyncedConfig("3 - Endurance Configurations", "StaminaJump", 25f, new ConfigDescription("Decrease of stamina cost per jump in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaSwim = CreateSyncedConfig("3 - Endurance Configurations", "StaminaSwim", 33f, new ConfigDescription("Decrease of stamina cost while swimming at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        MaxBaseCarryWeight = CreateSyncedConfig("3 - Endurance Configurations", "MaxCarryWeight", 450, new ConfigDescription("Max carry weight at Endurance skill 100.", new AcceptableValueRange<int>(300, 999)));
        EnduranceSkillGainMultiplier = CreateSyncedConfig("3 - Endurance Configurations", "EnduranceSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Endurance skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));

        // Vitality Configurations
        EnableVitality = CreateSyncedConfig("4 - Vitality Configurations", "EnableVitality", true, new ConfigDescription("Enables the Vitality skill. Default is true"));
        MaxBaseHP = CreateSyncedConfig("4 - Vitality Configurations", "MaxBaseHP", 150, new ConfigDescription("The max base HP when Vitality skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        HealthRegen = CreateSyncedConfig("4 - Vitality Configurations", "HealthRegeneration", 100f, new ConfigDescription("Increase of base health regeneration in percent at Vitality skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalitySkillGainMultiplier = CreateSyncedConfig("4 - Vitality Configurations", "VitalitySkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Vitality skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalityWorkSkillGainMultiplier = CreateSyncedConfig("4 - Vitality Configurations", "VitalityWorkSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast skill is gained via damage of your tools", new AcceptableValueRange<float>(0.01f, 999f)));

        // Rested Tweaks Configurations
        EnableRestedChanges = CreateSyncedConfig("5 - Rested Tweaks", "EnableRestedChanges", true, "Enables changing rested to scale more based on comfort level and base rested time. Default is true.");
        BaseRestTime = CreateSyncedConfig("5 - Rested Tweaks", "BaseRestTime", 600f, new ConfigDescription("The base time in seconds for Rested durations. Default is 480 seconds (8 minutes)", new AcceptableValueRange<float>(0.01f, 36000f)));
        BaseRestTime.SettingChanged += BaseRestTime_SettingChanged;
        RestTimePerComfortLevel = CreateSyncedConfig("5 - Rested Tweaks", "RestedTimePerComfortLevel", 180f, new ConfigDescription("The time in seconds to add to rested duration per comfort level. Game Default is 60.0", new AcceptableValueRange<float>(0.01f, 3600f)));
        RestTimePerComfortLevel.SettingChanged += RestTimePerComfortLevel_SettingChanged;

        // Item Tweaks Configurations
        EnableItemChanges = CreateSyncedConfig("6 - Item Tweaks", "EnableItemChanges", true, "Enables changing items max stack amount. Default is true.");
    }

    private void RestTimePerComfortLevel_SettingChanged(object sender, EventArgs e)
    {
        ClampConfig();

        foreach (SE_Rested effect in GetAllRestedEffects())
        {
            effect.m_TTLPerComfortLevel = RestTimePerComfortLevel.Value;
        }
    }

    private void BaseRestTime_SettingChanged(object sender, EventArgs e)
    {
        ClampConfig();

        foreach (SE_Rested effect in GetAllRestedEffects())
        {
            effect.m_baseTTL = BaseRestTime.Value;
        }
    }

    private static void ClampConfig()
    {
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
        SkillInjector.RegisterNewSkill(vitalitySkillId, "Vitality", "Increase base HP and health regen", 1f, LoadCustomTexture("vitality-icon.png"), Skills.SkillType.None);
        SkillInjector.RegisterNewSkill(enduranceSkillId, "Endurance", "Increase base stamina and carry weight", 1f, LoadCustomTexture("endurance-icon.png"), Skills.SkillType.None);
    }

    private void OnDestroy()
    {
        Config.Save();
        _instance = null;
        _harmony.UnpatchSelf();
    }

    [HarmonyPatch(typeof(FejdStartup), "SetupObjectDB")]
    public static class RestedEffectChanges
    {
        private static void Postfix()
        {
            if (EnableRestedChanges.Value)
            {
                SE_Rested effect = GetRestedEffect();
                if (effect != null)
                {
                    QOLLogger.LogInfo($"{LogPrefix} Successfully got rested effect.");
                    QOLLogger.LogInfo($"{LogPrefix} Changing base duration from {effect.m_baseTTL}");
                    effect.m_baseTTL = BaseRestTime.Value;
                    QOLLogger.LogInfo($"{LogPrefix} To {effect.m_baseTTL}");
                    QOLLogger.LogInfo($"{LogPrefix} Changing duration per comfort level from {effect.m_TTLPerComfortLevel}");
                    effect.m_TTLPerComfortLevel = RestTimePerComfortLevel.Value;
                    QOLLogger.LogInfo($"{LogPrefix} To {effect.m_TTLPerComfortLevel}");
                }
                else
                    QOLLogger.LogWarning($"{LogPrefix} Could not load SE_Rested effect object. Rested changes won't work");
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class ModifyItemStackSize
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (!EnableItemChanges.Value)
                return;

            else
            {
                foreach (ItemDrop.ItemData.ItemType type in (ItemDrop.ItemData.ItemType[])Enum.GetValues(typeof(ItemDrop.ItemData.ItemType)))
                {
                    foreach (ItemDrop item in __instance.GetAllItems(type, ""))
                    {
                        if (item.m_itemData.m_shared.m_name.StartsWith($"$item_"))
                        {
                            if (item.m_itemData.m_shared.m_maxStackSize > 1)
                            {
                                if (!_itemChanges.TryGetValue(item.m_itemData.m_shared.m_name, out ConfigEntry<int> config))
                                {
                                    ItemManager manager = new ItemManager(item);
                                    manager.SetStackSize(item);
                                    configSync.AddConfigEntry(manager.ItemMaxStackSize);
                                    _itemChanges.Add(item.m_itemData.m_shared.m_name, manager.ItemMaxStackSize);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateFood")]
    public static class FoodHealthRegenPatches
    {
        private static void Prefix(Player __instance, ref float ___m_foodRegenTimer)
        {
            if (EnableVitality.Value)
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
    }

    [HarmonyPatch(typeof(Player), "SetMaxHealth")]
    public static class MaxHealth
    {
        private static void Prefix(Player __instance, ref float health, bool flashBar)
        {
            if (EnableVitality.Value)
            {
                float configValue = MaxBaseHP.Value;
                health += vitalitySkillFactor * configValue;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "SetMaxStamina")]
    public static class MaxStamina
    {
        private static void Prefix(Player __instance, ref float stamina, bool flashBar)
        {
            if (EnableEndurance.Value)
            {
                float configValue = MaxBaseStamina.Value;
                stamina += enduranceSkillFactor * configValue;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")]
    public static class EnduranceWalkSpeed
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (EnableEndurance.Value)
            {
                float configValue = BaseWalkSpeed.Value;
                __result += enduranceSkillFactor * configValue / 100;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "GetRunSpeedFactor")]
    public static class EnduracneRunSpeed
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (EnableEndurance.Value)
            {
                float configValue = BaseRunSpeed.Value;
                __result += enduranceSkillFactor * configValue / 100;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateStats", new Type[] { typeof(float) })]
    public static class LevelSkill
    {
        private static void Prefix(Player __instance, float dt)
        {
            if (EnableEndurance.Value)
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
        }

        private static float stamina;
        private static float runSwimSkill = 0.0f;
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

            if (EnableVitality.Value)
                ApplyVitalitySkillFactors(__instance);
            if (EnableEndurance.Value)
                ApplyEnduranceSkillFactors(__instance);

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

        public static void ApplyVitalitySkillFactors(Player __instance)
        {
            vitalitySkillFactor = __instance.GetSkillFactor(VitalitySkill);
        }

        public static void ApplyEnduranceSkillFactors(Player __instance)
        {
            enduranceSkillFactor = __instance.GetSkillFactor(EnduranceSkill);
            string playerName = __instance.GetPlayerName();
            //QOLLogger.LogInfo($"{LogPrefix} Player: {playerName} has Vitality Skill Factor: {vitalitySkillFactor} and Endurance Skill Factor {enduranceSkillFactor} applied");
            float config;

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

    [HarmonyPatch(typeof(Player), "OnSkillLevelup")]
    public static class LevelupSkillApplyValues
    {
        private static void Postfix(Player __instance, Skills.SkillType skill, float level)
        {
            if (EnableEndurance.Value)
            {
                if (skill == EnduranceSkill)
                {
                    AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(__instance);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "OnJump")]
    public static class EnduranceSkillOnJump
    {
        private static void Prefix(Player __instance)
        {
            if (EnableEndurance.Value)
            {
                float num = __instance.m_jumpStaminaUsage - __instance.m_jumpStaminaUsage * __instance.GetEquipmentJumpStaminaModifier();
                bool b = __instance.HaveStamina(num * Game.m_moveStaminaRate);
                if (b)
                    IncreaseEndurance(__instance, 0.14f);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "Awake")]
    public static class AddPlayerToCache
    {
        private static void Postfix(Player __instance)
        {
            if (EnableRestedChanges.Value)
                _players.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDestroy")]
    public static class RemovePlayerFromCache
    {
        private static void Prefix(Player __instance)
        {
            if (EnableRestedChanges.Value)
                _players.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "DamageArea")]
    public static class MiningBigRocks
    {
        private static void Prefix(MineRock5 __instance, HitData hit, float __state)
        {
            if (EnableVitality.Value)
            {
                Player player = Player.m_localPlayer;
                if ((player == null) || !(player.GetZDOID() == hit.m_attacker) || hit.m_skill != Skills.SkillType.Pickaxes)
                    return;

                float skillGain = 0.04f + hit.m_damage.m_pickaxe * 0.00075f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
            }
        }
    }

    [HarmonyPatch(typeof(Destructible), "Damage")]
    public static class DestroySmallStuff
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            if (EnableVitality.Value)
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
    }

    [HarmonyPatch(typeof(TreeBase), "Damage")]
    public static class WoodCutting
    {
        private static void Prefix(TreeBase __instance, HitData hit)
        {
            if (EnableVitality.Value)
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
    }

    [HarmonyPatch(typeof(TreeLog), "Damage")]
    public static class WoodCutting_II
    {
        private static void Prefix(TreeLog __instance, HitData hit)
        {
            if (EnableVitality.Value)
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
    }

    [HarmonyPatch(typeof(Terminal), "InputText")]
    private static class InputText_Patch
    {
        private static void Postfix(Terminal __instance)
        {
            string text = __instance.m_input.text;
            if (text.ToLower().Contains("raiseskill vitality") && (Player.m_localPlayer != null) && EnableVitality.Value)
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(Player.m_localPlayer);
            if (text.ToLower().Contains("raiseskill endurance") && (Player.m_localPlayer != null) && EnableEndurance.Value)
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

    public static void IncreaseVitality(Player player, float value)
    {
        player.RaiseSkill(VitalitySkill, value * VitalitySkillGainMultiplier.Value);
    }

    public static void IncreaseEndurance(Player player, float value)
    {
        player.RaiseSkill(EnduranceSkill, value * EnduranceSkillGainMultiplier.Value);
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

    // For some reason when loaded on a dedicated server, the stack size doesn't take effect for the client.
    // This "fix" doesn't apply to any previously created item stacks. A new stack must be created for the effect to take place. A hack but it works I guess.
    private static void DedicatedServerPatches()
    {
        foreach (ItemDrop.ItemData.ItemType type in (ItemDrop.ItemData.ItemType[])Enum.GetValues(typeof(ItemDrop.ItemData.ItemType)))
        {
            foreach (ItemDrop item in ObjectDB.instance.GetAllItems(type, ""))
            {
                if (item.m_itemData.m_shared.m_name.StartsWith($"$item_"))
                {
                    if (item.m_itemData.m_shared.m_maxStackSize > 1)
                    {
                        if (_itemChanges.TryGetValue(item.m_itemData.m_shared.m_name, out ConfigEntry<int> config))
                        {
                            if (config.Value > 0)
                            {
                                item.m_itemData.m_shared.m_maxStackSize = config.Value;
                            }
                        }
                    }
                }
            }
        }

        SE_Rested effect = GetRestedEffect();
        if (effect != null)
        {
            effect.m_baseTTL = BaseRestTime.Value;
            effect.m_TTLPerComfortLevel = RestTimePerComfortLevel.Value;
        }
        else
            QOLLogger.LogWarning($"{LogPrefix} Cannot get SE_Rested effect! Patch cannot apply");
    }

    private enum Toggle
    {
        On = 1,
        Off = 0
    }
}