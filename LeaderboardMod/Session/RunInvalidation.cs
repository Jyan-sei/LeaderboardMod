using LeaderboardMod.Logging;
using LeaderboardMod.Sync;

namespace LeaderboardMod.Session;

internal static class RunInvalidation
{
	internal const string ReasonForbiddenConsole = "forbidden_console_command";
	internal const string ReasonAlreadyInvalidated = "already_invalidated";

	// console patch used to call this. this shit dont work from the client, we race vanilla.
	// server does the actual invalidate now. dont wire this back up
	internal static void InvalidateLocally(string reason, string detail = null)
	{
		if (!RunSessionManager.ShouldCapture())
			return;

		string saveId = RunSessionManager.Current?.SaveId ?? LeaderboardSaveId.ActiveRunId;
		if (string.IsNullOrEmpty(saveId))
			return;

		ApplyInvalidation(saveId, reason, detail, "client");
	}

	internal static void ApplyServerInvalidation(string saveId, string reason, string detail = null)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		ApplyInvalidation(saveId, reason, detail, "server");
	}

	/// <summary>
	/// loaded save still has an id we already invalidated locally - wipe it from slots and stay partial
	/// </summary>
	internal static void ReconfirmInvalidatedLoad(string saveId, string loadReason)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		var state = BackendSyncStateStore.Load(saveId);
		if (!state.IsInvalidated)
			return;

		int purged = LeaderboardSaveId.PurgeSaveIdFromAllLocalSaves(saveId);
		LeaderboardSaveId.ClearActiveRunId();
		RunSessionManager.StopForPartialLoadedSave(
			$"loaded invalidated run saveId={saveId} ({loadReason}); purged {purged} save file(s)");

		LbLog.Warn("Session:Invalidate",
			$"reconfirmed on load saveId={saveId} purgedFiles={purged} reason={state.InvalidationReason}");
	}

	private static void ApplyInvalidation(string saveId, string reason, string detail, string source)
	{
		int purged = LeaderboardSaveId.PurgeSaveIdFromAllLocalSaves(saveId);
		LeaderboardSaveId.ClearActiveRunId();
		RunSessionManager.StopForPartialLoadedSave(
			$"run invalidated ({source}): {reason}" + (string.IsNullOrEmpty(detail) ? "" : $" ({detail})") +
			$"; purged {purged} save file(s)");

		SessionTokenStore.ClearToken(saveId);
		BackendSync.CancelQueuedUploads(saveId);
		BackendSyncStateStore.MarkInvalidated(saveId, reason, detail);

		LbLog.Warn("Session:Invalidate",
			$"saveId={saveId} reason={reason} detail={detail ?? "(none)"} source={source} purgedFiles={purged}");
	}
}
