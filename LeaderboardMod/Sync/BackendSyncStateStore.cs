using System;
using System.Globalization;
using System.IO;
using LeaderboardMod.Capture;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using Newtonsoft.Json;

namespace LeaderboardMod.Sync;

internal static class BackendSyncStateStore
{
	private static string StateDir =>
		Path.Combine(LeaderboardPaths.GetPluginDirectory(), "backend_sync");

	internal static void Initialize()
	{
		Directory.CreateDirectory(StateDir);
	}

	internal static BackendSyncState Load(string saveId)
	{
		string path = GetStatePath(saveId);
		if (!File.Exists(path))
			return new BackendSyncState();

		try
		{
			return JsonConvert.DeserializeObject<BackendSyncState>(File.ReadAllText(path)) ?? new BackendSyncState();
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:State", $"Failed reading {path}: {ex.Message}");
			return new BackendSyncState();
		}
	}

	internal static void Save(string saveId, BackendSyncState state)
	{
		if (string.IsNullOrEmpty(saveId) || state == null)
			return;

		try
		{
			Directory.CreateDirectory(StateDir);
			string path = GetStatePath(saveId);
			File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented));
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:State", $"Failed writing state for save {saveId}: {ex.Message}");
		}
	}

	internal static void RecordPending(string saveId, int snapshotSeq, string timestampUtc)
	{
		var state = Load(saveId);
		if (snapshotSeq > state.LatestPendingSnapshotSeq)
		{
			state.LatestPendingSnapshotSeq = snapshotSeq;
			state.LatestPendingTimestampUtc = timestampUtc;
		}

		Save(saveId, state);
	}

	internal static void RecordSuccess(string saveId, int snapshotSeq)
	{
		var state = Load(saveId);
		if (snapshotSeq > state.LastAcknowledgedSnapshotSeq)
			state.LastAcknowledgedSnapshotSeq = snapshotSeq;
		state.LastSuccessUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		state.LastError = null;
		state.LastErrorUtc = null;
		Save(saveId, state);
	}

	internal static void RecordFailure(string saveId, string error)
	{
		var state = Load(saveId);
		state.LastError = error;
		state.LastErrorUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		Save(saveId, state);
	}

	internal static void MarkInvalidated(string saveId, string reason, string detail = null)
	{
		var state = Load(saveId);
		state.IsInvalidated = true;
		state.InvalidationReason = reason;
		state.InvalidationDetail = detail;
		state.InvalidatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		state.LastError = reason + (string.IsNullOrEmpty(detail) ? "" : $": {detail}");
		state.LastErrorUtc = state.InvalidatedUtc;
		Save(saveId, state);
	}

	internal static void Reset(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		try
		{
			string path = GetStatePath(saveId);
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:State", $"Failed clearing state for save {saveId}: {ex.Message}");
		}
	}

	private static string GetStatePath(string saveId)
	{
		string steam = PlayerIdentity.SanitizePathSegment(PlayerIdentity.GetSteamId());
		string save = PlayerIdentity.SanitizePathSegment(saveId);
		return Path.Combine(StateDir, $"{steam}_{save}.json");
	}
}
