using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LeaderboardMod.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LeaderboardMod.Session;

/// <summary>
/// per-run id stuck in save.sv as a top level field (gzip json).
/// not the krokmp SAVEID / lobby slot thing.
/// </summary>
internal static class LeaderboardSaveId
{
	internal const string SaveFieldName = "leaderboardSaveId";

	private static readonly Regex IdFormat = new Regex("^[a-f0-9]{32}$", RegexOptions.Compiled);

	private static string _activeRunId;

	internal static string ActiveRunId => _activeRunId;

	internal static bool IsValid(string value)
	{
		return !string.IsNullOrWhiteSpace(value) && IdFormat.IsMatch(value.Trim());
	}

	internal static string Generate()
	{
		return Guid.NewGuid().ToString("N");
	}

	internal static void SetActiveRunId(string id)
	{
		if (!IsValid(id))
			throw new ArgumentException($"Invalid {SaveFieldName}: {id}", nameof(id));

		_activeRunId = id;
		StripFromLiveRunSettings();
		LbLog.Step("SaveId:Active", id);
	}

	internal static void ClearActiveRunId()
	{
		_activeRunId = null;
		StripFromLiveRunSettings();
		LbLog.Step("SaveId:Active", "(cleared)");
	}

	/// <summary>
	/// vanilla TupleListToDic calls GetSetting for every runSettings key.
	/// unknown names throw so dont put leaderboardSaveId in the live dict.
	/// </summary>
	internal static void StripFromLiveRunSettings()
	{
		WorldGeneration.runSettings?.Remove(SaveFieldName);
	}

	internal static string TryReadFromSaveRoot(JObject saveRoot)
	{
		if (saveRoot == null)
			return null;

		string topLevel = saveRoot[SaveFieldName]?.ToString();
		if (IsValid(topLevel))
			return topLevel.Trim().ToLowerInvariant();

		return TryReadFromRunSettingsDictionary(saveRoot["runSettings"]);
	}

	internal static string TryReadFromSavePath(string saveSvPath)
	{
		var root = Storage.SaveSvReader.ReadSaveRoot(saveSvPath);
		return TryReadFromSaveRoot(root);
	}

	internal static string TryReadFromLiveRunSettings()
	{
		if (WorldGeneration.runSettings == null)
			return null;

		if (WorldGeneration.runSettings.TryGetValue(SaveFieldName, out object value))
		{
			string text = value?.ToString();
			if (IsValid(text))
				return text.Trim().ToLowerInvariant();
		}

		return null;
	}

	internal static bool RemoveFromSaveFile(string saveSvPath)
	{
		if (string.IsNullOrEmpty(saveSvPath) || !System.IO.File.Exists(saveSvPath))
			return false;

		try
		{
			byte[] raw = System.IO.File.ReadAllBytes(saveSvPath);
			string json = UnzipToString(raw);
			if (string.IsNullOrEmpty(json))
				return false;

			var root = JObject.Parse(json);
			bool removedTop = root.Remove(SaveFieldName);
			bool removedNested = StripFromRunSettingsToken(root["runSettings"]);
			if (!removedTop && !removedNested)
				return false;

			string updated = root.ToString(Newtonsoft.Json.Formatting.None);
			System.IO.File.WriteAllBytes(saveSvPath, Zip(updated));
			LbLog.Step("SaveId:Purge", saveSvPath);
			return true;
		}
		catch (Exception ex)
		{
			LbLog.Warn("SaveId:Purge", $"Failed removing id from {saveSvPath}: {ex.Message}");
			return false;
		}
	}

	/// <summary>strip leaderboardSaveId out of local slots/saves that still have this run id</summary>
	internal static int PurgeSaveIdFromAllLocalSaves(string saveId)
	{
		if (!IsValid(saveId))
			return 0;

		int purged = 0;
		foreach (string path in LiveSavePath.EnumerateCandidateSaveFiles())
		{
			string found = TryReadFromSavePath(path);
			if (!string.Equals(found, saveId, StringComparison.OrdinalIgnoreCase))
				continue;

			if (RemoveFromSaveFile(path))
				purged++;
		}

		if (purged > 0)
			LbLog.Step("SaveId:PurgeAll", $"saveId={saveId} purgedFrom={purged} file(s)");

		return purged;
	}

	internal static int InjectIntoLiveSaves(string id)
	{
		if (!IsValid(id))
			return 0;

		int stamped = 0;
		foreach (string path in LiveSavePath.EnumerateLiveSaveFiles())
		{
			if (InjectIntoSaveFile(path, id))
				stamped++;
		}
		if (stamped > 0)
			LbLog.Step("SaveId:Inject", $"stamped {id} into {stamped} live save file(s)");
		return stamped;
	}

	internal static bool InjectIntoSaveFile(string saveSvPath, string id)
	{
		if (!IsValid(id) || string.IsNullOrEmpty(saveSvPath) || !System.IO.File.Exists(saveSvPath))
			return false;

		try
		{
			byte[] raw = System.IO.File.ReadAllBytes(saveSvPath);
			string json = UnzipToString(raw);
			if (string.IsNullOrEmpty(json))
				return false;

			var root = JObject.Parse(json);
			root[SaveFieldName] = id;
			StripFromRunSettingsToken(root["runSettings"]);
			string updated = root.ToString(Newtonsoft.Json.Formatting.None);
			System.IO.File.WriteAllBytes(saveSvPath, Zip(updated));
			return true;
		}
		catch (Exception ex)
		{
			LbLog.Warn("SaveId:Inject", $"Failed patching {saveSvPath}: {ex.Message}");
			return false;
		}
	}

	private static bool StripFromRunSettingsToken(JToken runSettingsToken)
	{
		if (runSettingsToken is not JArray array)
			return false;

		bool removed = false;
		for (int i = array.Count - 1; i >= 0; i--)
		{
			JToken entry = array[i];
			string key = entry["Item1"]?.ToString() ?? entry["Key"]?.ToString();
			if (!string.Equals(key, SaveFieldName, StringComparison.Ordinal))
				continue;

			entry.Remove();
			removed = true;
		}

		return removed;
	}

	private static string TryReadFromRunSettingsDictionary(JToken runSettingsToken)
	{
		if (runSettingsToken == null)
			return null;

		try
		{
			if (runSettingsToken is JArray array)
			{
				foreach (JToken entry in array)
				{
					string key = entry["Item1"]?.ToString() ?? entry["Key"]?.ToString();
					if (!string.Equals(key, SaveFieldName, StringComparison.Ordinal))
						continue;

					string value = entry["Item2"]?.ToString() ?? entry["Value"]?.ToString();
					if (IsValid(value))
						return value.Trim().ToLowerInvariant();
				}
			}
		}
		catch
		{
			// ignore malformed runSettings
		}

		return null;
	}

	private static string UnzipToString(byte[] compressed)
	{
		using var input = new System.IO.MemoryStream(compressed);
		using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
		using var output = new System.IO.MemoryStream();
		gzip.CopyTo(output);
		return System.Text.Encoding.UTF8.GetString(output.ToArray());
	}

	private static byte[] Zip(string uncompressed)
	{
		using var output = new System.IO.MemoryStream();
		using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(uncompressed);
			gzip.Write(bytes, 0, bytes.Length);
		}

		return output.ToArray();
	}
}
