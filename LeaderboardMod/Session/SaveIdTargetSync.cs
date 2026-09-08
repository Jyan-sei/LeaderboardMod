using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Sync;

namespace LeaderboardMod.Session;

/// <summary>
/// keep RunSession.SaveId matched to whatever leaderboardSaveId we yanked off save.sv before vanilla eats it
/// </summary>
internal static class SaveIdTargetSync
{
	private static bool _pendingLoadRunSession;
	private static bool _freshRunGuard;
	private static string _stagedIdFromLoad;
	private static string _stagedLoadPath;
	private static bool _stagedAuthoritativeLoad;
	private static bool _stagedLoadMissingId;

	internal static void MarkPendingLoadRun()
	{
		_pendingLoadRunSession = true;
		_freshRunGuard = false;
	}

	/// <summary>new StartRun - ignore leftover save.sv ids until a real LoadRun</summary>
	internal static void MarkFreshRun()
	{
		_pendingLoadRunSession = false;
		_freshRunGuard = true;
		ClearStaging();
	}

	internal static void StageIdFromLoadPath(string savePath, string reason)
	{
		if (LiveSavePath.IsEphemeralLoadPath(savePath))
			return;

		_stagedAuthoritativeLoad = true;
		_stagedLoadPath = savePath;

		string id = LeaderboardSaveId.TryReadFromSavePath(savePath);
		if (!LeaderboardSaveId.IsValid(id))
		{
			_stagedIdFromLoad = null;
			_stagedLoadMissingId = true;
			LbLog.Warn("SaveId:Load", $"No {LeaderboardSaveId.SaveFieldName} in {savePath ?? "(null)"} ({reason})");
			return;
		}

		_stagedIdFromLoad = id;
		_stagedLoadMissingId = false;
		LbLog.Step("SaveId:Load", $"{reason} -> {id}");
	}

	internal static void TryApplyStagedId(string reason)
	{
		if (!RunSessionManager.ShouldCapture())
		{
			ClearStaging();
			return;
		}

		if (!_stagedAuthoritativeLoad)
		{
			ClearStaging();
			return;
		}

		bool pendingLoadRun = _pendingLoadRunSession;
		string id = _stagedIdFromLoad;
		string loadPath = _stagedLoadPath;
		bool missingId = _stagedLoadMissingId;
		ClearStaging();

		// leftover save.sv from the previous run is still on disk during a new game.
		// dont steal that id just because TryLoadGame peeked at it.
		if (_freshRunGuard && !pendingLoadRun)
		{
			LbLog.Step("SaveId:Load", $"ignored leftover save id during fresh run ({reason})");
			return;
		}

		if (LeaderboardSaveId.IsValid(id))
		{
			ApplyLoadedId(id, loadPath, reason);
			LeaderboardSaveId.InjectIntoLiveSaves(id);
			return;
		}

		if (missingId)
		{
			string recovered = RunContinuationPolicy.TryRecoverSaveIdForLoad(loadPath);
			if (LeaderboardSaveId.IsValid(recovered))
			{
				ApplyLoadedId(recovered, loadPath, reason + " recovered-id");
				LeaderboardSaveId.InjectIntoLiveSaves(recovered);
				return;
			}
			ApplyMintedPartialLoad(reason, pendingLoadRun);
		}
	}

	private static void ApplyLoadedId(string id, string loadPath, string reason)
	{
		if (BackendSyncStateStore.Load(id).IsInvalidated)
		{
			RunInvalidation.ReconfirmInvalidatedLoad(id, reason);
			return;
		}

		if (RunContinuationPolicy.LooksLikeStolenId(id, loadPath))
		{
			LbLog.Warn("SaveId:Splice",
				$"save {id} already has a different shallow run locally - minting a new id for this continue ({reason})");
			ApplyMintedPartialLoad(reason + " spliced-id", _pendingLoadRunSession);
			return;
		}

		if (_pendingLoadRunSession)
		{
			_pendingLoadRunSession = false;
			_freshRunGuard = false;
			RunSessionManager.EndSession();
			RunSessionManager.StartLoadedSession(id);
			LbLog.Step("SaveId:Session", $"LoadRun session saveId={id} ({reason})");
			return;
		}

		if (!RunSessionManager.HasActiveSession)
		{
			_freshRunGuard = false;
			RunSessionManager.StartLoadedSession(id);
			LbLog.Step("SaveId:Session", $"Loaded session saveId={id} ({reason})");
			return;
		}

		RunSessionManager.SwitchTarget(id, reason);
	}

	private static void ApplyMintedPartialLoad(string reason, bool wasPendingLoadRun)
	{
		_pendingLoadRunSession = false;
		_freshRunGuard = false;
		string id = LeaderboardSaveId.Generate();
		LeaderboardSaveId.SetActiveRunId(id);
		TryStampMintedId(id);

		if (wasPendingLoadRun)
			RunSessionManager.EndSession();

		RunSessionManager.StartMintedPartialSession(id);
		string detail = wasPendingLoadRun ? "LoadRun" : "load";
		LbLog.Step("SaveId:Mint", $"{detail}: no {LeaderboardSaveId.SaveFieldName} - minted {id} and started partial tracking ({reason})");
	}

	private static void TryStampMintedId(string id)
	{
		LeaderboardSaveId.InjectIntoLiveSaves(id);
	}

	private static void ClearStaging()
	{
		_stagedIdFromLoad = null;
		_stagedLoadPath = null;
		_stagedAuthoritativeLoad = false;
		_stagedLoadMissingId = false;
	}
}
