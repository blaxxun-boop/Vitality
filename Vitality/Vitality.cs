using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using SkillManager;

namespace Vitality;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("randyknapp.mods.epicloot")]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class Vitality : BaseUnityPlugin
{
	private const string ModName = "Vitality";
	private const string ModVersion = "1.1.3";
	private const string ModGUID = "org.bepinex.plugins.vitality";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<float> bonusHPMultiplier = null!;
	private static ConfigEntry<int> bonusHPRegenLevel = null!;
	private static ConfigEntry<float> bonusHPRegen = null!;
	private static ConfigEntry<int> foodBonusLevel = null!;
	private static ConfigEntry<int> foodBonus = null!;
	private static ConfigEntry<int> skillLossBukePerry = null!;
	private static ConfigEntry<float> experienceGainedFactor = null!;
	private static ConfigEntry<int> experienceLoss = null!;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0,
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public bool? ShowRangeAsPercent;
	}

	private static Skill vitality = null!;

	public void Awake()
	{
		vitality = new Skill("Vitality", "vitality-icon.png")
		{
			Configurable = false,
		};
		vitality.Description.English("Increases your base health.");
		vitality.Name.German("Vitalität");
		vitality.Description.German("Erhöht deine Basis-Lebenspunkte.");

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		bonusHPMultiplier = config("2 - Vitality", "Base Health Multiplier", 2f, new ConfigDescription("Multiplier for your base health at skill level 100.", new AcceptableValueRange<float>(1f, 20f)));
		bonusHPRegenLevel = config("2 - Vitality", "Minimum Level for Bonus HP Regen", 30, new ConfigDescription("Skill level required to gain bonus HP regen. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		bonusHPRegen = config("2 - Vitality", "Bonus HP Regen", 1f, new ConfigDescription("Bonus HP regen gained once the level requirement is met.", new AcceptableValueRange<float>(0.5f, 10f)));
		foodBonusLevel = config("2 - Vitality", "Minimum Level for Food Bonus", 50, new ConfigDescription("Skill level required to gain bonus HP from food. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		foodBonus = config("2 - Vitality", "Food Bonus", 10, new ConfigDescription("Bonus HP gained from food once the level requirement is met.", new AcceptableValueRange<int>(0, 100)));
		skillLossBukePerry = config("2 - Vitality", "Skill Loss for Bukeperries Bonus", 1, new ConfigDescription("Skill to lose, if you eat a Bukeperry.", new AcceptableValueRange<int>(0, 100)));
		experienceGainedFactor = config("3 - Other", "Skill Experience Gain Factor", 1f, new ConfigDescription("Factor for experience gained for the vitality skill.", new AcceptableValueRange<float>(0.01f, 5f)));
		experienceGainedFactor.SettingChanged += (_, _) => vitality.SkillGainFactor = experienceGainedFactor.Value;
		vitality.SkillGainFactor = experienceGainedFactor.Value;
		experienceLoss = config("3 - Other", "Skill Experience Loss", 5, new ConfigDescription("How much experience to lose in the vitality skill on death.", new AcceptableValueRange<int>(0, 100)));
		experienceLoss.SettingChanged += (_, _) => vitality.SkillLoss = experienceLoss.Value;
		vitality.SkillLoss = experienceLoss.Value;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
	private class PlayerUseMethodForBaseHP
	{
		[UsedImplicitly]
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo baseHpField = AccessTools.DeclaredField(typeof(Player), nameof(Player.m_baseHP));
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ldfld && instruction.OperandIs(baseHpField))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Player), nameof(Player.GetBaseFoodHP)));
				}
				else
				{
					yield return instruction;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
	private class IncreaseBaseHealth
	{
		[UsedImplicitly]
		[HarmonyPriority(Priority.LowerThanNormal)]
		private static void Postfix(Player __instance, ref float __result)
		{
			__result *= 1 + (bonusHPMultiplier.Value - 1) * __instance.GetSkillFactor("Vitality");
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
	private static class PunishBukeperries
	{
		private static void Postfix(Player __instance, ItemDrop.ItemData item)
		{
			if (skillLossBukePerry.Value > 0 && item.m_shared.m_name == "$item_pukeberries")
			{
				__instance.LowerSkill("Vitality", skillLossBukePerry.Value / 100f);
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
	private class AddSkillGain
	{
		[UsedImplicitly]
		private static void Postfix(Player __instance, ItemDrop.ItemData item, ref bool __result)
		{
			if (__result)
			{
				__instance.RaiseSkill("Vitality", (float)Math.Sqrt(item.m_shared.m_food));
			}
		}
	}

	[HarmonyPatch]
	private static class IncreaseFoodHealth
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(Player), nameof(Player.EatFood)),
			AccessTools.DeclaredMethod(typeof(Player), nameof(Player.UpdateFood)),
		};

		private static float ManipulateFoodHealth(float health) => health * (1 + (foodBonusLevel.Value > 0 && foodBonusLevel.Value <= Player.m_localPlayer.GetSkillFactor("Vitality") * 100f ? foodBonus.Value / 100f : 0));

		private static float tmpPtr;

		private static unsafe float* ManipulateFoodHealthBoxed(float* health)
		{
			tmpPtr = ManipulateFoodHealth(*health);
			fixed (float* addr = &tmpPtr) return addr;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo m_food = AccessTools.DeclaredField(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_food));
			foreach (CodeInstruction instruction in instructions)
			{
				yield return instruction;
				if ((instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldflda) && instruction.OperandIs(m_food))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(IncreaseFoodHealth), instruction.opcode == OpCodes.Ldflda ? nameof(ManipulateFoodHealthBoxed) : nameof(ManipulateFoodHealth)));
				}
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
	private static class IncreaseHealthRegeneration
	{
		private static float GetRegIncrease() => bonusHPRegenLevel.Value > 0 && bonusHPRegenLevel.Value <= Player.m_localPlayer.GetSkillFactor("Vitality") * 100f ? bonusHPRegen.Value : 0f;

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> instructionList = instructions.ToList();
			yield return instructionList[0];
			for (int i = 1; i < instructionList.Count; ++i)
			{
				yield return instructionList[i];
				if (instructionList[i - 1].opcode == OpCodes.Ldc_R4 && instructionList[i - 1].OperandIs(0f) && instructionList[i].IsStloc())
				{
					yield return new CodeInstruction(OpCodes.Ldloc_S, instructionList[i].operand);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(IncreaseHealthRegeneration), nameof(GetRegIncrease)));
					yield return new CodeInstruction(OpCodes.Add);
					yield return instructionList[i];
				}
			}
		}
	}
}
