using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pipakin.SkillInjectorMod;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace QOL;

[BepInPlugin(pluginGUID, pluginName, pluginVersion)]
[BepInDependency("com.pipakin.SkillInjectorMod", BepInDependency.DependencyFlags.HardDependency)]
public class QOL : BaseUnityPlugin
{
    const string pluginGUID = "fortis.mods.qolmod";
    const string pluginName = "QOLChanges";
    const string pluginVersion = "0.0.1";

    private static QOL _instance;
    private static readonly int vitalitySkillId = 638;
    private static readonly int enduranceSkillId = 639;
    private static readonly Skills.SkillType VitalitySkill = (Skills.SkillType)vitalitySkillId;
    private static readonly Skills.SkillType EnduranceSkill = (Skills.SkillType)enduranceSkillId;

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
    private static ConfigEntry<double> MaxRestedTime;

    private static float vitalitySkillFactor;
    private static float enduranceSkillFactor;

    private Harmony _harmony;

    private static readonly ManualLogSource QOLLogger = BepInEx.Logging.Logger.CreateLogSource(pluginName);
    private static readonly string LogPrefix = $"[QOLChanges v{pluginVersion}]";

    public void Awake()
    {
        QOLLogger.LogInfo($"{LogPrefix} Initializing Plugin");
        _instance = this;
        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginGUID);
        BindConfigs();
        if (!EnableMod.Value)
        {
            QOLLogger.LogInfo($"{LogPrefix} Mod Disabled, Stopping Initialization");
            return;
        }
        InjectSkills();
    }

    private void BindConfigs()
    {
        // General Binds
        EnableMod = Config.Bind("General", "EnableMod", true, "Enable The Mod. Default Is true");

        // Vitality Configurations
        EnableVitality = Config.Bind("Vitality Configurations", "EnableVitality", true, new ConfigDescription("Enables the Vitality skill"));
        MaxBaseHP = Config.Bind("Vitality Configurations", "MaxBaseHP", 150, new ConfigDescription("The max base HP when Vitality skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        HealthRegen = Config.Bind("Vitality Configurations", "HealthRegeneration", 100f, new ConfigDescription("Increase of base health regeneration in percent at Vitality skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalitySkillGainMultiplier = Config.Bind("Vitality Configurations", "VitalitySkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Vitality skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));
        VitalityWorkSkillGainMultiplier = Config.Bind("Vitality Configurations", "VitalityWorkSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast skill is gained via damage of your tools", new AcceptableValueRange<float>(0.01f, 999f)));


        // Endurance Configurations
        EnableEndurance = Config.Bind("Endurance Configurations", "EnableEndurance", true, new ConfigDescription("Enables the Endurance skill"));
        MaxBaseStamina = Config.Bind("Endurance Configurations", "MaxBaseStamina", 200, new ConfigDescription("The max base stamina when Endurance skill is level 100.", new AcceptableValueRange<int>(1, 999)));
        BaseWalkSpeed = Config.Bind("Endurance Configurations", "BaseWalkSpeedMultiplier", 20f, new ConfigDescription("Increase of base walking speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseRunSpeed = Config.Bind("Endurance Configurations", "BaseRunSpeedMultiplier", 20f, new ConfigDescription("Increase of base running speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        BaseSwimSpeed = Config.Bind("Endurance Configurations", "BaseSwimSpeedMultiplier", 50f, new ConfigDescription("Increase of base swimming speed in percent at Endurance level 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaRegen = Config.Bind("Endurance Configuations", "StaminaRegen", 72f, new ConfigDescription("Increase of base stamina regeneration in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 999f)));
        StaminaDelay = Config.Bind("Endurance Configurations", "StaminaDelay", 50f, new ConfigDescription("Decrease the delay for stamina regeneration after usage in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaJump = Config.Bind("Endurance Configurations", "StaminaJump", 25f, new ConfigDescription("Decrease of stamina cost per jump in percent at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        StaminaSwim = Config.Bind("Endurance Configurations", "StaminaSwim", 33f, new ConfigDescription("Decrease of stamina cost while swimming at Endurance skill 100.", new AcceptableValueRange<float>(0.01f, 100f)));
        MaxBaseCarryWeight = Config.Bind("Endurance Configurations", "MaxCarryWeight", 450, new ConfigDescription("Max carry weight at Endurance skill 100.", new AcceptableValueRange<int>(300, 999)));
        EnduranceSkillGainMultiplier = Config.Bind("Endurance Configurations", "EnduranceSkillGain", 1f, new ConfigDescription("Multiplier for determining how fast Endurance skill is gained. Higher number means greater increases in skill gain", new AcceptableValueRange<float>(0.01f, 999f)));

        // Player Configurations
        EnableRestedChanges = Config.Bind("Status Effect Changes", "EnableRestedChanges", true, "Enables changing rested to scale more based on comfort level");
        MaxRestedTime = Config.Bind("Status Effect Changes", "MaxRestedTime", 60.0, "The time in minutes the max comfort level gives. Max comfort is 19. Changing this will change the scaling of rested time per level. Default is 60.0");
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

                    QOLLogger.LogInfo($"{LogPrefix} Health Regeneration is set to {___m_foodRegenTimer} (out of 10)");
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
                QOLLogger.LogInfo($"{LogPrefix} SetMaxHealth Prefix fired");
                float configValue = MaxBaseHP.Value;
                health += vitalitySkillFactor * configValue;
                QOLLogger.LogInfo($"{LogPrefix} Health Increased By {vitalitySkillFactor * configValue}");
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
                QOLLogger.LogInfo($"{LogPrefix} SetMaxStamina Prefix fired");
                float configValue = MaxBaseStamina.Value;
                stamina += enduranceSkillFactor * configValue;
                QOLLogger.LogInfo($"{LogPrefix} Stamina increased by {enduranceSkillFactor * configValue}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")]
    public static class EnduranceWalkSpeed
    {
        private static void PostFix(Player __instance, ref float __result)
        {
            if (EnableEndurance.Value)
            {
                QOLLogger.LogInfo($"{LogPrefix} GetJogSpeedFactor fired");
                float configValue = BaseWalkSpeed.Value;
                __result += enduranceSkillFactor * configValue;
                QOLLogger.LogInfo($"{LogPrefix} Increased base walk speed by {enduranceSkillFactor * configValue}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "GetRunSpeedFactor")]
    public static class EnduracneRunSpeed
    {
        private static void PostFix(Player __instance, ref float __result)
        {
            if (EnableEndurance.Value)
            {
                QOLLogger.LogInfo($"{LogPrefix} GetRunSpeedFactor fired");
                float configValue = BaseRunSpeed.Value;
                __result += enduranceSkillFactor * configValue;
                QOLLogger.LogInfo($"{LogPrefix} Increased base run speed by {enduranceSkillFactor * configValue}");
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
            QOLLogger.LogInfo($"{LogPrefix} Load Postfix fired");
            AttributeOverwriteOnLoad.m_maxCarryWeight = __instance.m_maxCarryWeight;
            AttributeOverwriteOnLoad.m_jumpStaminaUse = __instance.m_jumpStaminaUsage;
            AttributeOverwriteOnLoad.m_staminaRegenDelay = __instance.m_staminaRegenDelay;
            AttributeOverwriteOnLoad.m_staminaRegen = __instance.m_staminaRegen;
            AttributeOverwriteOnLoad.m_swimStaminaDrainMinSkill = __instance.m_swimStaminaDrainMinSkill;
            AttributeOverwriteOnLoad.m_swimStaminaDrainMaxSkill = __instance.m_swimStaminaDrainMaxSkill;
            AttributeOverwriteOnLoad.m_swimSpeed = __instance.m_swimSpeed;
            AttributeOverwriteOnLoad.m_swimTurnSpeed = __instance.m_swimTurnSpeed;

            if (EnableEndurance.Value)
                ApplyEnduranceSkillFactors(__instance);
        }

        public static void ApplyEnduranceSkillFactors(Player __instance)
        {
            enduranceSkillFactor = __instance.GetSkillFactor(EnduranceSkill);
            string playerName = __instance.GetPlayerName();
            QOLLogger.LogInfo($"{LogPrefix} Player: {playerName} has Vitality Skill Factor: {vitalitySkillFactor} and Endurance Skill Factor {enduranceSkillFactor} applied");
            float config;

            config = MaxBaseCarryWeight.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Base Carry Weight For Player: {playerName} from {__instance.m_maxCarryWeight}");
            __instance.m_maxCarryWeight = AttributeOverwriteOnLoad.m_maxCarryWeight + enduranceSkillFactor * config;
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_maxCarryWeight}");

            config = StaminaJump.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Base Jump Stamina Cost For Player: {playerName} from {__instance.m_jumpStaminaUsage}");
            __instance.m_jumpStaminaUsage = AttributeOverwriteOnLoad.m_jumpStaminaUse * (1 - enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_jumpStaminaUsage}");

            config = StaminaDelay.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Regen Delay For Player: {playerName} from {__instance.m_staminaRegenDelay}");
            __instance.m_staminaRegenDelay = AttributeOverwriteOnLoad.m_staminaRegenDelay * (1 - enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_staminaRegenDelay}");

            config = StaminaRegen.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Regen For Player: {playerName} from {__instance.m_staminaRegen}");
            __instance.m_staminaRegen = AttributeOverwriteOnLoad.m_staminaRegen * (1 + enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_staminaRegen}");

            config = StaminaSwim.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Swim Min Cost For Player {playerName} from {__instance.m_swimStaminaDrainMinSkill}");
            __instance.m_swimStaminaDrainMinSkill = AttributeOverwriteOnLoad.m_swimStaminaDrainMinSkill * (1 - enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimStaminaDrainMinSkill}");

            QOLLogger.LogInfo($"{LogPrefix} Changing Stamina Swim Max Cost For Player {playerName} from {__instance.m_swimStaminaDrainMaxSkill}");
            __instance.m_swimStaminaDrainMaxSkill = AttributeOverwriteOnLoad.m_swimStaminaDrainMaxSkill * (1 - enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimStaminaDrainMaxSkill}");

            config = BaseSwimSpeed.Value;
            QOLLogger.LogInfo($"{LogPrefix} Changing Base Swim Speed For Player {playerName} from {__instance.m_swimSpeed}");
            __instance.m_swimSpeed = AttributeOverwriteOnLoad.m_swimSpeed * (1 + enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimSpeed}");

            QOLLogger.LogInfo($"{LogPrefix} Changing Base Swim Turn Speed For Player {playerName} from {__instance.m_swimTurnSpeed}");
            __instance.m_swimTurnSpeed = AttributeOverwriteOnLoad.m_swimTurnSpeed * (1 + enduranceSkillFactor * config / 100);
            QOLLogger.LogInfo($"{LogPrefix} to {__instance.m_swimTurnSpeed}");
        }
    }

    [HarmonyPatch(typeof(Player), "OnSkillLevelup")]
    public static class LevelupSkillApplyValues
    {
        private static void Postfix(Player __instance, Skills.SkillType skill, float level)
        {
            if (EnableEndurance.Value)
            {
                QOLLogger.LogInfo($"{LogPrefix} OnSkillLevelup Postfix fired");
                if (skill == EnduranceSkill)
                {
                    QOLLogger.LogInfo($"{LogPrefix} OnSkillLevelup Applying Skill Factors");
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
                QOLLogger.LogInfo($"{LogPrefix} OnJump Prefix fired");
                float num = __instance.m_jumpStaminaUsage - __instance.m_jumpStaminaUsage * __instance.GetEquipmentJumpStaminaModifier();
                bool b = __instance.HaveStamina(num * Game.m_moveStaminaRate);
                if (b)
                    IncreaseEndurance(__instance, 0.14f);
            }
        }
    }

    [HarmonyPatch(typeof(MineRock5), "DamageArea")]
    public static class MiningBigRocks
    {
        private static void Prefix(MineRock5 __instance, HitData hit, float __state)
        {
            if (EnableVitality.Value)
            {
                QOLLogger.LogInfo($"{LogPrefix} DamageArea prefix fired");
                Player player = Player.m_localPlayer;
                if ((player == null) || !(player.GetZDOID() == hit.m_attacker) || hit.m_skill != Skills.SkillType.Pickaxes)
                    return;

                float skillGain = 0.04f + hit.m_damage.m_pickaxe * 0.00075f * VitalityWorkSkillGainMultiplier.Value;
                IncreaseVitality(player, skillGain);
                QOLLogger.LogInfo($"{LogPrefix} Player: {player.GetPlayerName()} Gained: {skillGain} for Vitality Skill!");
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
                QOLLogger.LogInfo($"{LogPrefix} Damage prefix fired");
                Player player = Player.m_localPlayer;
                if ((player is null) || !(player.GetZDOID() == hit.m_attacker))
                    return;

                if (__instance.name.ToLower().Contains("rock") && hit.m_skill == Skills.SkillType.Pickaxes)
                {
                    float skillGain = 0.1f + hit.m_damage.m_pickaxe * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                    IncreaseVitality(player, skillGain);
                    QOLLogger.LogInfo($"{LogPrefix} Player: {player.GetPlayerName()} Gained: {skillGain} for Vitality Skill!");
                }
                else if (hit.m_skill == Skills.SkillType.WoodCutting)
                {
                    float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                    IncreaseVitality(player, skillGain);
                    QOLLogger.LogInfo($"{LogPrefix} Player: {player.GetPlayerName()} Gained: {skillGain} for Vitality Skill!");
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
                QOLLogger.LogInfo($"{LogPrefix} Damage Tree base prefix fired");
                Player player = Player.m_localPlayer;
                if ((player is null) || !(player.GetZDOID() == hit.m_attacker))
                    return;

                if (hit.m_skill == Skills.SkillType.WoodCutting && hit.m_toolTier >= __instance.m_minToolTier)
                {
                    float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                    IncreaseVitality(player, skillGain);
                    QOLLogger.LogInfo($"{LogPrefix} Player: {player.GetPlayerName()} Gained: {skillGain} for Vitality Skill!");
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
                QOLLogger.LogInfo($"{LogPrefix} Damage Log tree prefix fired");
                Player player = Player.m_localPlayer;
                if ((player == null) || !(player.GetZDOID() == hit.m_attacker))
                {
                    return;
                }
                if (hit.m_skill == Skills.SkillType.WoodCutting && hit.m_toolTier >= __instance.m_minToolTier)
                {
                    float skillGain = 0.1f + hit.m_damage.m_chop * 0.001f * VitalityWorkSkillGainMultiplier.Value;
                    IncreaseVitality(player, skillGain);
                    QOLLogger.LogInfo($"{LogPrefix} Player: {player.GetPlayerName()} Gained: {skillGain} for Vitality Skill!");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Terminal), "InputText")]
    private static class InputText_Patch
    {
        private static void Postfix(Terminal __instance)
        {
            QOLLogger.LogInfo($"{LogPrefix} InputText Postfix fired");
            string text = __instance.m_input.text;
            if (text.ToLower().Contains("raiseskill vitality") && (Player.m_localPlayer != null) && EnableVitality.Value)
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(Player.m_localPlayer);
            if (text.ToLower().Contains("raiseskill endurance") && (Player.m_localPlayer != null) && EnableEndurance.Value)
                AttributeOverwriteOnLoad.ApplyEnduranceSkillFactors(Player.m_localPlayer);
        }
        private static bool Prefix(Terminal __instance)
        {
            QOLLogger.LogInfo($"{LogPrefix} InputText Prefix fired");
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
}