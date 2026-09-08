using System.Reflection;
using HarmonyLib;
using LeaderboardMod.Session;
using LeaderboardMod.Tracking;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ApplyWoundItem))]
internal static class MedicalApplyWoundItemPatch
{
	[HarmonyPrefix]
	private static void Prefix(PlayerCamera __instance, Item item)
	{
		MedicalUseContext.Set(__instance.body, __instance.selectedLimb, item);
	}

	[HarmonyPostfix]
	private static void Postfix(PlayerCamera __instance, Item item)
	{
		if (!MedicalUseTracker.IsMedicalItem(item))
		{
			MedicalUseContext.ClearWoundApply();
			return;
		}

		if (MinigameBase.main != null && MinigameBase.main.currentMinigame != null)
		{
			MedicalUseContext.ClearWoundApply();
			return;
		}

		if (item.TryGetComponent<WaterContainerItem>(out _))
		{
			MedicalUseContext.ClearWoundApply();
			return;
		}

		if (item.Stats.usableOnLimb)
			MedicalUseTracker.TryRecord(__instance.body, __instance.selectedLimb.body, item);

		MedicalUseContext.ClearWoundApply();
	}
}

[HarmonyPatch(typeof(Body), nameof(Body.UseItem))]
internal static class MedicalBodyUseItemPatch
{
	[HarmonyPrefix]
	private static void Prefix(Body __instance)
	{
		if (PlayerCamera.main != null && __instance == PlayerCamera.main.body)
			MedicalUseContext.Healer = __instance;
	}
}

[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.StartMinigame))]
internal static class MedicalStartMinigamePatch
{
	[HarmonyPostfix]
	private static void Postfix(Minigame minigame, Item item)
	{
		if (!MedicalUseTracker.IsMedicalItem(item))
			return;

		Body healer = KrokMpOptional.GetLastWoundItemUser();
		if (healer == null && PlayerCamera.main != null)
			healer = PlayerCamera.main.body;

		Limb limb = GetMinigameLimb(minigame);
		MedicalUseContext.Set(healer, limb, item);

		if (minigame is BandageMinigame)
			MedicalUseContext.BeginBandageSession();
		else if (minigame is SyringeMinigame)
			MedicalUseContext.BeginInjectionSession(item);
	}

	private static Limb GetMinigameLimb(Minigame minigame)
	{
		if (minigame is BandageMinigame bandage)
			return bandage.limb;
		if (minigame is SyringeMinigame)
			return Traverse.Create(minigame).Field("limb").GetValue<Limb>();
		return MedicalUseContext.TargetLimb;
	}
}

[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.EndMinigame))]
internal static class MedicalEndMinigamePatch
{
	[HarmonyPostfix]
	private static void Postfix()
	{
		MedicalUseContext.EndMinigameSessions();
	}
}

[HarmonyPatch(typeof(BandageMinigame), "DoBandageAction")]
internal static class MedicalBandageActionPatch
{
	[HarmonyPostfix]
	private static void Postfix(BandageMinigame __instance)
	{
		if (!MedicalUseContext.BandageSessionActive || MedicalUseContext.BandageSessionRecorded)
			return;
		Item item = MinigameBase.main != null ? MinigameBase.main.currentItem : null;
		if (!MedicalUseTracker.IsMedicalItem(item))
			return;

		Body healer = MedicalUseContext.Healer ?? PlayerCamera.main?.body;
		MedicalUseTracker.TryRecord(healer, __instance.limb.body, item);
		MedicalUseContext.BandageSessionRecorded = true;
	}
}

[HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.ApplyToLimb))]
internal static class MedicalApplyToLimbPatch
{
	[HarmonyPrefix]
	private static void Prefix(WaterContainerItem __instance, ref float __state)
	{
		__state = __instance.CurrentTotal;
	}

	[HarmonyPostfix]
	private static void Postfix(WaterContainerItem __instance, Limb limb, float __state)
	{
		if (__instance.CurrentTotal >= __state)
			return;
		Item item = __instance.GetComponent<Item>();
		if (!MedicalUseTracker.IsMedicalItem(item))
			return;

		Body healer = MedicalUseContext.Healer ?? PlayerCamera.main?.body;
		MedicalUseTracker.TryRecord(healer, limb.body, item);
	}
}

[HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.Inject))]
internal static class MedicalInjectPatch
{
	[HarmonyPrefix]
	private static void Prefix(WaterContainerItem __instance, Limb limb, float amount, ref float __state)
	{
		__state = __instance.CurrentTotal;
		if (MedicalUseContext.Item == null && __instance.TryGetComponent<Item>(out var item))
			MedicalUseContext.Item = item;
		if (MedicalUseContext.TargetLimb == null)
			MedicalUseContext.TargetLimb = limb;
	}

	[HarmonyPostfix]
	private static void Postfix(WaterContainerItem __instance, Limb limb, float amount, float __state)
	{
		if (__instance.CurrentTotal >= __state)
			return;
		float drained = __state - __instance.CurrentTotal;
		if (drained <= 0f)
			return;
		MedicalUseTracker.TryRecordInjection(drained, __instance, limb);
	}
}

[HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.Drink))]
internal static class MedicalDrinkPatch
{
	[HarmonyPrefix]
	private static void Prefix(WaterContainerItem __instance, ref float __state)
	{
		__state = __instance.CurrentTotal;
	}

	[HarmonyPostfix]
	private static void Postfix(WaterContainerItem __instance, Body body, float __state)
	{
		if (__instance.CurrentTotal >= __state)
			return;
		Item item = __instance.GetComponent<Item>();
		if (!MedicalUseTracker.IsMedicalItem(item))
			return;

		Body healer = MedicalUseContext.Healer ?? PlayerCamera.main?.body ?? body;
		MedicalUseTracker.TryRecord(healer, body, item);
	}
}
