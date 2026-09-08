using HarmonyLib;
using LeaderboardMod.Tracking;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
internal static class DrillPodFinishWorldGenerationPatch
{
	[HarmonyPrefix]
	private static void Prefix(WorldGeneration __instance, ref bool __state)
	{
		__state = __instance.doPod;
	}

	[HarmonyPostfix]
	private static void Postfix(bool __state)
	{
		if (!__state)
			return;
		RunEventTracker.RecordDrillPodUsed();
	}
}
