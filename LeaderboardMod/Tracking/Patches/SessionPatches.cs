using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(PreRunScript), nameof(PreRunScript.StartRun))]
internal static class SessionStartRunPatch
{
	[HarmonyPostfix]
	private static void Postfix()
	{
		if (!RunSessionManager.ShouldCapture())
			return;

		RunSessionManager.EndSession();
		SaveIdTargetSync.MarkFreshRun();
		string runId = LeaderboardSaveId.Generate();
		LeaderboardSaveId.SetActiveRunId(runId);
		RunSessionManager.StartFreshSession(runId);
		LbLog.Step("Patch:Session", $"StartRun - new run id={runId}");
	}
}

[HarmonyPatch(typeof(PreRunScript), nameof(PreRunScript.LoadRun))]
internal static class SessionLoadRunPatch
{
	[HarmonyPostfix]
	private static void Postfix()
	{
		if (!RunSessionManager.ShouldCapture())
			return;

		RunSessionManager.EndSession();
		SaveIdTargetSync.MarkPendingLoadRun();
		LbLog.Step("Patch:Session", "LoadRun - awaiting leaderboardSaveId from save.sv (mint if missing)");
	}
}

[HarmonyPatch(typeof(PlayerCamera), "ToMainMenu")]
internal static class SessionToMainMenuPatch
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void Prefix()
	{
		VanillaSaveForcedCapturePatch.CaptureForMainMenu();
	}

	[HarmonyPostfix]
	private static void Postfix()
	{
		RunSessionManager.EndSession();
	}
}
