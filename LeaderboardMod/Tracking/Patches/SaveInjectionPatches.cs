using System.IO;
using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
internal static class LeaderboardSaveIdSaveGamePatch
{
	[HarmonyPrefix]
	private static void Prefix()
	{
		// never persist into runSettings - vanilla GetSetting(First) throws on unknown keys
		LeaderboardSaveId.StripFromLiveRunSettings();
	}

	/// <summary>
	/// stamp id into the save we just wrote. capture redirects to a temp folder -
	/// dont touch leftover vanilla save.sv or a later continue inherits this run.
	/// </summary>
	[HarmonyPostfix]
	[HarmonyPriority(Priority.First)]
	private static void PostfixInjectIntoWrittenSave()
	{
		string id = LeaderboardSaveId.ActiveRunId;
		if (!LeaderboardSaveId.IsValid(id))
			return;

		if (LeaderboardSaveCapture.IsCapturing || LeaderboardSavePath.IsRedirectActive)
		{
			string redirected = Path.Combine(LeaderboardSavePath.Current(), "save.sv");
			LeaderboardSaveId.InjectIntoSaveFile(redirected, id);
			return;
		}

		string written = LiveSavePath.GetLiveWritePath();
		LeaderboardSaveId.InjectIntoSaveFile(written, id);
		LeaderboardSaveId.InjectIntoLiveSaves(id);
	}
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame))]
internal static class LeaderboardSaveIdTryLoadGamePatch
{
	/// <summary>vanilla deletes save.sv at the end of TryLoadGame - grab the id before that</summary>
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	private static void PrefixCaptureBeforeConsume()
	{
		if (!SaveSystem.loadedRun)
			return;

		SaveIdTargetSync.StageIdFromLoadPath(LiveSavePath.GetPathForLoad(), "TryLoadGame");
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	private static void PostfixApplySession()
	{
		if (!SaveSystem.loadedRun)
			return;

		SaveIdTargetSync.TryApplyStagedId("TryLoadGame");
	}
}
