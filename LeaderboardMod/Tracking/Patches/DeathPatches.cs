using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using LeaderboardMod.Tracking;
using UnityEngine;
namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(PlayerCamera), "EndSequence")]
internal static class DeathPatches
{
	[HarmonyPostfix]
	private static void Postfix(PlayerCamera __instance, int type)
	{
		if (type != 0)
			return;
		if (PlayerCamera.main != __instance)
			return;
		if (!RunEventTracker.TryGetSession(out _))
			return;
		RunEventTracker.RecordSessionDeath();
		PathTrailCapture.MarkDeath();
		LbLog.Step("Patch:Death", "Session death recorded - creating forced snapshot for final stats");
		LeaderboardSaveCapture.CreateLeaderboardSave(forcedSave: true, adjustTotalDeathCount: 1);
	}
}
