using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Tracking;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(ElderThornbackBehaviour), "Start")]
internal static class ElderStartPatch
{
	// prefix so a throwing vanilla Start (mp world not ready, missing renderer) still registers
	[HarmonyPrefix]
	private static void Prefix(ElderThornbackBehaviour __instance)
	{
		ElderTrailCapture.Register(__instance);
	}
}

[HarmonyPatch(typeof(ElderThornbackBehaviour), "OnDestroy")]
internal static class ElderPatches
{
	[HarmonyPostfix]
	private static void Postfix(ElderThornbackBehaviour __instance)
	{
		ElderTrailCapture.Unregister(__instance);
		if (!RunEventTracker.TryGetSession(out _))
			return;

		var build = __instance.GetComponent<BuildingEntity>();
		if (build == null || build.health > 0f)
			return;

		// kill credit is health based. vanilla happiness uses host-camera proximity;
		// mp clients can finish an elder while the host is out of range / elder is frozen
		RunEventTracker.RecordElderKill(__instance.GetInstanceID());
	}
}
