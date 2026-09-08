using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;

namespace LeaderboardMod.Tracking.Patches;

/// <summary>
/// qol "load this sp slot as host" copies the file into mp_save. restamp the id
/// onto those copies so enabling mp doesnt start a new partial run.
/// </summary>
[HarmonyPatch]
internal static class QolSpToMpSavePatch
{
	static bool Prepare()
	{
		return AccessTools.TypeByName("QoL_Unknown.KrokoshaMpSaveBundle") != null;
	}

	static System.Reflection.MethodBase TargetMethod()
	{
		var type = AccessTools.TypeByName("QoL_Unknown.KrokoshaMpSaveBundle");
		return AccessTools.Method(type, "StageSingleplayerSlotAsHostBundle");
	}

	[HarmonyPostfix]
	private static void Postfix(bool __result, string saveFolder, string slotFileName)
	{
		if (!__result)
			return;

		string source = System.IO.Path.Combine(saveFolder ?? "", slotFileName ?? "");
		string id = LeaderboardSaveId.ActiveRunId;
		if (!LeaderboardSaveId.IsValid(id))
			id = LeaderboardSaveId.TryReadFromSavePath(source);
		if (!LeaderboardSaveId.IsValid(id))
			id = RunContinuationPolicy.TryRecoverSaveIdForLoad(source);
		if (!LeaderboardSaveId.IsValid(id))
		{
			LbLog.Warn("SaveId:SpToMp", $"copied {source} into mp_save with no id to stamp");
			return;
		}

		LeaderboardSaveId.InjectIntoLiveSaves(id);
		LbLog.Step("SaveId:SpToMp", $"kept {id} after qol copied {slotFileName} into mp_save");
	}
}
