using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using SkillManager;

namespace Vitality;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("randyknapp.mods.epicloot")]
public class Vitality : BaseUnityPlugin
{
	private const string ModName = "Vitality";
	private const string ModVersion = "1.0.1";
	private const string ModGUID = "org.bepinex.plugins.vitality";

	public void Awake()
	{
		Skill vitality = new("Vitality", "vitality-icon.png")
		{
			SkillGainFactor = 2f,
			Configurable = true
		};
		vitality.Description.English("Increases your base health.");
		vitality.Name.German("Vitalität");
		vitality.Description.German("Erhöht deine Basis-Lebenspunkte.");

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

	[HarmonyPriority(Priority.High)]
	[HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
	private class IncreaseBaseHealth
	{
		[UsedImplicitly]
		private static void Postfix(Player __instance, ref float __result)
		{
			__result *= 1 + __instance.GetSkillFactor("Vitality");
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
}
