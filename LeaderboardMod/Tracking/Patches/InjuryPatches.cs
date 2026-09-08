using HarmonyLib;
using LeaderboardMod.Tracking;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(Limb), nameof(Limb.ImpactDamage))]
internal static class InjuryImpactDamagePatch
{
	[HarmonyPostfix]
	private static void Postfix(Limb __instance, float force)
	{
		if (force <= 0f)
			return;
		if (!RunEventTracker.TryGetSession(out _))
			return;
		if (PlayerCamera.main == null || PlayerCamera.main.body == null)
			return;
		if (__instance.body != PlayerCamera.main.body)
			return;
		RunEventTracker.RecordFirstInjury();
	}
}
