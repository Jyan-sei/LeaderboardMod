using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Tracking;

internal static class MedicalUseTracker
{
	internal static bool IsMedicalItem(Item item)
	{
		return item != null && item.Stats != null && item.Stats.category == "medical";
	}

	internal static void TryRecord(Body healer, Body target, Item item)
	{
		if (!IsMedicalItem(item))
			return;
		if (!RunEventTracker.TryGetSession(out _))
			return;
		if (!IsLocalHealer(healer))
			return;
		if (target == null)
			return;

		if (healer == target)
			RunEventTracker.RecordMedicalUsedOnSelf(item);
		else
			RunEventTracker.RecordMedicalUsedOnTeammate(item);
	}

	internal static void TryRecordFromContext()
	{
		if (MedicalUseContext.Item == null || MedicalUseContext.TargetLimb == null)
			return;
		TryRecord(MedicalUseContext.Healer, MedicalUseContext.TargetLimb.body, MedicalUseContext.Item);
	}

	internal static bool TryRecordInjection(float amount, WaterContainerItem container, Limb limb)
	{
		if (container == null || limb == null)
			return false;
		Item item = container.GetComponent<Item>();
		if (!IsMedicalItem(item))
			return false;

		if (MedicalUseContext.InjectionSessionActive)
		{
			if (MedicalUseContext.InjectionRecorded)
				return false;
			MedicalUseContext.InjectionAccumulated += amount;
			if (MedicalUseContext.InjectionAccumulated < MedicalUseContext.InjectionThreshold)
				return false;
			MedicalUseContext.InjectionRecorded = true;
		}
		else if (amount < container.Capacity * 0.01f)
		{
			return false;
		}

		Body healer = MedicalUseContext.Healer ?? PlayerCamera.main?.body;
		TryRecord(healer, limb.body, item);
		return true;
	}

	private static bool IsLocalHealer(Body healer)
	{
		if (healer == null)
			return false;
		return KrokMpOptional.IsBodyLocal(healer);
	}
}
