using System;
using System.IO;
using LeaderboardMod.Capture;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using LeaderboardMod.Storage;
using LeaderboardMod.Sync;
using UnityEngine;

namespace LeaderboardMod.Session;

internal static class RunSessionManager
{
	internal static RunSession Current { get; private set; }
	private static bool _loggedClientSkip;

	internal static bool ShouldCapture()
	{
		if (!KrokMpOptional.IsNetworkRunning)
			return true;
		if (KrokMpOptional.IsServer)
			return true;
		if (!_loggedClientSkip)
		{
			_loggedClientSkip = true;
			LbLog.Warn("MP:Gate", "KrokMP client detected - LeaderboardMod tracking disabled on this machine.");
		}
		return false;
	}

	internal static bool HasActiveSession => Current != null && Current.Active;

	internal static void StartFreshSession(string saveId)
	{
		if (!ShouldCapture())
			return;

		BeginSession(saveId, RunContinuationPolicy.ForFreshStart());
	}

	internal static void StartLoadedSession(string saveId)
	{
		if (!ShouldCapture())
			return;

		if (!LeaderboardSaveId.IsValid(saveId))
		{
			StartMintedPartialSession(LeaderboardSaveId.Generate());
			return;
		}

		BeginSession(saveId, RunContinuationPolicy.EvaluateForLoadedSave(saveId));
	}

	internal static void StartMintedPartialSession(string saveId)
	{
		if (!ShouldCapture())
			return;

		if (!LeaderboardSaveId.IsValid(saveId))
			saveId = LeaderboardSaveId.Generate();

		BeginSession(saveId, RunContinuationPolicy.ForMintedPartialLoad());
	}

	internal static void EndSession()
	{
		if (Current != null)
		{
			LbLog.Step("Session:End", $"saveId={Current.SaveId} snapshots={Current.SnapshotSeq}");
			Current.Active = false;
			Current = null;
		}
		// leftover ActiveRunId would get stamped onto the next capture/save
		if (LeaderboardSaveId.IsValid(LeaderboardSaveId.ActiveRunId))
			LeaderboardSaveId.ClearActiveRunId();
	}

	internal static void StopForPartialLoadedSave(string reason)
	{
		string saveId = Current?.SaveId;
		if (!string.IsNullOrEmpty(saveId))
			BackendSync.CancelQueuedUploads(saveId);

		EndSession();
		LeaderboardSaveId.ClearActiveRunId();
		LbLog.Step("Session:PartialHalt", reason);
	}

	internal static void SwitchTarget(string saveId, string reason)
	{
		if (Current != null && Current.SaveId == saveId)
		{
			LbLog.Step("SaveId:Session", $"Same target saveId={saveId} ({reason})");
			return;
		}

		string previous = Current?.SaveId;
		if (!string.IsNullOrEmpty(previous))
			BackendSync.CancelQueuedUploads(previous);

		EndSession();
		StartLoadedSession(saveId);
		LbLog.Step("SaveId:Switch", $"{previous ?? "none"} -> {saveId} ({reason})");
	}

	// used to mint a session if you were already in-world. this shit dont work,
	// hot reload stamped a fake run on the live save. 100 builds later disabling it fixed all my issues
	internal static void EnsureSessionForLateStart()
	{
		if (!ShouldCapture() || HasActiveSession)
			return;
		LbLog.Warn("Session:LateStart", "In-world session without StartRun - tracking disabled");
	}

	internal static bool IsMultiplayerHosted()
	{
		return KrokMpOptional.IsNetworkRunning && KrokMpOptional.IsServer;
	}

	internal static int GetMpPlayerCount()
	{
		return KrokMpOptional.GetMpPlayerCount();
	}

	internal static int GetMpPlayerLimit()
	{
		return KrokMpOptional.GetMpPlayerLimit();
	}

	internal static bool IsSaveOurs()
	{
		return KrokMpOptional.IsSaveOurs();
	}

	private static void BeginSession(string saveId, RunContinuationPolicy.Decision decision)
	{
		if (!LeaderboardSaveId.IsValid(saveId))
			saveId = ResolveSaveId();

		int restoredSeq = decision.EnableTracking ? CaptureDatabase.GetMaxSnapshotSeq(saveId) : 0;
		DateTime runStarted = CaptureDatabase.TryGetRegisteredRunStartedUtc(saveId) ?? DateTime.UtcNow;

		Current = new RunSession
		{
			SaveId = saveId,
			RunStartedUtc = runStarted,
			IsPartialRun = decision.MarkPartialInSidecar,
			TrackingEnabled = decision.EnableTracking,
			Active = true,
			SnapshotSeq = restoredSeq,
			LastRecordedBiomeDepth = WorldGeneration.world != null ? WorldGeneration.world.biomeDepth : -1
		};
		MedicalEventCapture.Reset();

		if (decision.EnableTracking && LeaderboardSaveId.IsValid(saveId))
			LeaderboardSaveId.SetActiveRunId(saveId);

		LbLog.Step("Session:Start",
			$"saveId={Current.SaveId} partial={Current.IsPartialRun} tracking={Current.TrackingEnabled} " +
			$"seq={Current.SnapshotSeq} reason={decision.Reason} steamId={PlayerIdentity.GetSteamId()} " +
			$"mpHosted={IsMultiplayerHosted()}");

		CaptureDatabase.EnsureSessionTable(Current);
		ElderTrailCapture.DiscoverWorldElders();
		if (decision.FreshRunReset)
		{
			CaptureDatabase.ResetSessionTableForFreshRun(Current);
			BackendSync.PrepareFreshRun(saveId);
		}
		else if (!decision.EnableTracking)
		{
			BackendSync.CancelQueuedUploads(saveId);
		}
		else
		{
			BackendSync.ResumeBacklogIfNeeded(saveId);
		}
	}

	private static string ResolveSaveId()
	{
		if (LeaderboardSaveId.IsValid(LeaderboardSaveId.ActiveRunId))
			return LeaderboardSaveId.ActiveRunId;

		string fromLiveSave = LeaderboardSaveId.TryReadFromSavePath(LiveSavePath.GetPathForLoad());
		if (LeaderboardSaveId.IsValid(fromLiveSave))
			return fromLiveSave;

		string fallback = LeaderboardSaveId.Generate();
		LbLog.Warn("Session:SaveId", $"No {LeaderboardSaveId.SaveFieldName} found - generated fallback {fallback}");
		LeaderboardSaveId.SetActiveRunId(fallback);
		return fallback;
	}
}
