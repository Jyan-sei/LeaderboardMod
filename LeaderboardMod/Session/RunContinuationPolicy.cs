using System.Collections.Generic;
using LeaderboardMod.Logging;
using LeaderboardMod.Storage;
using LeaderboardMod.Sync;

namespace LeaderboardMod.Session;

/// <summary>
/// do we resume sync for this loaded save or just watch.
/// checked on every load/swap, not once per process.
/// </summary>
internal static class RunContinuationPolicy
{
	internal sealed class Decision
	{
		internal bool EnableTracking;
		internal bool MarkPartialInSidecar;
		internal bool FreshRunReset;
		internal string Reason;
	}

	internal static Decision ForFreshStart()
	{
		return new Decision
		{
			EnableTracking = true,
			MarkPartialInSidecar = false,
			FreshRunReset = true,
			Reason = "StartRun fresh attestation",
		};
	}

	internal static Decision ForMintedPartialLoad()
	{
		return new Decision
		{
			EnableTracking = true,
			MarkPartialInSidecar = true,
			FreshRunReset = true,
			Reason = "loaded save missing leaderboardSaveId - minted new partial saveId",
		};
	}

	internal static Decision EvaluateForLoadedSave(string saveId)
	{
		if (!LeaderboardSaveId.IsValid(saveId))
			return ForMintedPartialLoad();

		var syncState = BackendSyncStateStore.Load(saveId);
		if (syncState.IsInvalidated)
		{
			return new Decision
			{
				EnableTracking = false,
				MarkPartialInSidecar = true,
				FreshRunReset = false,
				Reason = "run invalidated locally - uploads blocked",
			};
		}

		if (HasLocalAttestationHistory(saveId))
		{
			return new Decision
			{
				EnableTracking = true,
				MarkPartialInSidecar = false,
				FreshRunReset = false,
				Reason = "continued attested run (token/sync/sqlite history)",
			};
		}

		return new Decision
		{
			EnableTracking = true,
			MarkPartialInSidecar = false,
			FreshRunReset = false,
			Reason = "leaderboardSaveId present - allow sync attempt",
		};
	}

	internal static bool HasLocalAttestationHistory(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return false;

		if (!string.IsNullOrEmpty(SessionTokenStore.GetToken(saveId)))
			return true;

		var sync = BackendSyncStateStore.Load(saveId);
		if (sync.LastAcknowledgedSnapshotSeq > 0 || sync.LatestPendingSnapshotSeq > 0)
			return true;

		return CaptureDatabase.GetMaxSnapshotSeq(saveId) > 0;
	}

	/// <summary>
	/// capture used to stamp the live run id onto leftover save.sv. then continue
	/// would append a deeper save onto a different L1 run that already used that id.
	/// </summary>
	internal static bool LooksLikeStolenId(string saveId, string savePath)
	{
		var last = CaptureDatabase.TryGetLastSnapshotHint(saveId);
		if (last == null)
			return false;

		bool lastLooksFresh = last.BiomeDepth <= 0 && last.DepthMeters < 150;
		if (!lastLooksFresh)
			return false;

		var root = SaveSvReader.ReadSaveRoot(savePath);
		if (root == null)
			return false;

		int saveBiome = ReadSaveBiomeDepth(root);
		double saveDepth = ReadSaveDepthMeters(root);
		string savePersona = ReadSavePersonaKey(root);

		bool saveIsDeeper = saveBiome > 0 || saveDepth > last.DepthMeters + 50;
		bool personaMismatch = !string.IsNullOrEmpty(last.PersonaKey)
			&& last.PersonaKey != "|||"
			&& !string.IsNullOrEmpty(savePersona)
			&& savePersona != "|||"
			&& !string.Equals(last.PersonaKey, savePersona, System.StringComparison.Ordinal);

		if (!saveIsDeeper && !personaMismatch)
			return false;

		LbLog.Warn("Session:Splice",
			$"id={saveId} lastBiome={last.BiomeDepth} lastDepth={last.DepthMeters:0.#} " +
			$"saveBiome={saveBiome} saveDepth={saveDepth:0.#} personaMismatch={personaMismatch}");
		return true;
	}

	/// <summary>
	/// mp/qol often copy the world and drop leaderboardSaveId. if another local file or
	/// sqlite run is clearly the same character at the same depth, keep that id.
	/// </summary>
	internal static string TryRecoverSaveIdForLoad(string loadPath)
	{
		var candidates = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (string path in LiveSavePath.EnumerateCandidateSaveFiles())
		{
			if (string.Equals(path, loadPath, System.StringComparison.OrdinalIgnoreCase))
				continue;
			string id = LeaderboardSaveId.TryReadFromSavePath(path);
			if (LeaderboardSaveId.IsValid(id))
				candidates.Add(id);
		}
		foreach (string id in CaptureDatabase.ListKnownSaveIds())
			candidates.Add(id);

		string recovered = null;
		foreach (string id in candidates)
		{
			if (!LooksLikeSameContinuedRun(id, loadPath))
				continue;
			if (recovered != null && !string.Equals(recovered, id, System.StringComparison.OrdinalIgnoreCase))
			{
				LbLog.Warn("SaveId:Recover", $"multiple matching ids ({recovered}, {id}) - not guessing");
				return null;
			}
			recovered = id;
		}

		if (LeaderboardSaveId.IsValid(recovered))
			LbLog.Step("SaveId:Recover", $"{recovered} from missing field in {loadPath ?? "(null)"}");
		return recovered;
	}

	internal static bool LooksLikeSameContinuedRun(string saveId, string savePath)
	{
		if (!LeaderboardSaveId.IsValid(saveId) || string.IsNullOrEmpty(savePath))
			return false;
		if (LooksLikeStolenId(saveId, savePath))
			return false;

		var last = CaptureDatabase.TryGetLastSnapshotHint(saveId);
		var root = SaveSvReader.ReadSaveRoot(savePath);
		if (root == null)
			return false;

		int saveBiome = ReadSaveBiomeDepth(root);
		double saveDepth = ReadSaveDepthMeters(root);
		string savePersona = ReadSavePersonaKey(root);

		if (last == null)
			return false;

		bool personaMatch = string.IsNullOrEmpty(last.PersonaKey)
			|| last.PersonaKey == "|||"
			|| string.IsNullOrEmpty(savePersona)
			|| savePersona == "|||"
			|| string.Equals(last.PersonaKey, savePersona, System.StringComparison.Ordinal);
		if (!personaMatch)
			return false;

		double depthDelta = System.Math.Abs(saveDepth - last.DepthMeters);
		bool depthClose = depthDelta <= 2500;
		bool biomeClose = saveBiome == last.BiomeDepth
			|| (saveBiome > 0 && last.BiomeDepth > 0 && System.Math.Abs(saveBiome - last.BiomeDepth) <= 1);
		return depthClose || biomeClose;
	}

	private static int ReadSaveBiomeDepth(Newtonsoft.Json.Linq.JObject root)
	{
		// save.sv stores biome as 1-based layer index
		int stored = TokenInt(root["biome"], 1);
		return stored > 0 ? stored - 1 : 0;
	}

	private static double ReadSaveDepthMeters(Newtonsoft.Json.Linq.JObject root)
	{
		return TokenDouble(root["totalTraveled"], 0);
	}

	private static string ReadSavePersonaKey(Newtonsoft.Json.Linq.JObject root)
	{
		return string.Join("|",
			root["cId"]?.ToString() ?? "",
			root["cAge"]?.ToString() ?? "",
			root["cHeight"]?.ToString() ?? "",
			root["cVer"]?.ToString() ?? "");
	}

	private static int TokenInt(Newtonsoft.Json.Linq.JToken token, int fallback)
	{
		if (token == null)
			return fallback;
		return int.TryParse(token.ToString(), out int value) ? value : fallback;
	}

	private static double TokenDouble(Newtonsoft.Json.Linq.JToken token, double fallback)
	{
		if (token == null)
			return fallback;
		return double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double value)
			? value
			: fallback;
	}
}
