using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LeaderboardMod.Session;

/// <summary>
/// figure out save.sv paths for vanilla + optional qol/mp overrides (reflection, no hard qol dep)
/// </summary>
internal static class LiveSavePath
{
	private static FieldInfo _mpPathReplacementField;
	private static PropertyInfo _qolSaveFolderProperty;
	private static FieldInfo _qolCurrentSaveFileField;
	private static bool _reflectionResolved;

	internal static string GetVanillaWritePath()
	{
		return Path.Combine(Application.persistentDataPath, "save.sv");
	}

	/// <summary>folder krokmp actually writes this SaveGame into (can be mp_save or mp_save/STEAM_*)</summary>
	internal static string GetLiveWritePath()
	{
		EnsureReflection();
		string replacement = ReadMpPathReplacement();
		if (!string.IsNullOrEmpty(replacement))
			return Path.Combine(replacement, "save.sv");
		return GetVanillaWritePath();
	}

	internal static string GetMpSaveRoot()
	{
		if (string.IsNullOrEmpty(Application.persistentDataPath))
			return null;
		return Path.Combine(Application.persistentDataPath, "mp_save");
	}

	/// <summary>best guess at the save file thats about to load (or just did)</summary>
	internal static string GetPathForLoad()
	{
		EnsureReflection();

		// qol slot files are the real load source during hot-swap, dont let stale mp_save/save.sv win
		string qolPath = TryGetQolActiveSavePath();
		if (!string.IsNullOrEmpty(qolPath) && File.Exists(qolPath) && ShouldPreferQolPathOverMp(qolPath))
			return qolPath;

		string replacement = ReadMpPathReplacement();
		if (!string.IsNullOrEmpty(replacement))
		{
			string candidate = Path.Combine(replacement, "save.sv");
			if (File.Exists(candidate))
				return candidate;
		}

		if (!string.IsNullOrEmpty(qolPath) && File.Exists(qolPath))
			return qolPath;

		string vanilla = GetVanillaWritePath();
		if (File.Exists(vanilla))
			return vanilla;

		return vanilla;
	}

	internal static bool IsEphemeralLoadPath(string savePath)
	{
		if (string.IsNullOrEmpty(savePath))
			return true;

		string normalized = savePath.Replace('\\', '/');
		return normalized.IndexOf("RKI_live", StringComparison.OrdinalIgnoreCase) >= 0
			|| normalized.IndexOf("/Temp/", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool ShouldPreferQolPathOverMp(string qolPath)
	{
		string file = Path.GetFileName(qolPath);
		if (string.IsNullOrEmpty(file))
			return false;

		return file.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(file, "save.sv", StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureReflection()
	{
		if (_reflectionResolved)
			return;

		_reflectionResolved = true;

		Type mpPatch = AccessTools.TypeByName("KrokoshaCasualtiesMP.SavesystemPatch");
		if (mpPatch != null)
			_mpPathReplacementField = AccessTools.Field(mpPatch, "savedatapathreplacement");

		Type saveManager = AccessTools.TypeByName("QoL_Unknown.SaveManager");
		if (saveManager != null)
		{
			_qolSaveFolderProperty = AccessTools.Property(saveManager, "SaveFolder");
			_qolCurrentSaveFileField = AccessTools.Field(saveManager, "CurrentSaveFile");
		}
	}

	private static string ReadMpPathReplacement()
	{
		if (_mpPathReplacementField == null)
			return null;

		try
		{
			return _mpPathReplacementField.GetValue(null) as string;
		}
		catch
		{
			return null;
		}
	}

	private static string TryGetQolActiveSavePath()
	{
		if (_qolSaveFolderProperty == null || _qolCurrentSaveFileField == null)
			return null;

		try
		{
			string folder = _qolSaveFolderProperty.GetValue(null) as string;
			string file = _qolCurrentSaveFileField.GetValue(null) as string;
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file))
				return null;

			return Path.Combine(folder, file);
		}
		catch
		{
			return null;
		}
	}

	internal static string GetQolSaveFolder()
	{
		EnsureReflection();
		if (_qolSaveFolderProperty == null)
			return null;

		try
		{
			return _qolSaveFolderProperty.GetValue(null) as string;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>qol slots, vanilla save.sv, and mp host save.sv we might need to purge an id from</summary>
	internal static System.Collections.Generic.IEnumerable<string> EnumerateCandidateSaveFiles()
	{
		EnsureReflection();

		var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string persistent = Application.persistentDataPath;
		if (!string.IsNullOrEmpty(persistent) && Directory.Exists(persistent))
		{
			foreach (string file in Directory.GetFiles(persistent, "*.sv", SearchOption.TopDirectoryOnly))
				paths.Add(file);
		}

		string qolFolder = GetQolSaveFolder();
		if (!string.IsNullOrEmpty(qolFolder) && Directory.Exists(qolFolder))
		{
			foreach (string file in Directory.GetFiles(qolFolder, "*.sv", SearchOption.TopDirectoryOnly))
				paths.Add(file);
		}

		AddMpSaveCopies(paths);

		return paths;
	}

	/// <summary>the copies this session might load next - not every qol slot</summary>
	internal static System.Collections.Generic.IEnumerable<string> EnumerateLiveSaveFiles()
	{
		EnsureReflection();
		var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddIfExists(paths, GetVanillaWritePath());
		AddIfExists(paths, GetLiveWritePath());
		AddIfExists(paths, GetPathForLoad());
		string qol = TryGetQolActiveSavePath();
		AddIfExists(paths, qol);
		AddMpSaveCopies(paths);

		return paths;
	}

	private static void AddMpSaveCopies(System.Collections.Generic.HashSet<string> paths)
	{
		string mpRoot = GetMpSaveRoot();
		if (string.IsNullOrEmpty(mpRoot) || !Directory.Exists(mpRoot))
			return;

		AddIfExists(paths, Path.Combine(mpRoot, "save.sv"));
		foreach (string dir in Directory.GetDirectories(mpRoot))
			AddIfExists(paths, Path.Combine(dir, "save.sv"));
	}

	private static void AddIfExists(System.Collections.Generic.HashSet<string> paths, string path)
	{
		if (string.IsNullOrEmpty(path) || IsEphemeralLoadPath(path) || !File.Exists(path))
			return;
		paths.Add(path);
	}
}
