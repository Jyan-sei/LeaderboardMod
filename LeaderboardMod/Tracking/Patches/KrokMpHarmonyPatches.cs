using System;
using System.Reflection;
using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Tracking;
using UnityEngine;

using LeaderboardMod.Session;

namespace LeaderboardMod.Tracking.Patches;

/// <summary>
/// harmony patches that need krokmp types. only applied if that dll is loaded.
/// </summary>
internal static class KrokMpHarmonyPatches
{
	private static bool _applied;
	private static bool _gaveUp;

	internal static void Begin(Harmony harmony)
	{
		if (_applied || _gaveUp || harmony == null)
			return;

		KrokMpOptional.Resolve();
		if (!KrokMpOptional.IsPresent)
		{
			_gaveUp = true;
			return;
		}

		TryPatchCraftMp(harmony);
		TryPatchMedicalMp(harmony);
		_applied = true;
	}

	private static void TryPatchCraftMp(Harmony harmony)
	{
		Type patchType = AccessTools.TypeByName("KrokoshaCasualtiesMP.PlayerCamera_TryCraft_MultiplayerPatch");
		MethodInfo target = patchType != null ? AccessTools.Method(patchType, "Recipe_TryMake") : null;
		if (target == null)
		{
			LbLog.Warn("KrokMP:Patch", "Recipe_TryMake MP patch target not found");
			return;
		}

		harmony.Patch(
			target,
			postfix: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(CraftMpTryMakePostfix)));
		LbLog.Step("KrokMP:Patch", "Patched Recipe_TryMake (MP craft tracking)");
	}

	private static void CraftMpTryMakePostfix(Body crafter, Recipe craftingtable, bool __result)
	{
		if (!__result)
			return;
		if (!RunEventTracker.TryGetSession(out _))
			return;
		if (!KrokMpOptional.IsBodyLocal(crafter))
			return;
		RunEventTracker.RecordCraft(craftingtable);
	}

	private static void TryPatchMedicalMp(Harmony harmony)
	{
		Type forceApplyType = AccessTools.TypeByName("KrokoshaCasualtiesMP.PlayerCamera_ApplyWoundItem_MultiplayerPatch");
		MethodInfo forceApply = forceApplyType != null
			? AccessTools.Method(forceApplyType, "ForceApplyWoundItem")
			: null;
		if (forceApply != null)
		{
			harmony.Patch(
				forceApply,
				prefix: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(MedicalForceApplyPrefix)),
				finalizer: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(MedicalForceApplyFinalizer)));
		}

		MethodInfo betterUse = AccessTools.Method(AccessTools.TypeByName("KrokoshaCasualtiesMP.ItemSync"), "BetterUseItem");
		if (betterUse != null)
		{
			harmony.Patch(
				betterUse,
				prefix: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(MedicalBetterUsePrefix)),
				postfix: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(MedicalBetterUsePostfix)));
		}

		Type useItemPatch = AccessTools.TypeByName("KrokoshaCasualtiesMP.Body_UseItem_MultiplayerPatch");
		MethodInfo serverRequest = useItemPatch != null
			? AccessTools.Method(useItemPatch, "Server_RequestUseItem")
			: null;
		if (serverRequest != null)
		{
			harmony.Patch(
				serverRequest,
				prefix: new HarmonyMethod(typeof(KrokMpHarmonyPatches), nameof(MedicalServerRequestPrefix)));
		}

		LbLog.Step("KrokMP:Patch", "Patched MP medical hooks");
	}

	private static void MedicalForceApplyPrefix(Body healer, Limb limb, Item item)
	{
		MedicalUseContext.Set(healer, limb, item);
	}

	private static Exception MedicalForceApplyFinalizer(Exception __exception)
	{
		MedicalUseContext.ClearWoundApply();
		return __exception;
	}

	private static void MedicalBetterUsePrefix(Body body, Item item, ref Body __state)
	{
		__state = MedicalUseContext.Healer;
		if (MedicalUseContext.Healer == null && PlayerCamera.main != null)
			MedicalUseContext.Healer = PlayerCamera.main.body;
		else if (MedicalUseContext.Healer == null)
			MedicalUseContext.Healer = body;
	}

	private static void MedicalBetterUsePostfix(Body body, Item item, Body __state)
	{
		MedicalUseContext.Healer = __state;
	}

	private static void MedicalServerRequestPrefix(object[] __args)
	{
		if (__args == null || __args.Length == 0 || __args[0] == null)
			return;
		if (KrokMpOptional.TryGetNetPlayerBody(__args[0], out Body body))
			MedicalUseContext.Healer = body;
	}
}
