using UnityEngine;

namespace LeaderboardMod.Tracking;

internal static class MedicalUseContext
{
	internal static Body Healer;
	internal static Limb TargetLimb;
	internal static Item Item;

	internal static bool BandageSessionActive;
	internal static bool BandageSessionRecorded;

	internal static bool InjectionSessionActive;
	internal static float InjectionThreshold;
	internal static float InjectionAccumulated;
	internal static bool InjectionRecorded;

	internal static void Set(Body healer, Limb targetLimb, Item item)
	{
		Healer = healer;
		TargetLimb = targetLimb;
		Item = item;
	}

	internal static void ClearWoundApply()
	{
		Healer = null;
		TargetLimb = null;
		Item = null;
	}

	internal static void BeginBandageSession()
	{
		BandageSessionActive = true;
		BandageSessionRecorded = false;
	}

	internal static void BeginInjectionSession(Item item)
	{
		InjectionSessionActive = true;
		InjectionRecorded = false;
		InjectionAccumulated = 0f;
		InjectionThreshold = 0f;
		if (item != null && item.TryGetComponent<WaterContainerItem>(out var container))
			InjectionThreshold = container.Capacity * 0.01f;
	}

	internal static void EndMinigameSessions()
	{
		BandageSessionActive = false;
		BandageSessionRecorded = false;
		InjectionSessionActive = false;
		InjectionThreshold = 0f;
		InjectionAccumulated = 0f;
		InjectionRecorded = false;
	}
}
