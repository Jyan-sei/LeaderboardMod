using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Tracking;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(WorldGeneration), "RegenerateWorld")]
internal static class LayerRegenerateWorldPatch
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void Prefix(WorldGeneration __instance)
	{
		if (!RunEventTracker.TryGetSession(out var session))
			return;
		session.ExpectingLayerTransition = true;
		session.PendingOldBiomeDepth = __instance.biomeDepth;
		RunEventTracker.ResetTrapsForNewLayer();
		LbLog.Step("Patch:Layer", $"RegenerateWorld begin - oldDepth={session.PendingOldBiomeDepth}");
		VanillaSaveForcedCapturePatch.TryForcedWorldCapture("RegenerateWorld");
	}
}

[HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
internal static class LayerFinishWorldGenerationPatch
{
	[HarmonyPostfix]
	private static void Postfix(WorldGeneration __instance)
	{
		if (!RunEventTracker.TryGetSession(out var session))
			return;

		TerrainOverviewCapture.MarkNewGeneration(session);
		ElderTrailCapture.DiscoverWorldElders();

		if (!session.ExpectingLayerTransition)
			return;

		int newDepth = __instance.biomeDepth;
		int oldDepth = session.PendingOldBiomeDepth;
		session.ExpectingLayerTransition = false;
		session.PendingArrivalCapture = true;

		if (newDepth != oldDepth)
		{
			RunEventTracker.RecordLayerTransition(oldDepth, newDepth);
			LbLog.Step("Patch:Layer", $"Layer transition {oldDepth} -> {newDepth} " +
				$"(total={session.LayerTransitionCount}, jungleToL2={session.JungleToDeepGravelTransitions})");
		}

		LbLog.Step("Patch:Layer", "Queued arrival snapshot after world generation finishes");
	}
}
