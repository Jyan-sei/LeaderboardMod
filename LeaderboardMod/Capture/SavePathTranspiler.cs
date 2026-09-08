using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal static class KrokMpSaveReflection
{
	private static FieldInfo _saveGameForceField;
	private static FieldInfo _savedataPathField;
	private static bool _resolved;
	private static bool _available;

	internal static bool TryResolve()
	{
		if (_resolved)
			return _available;

		_resolved = true;
		var saveGameType = AccessTools.TypeByName("KrokoshaCasualtiesMP.SaveSystem_SaveGame_MultiplayerPatch");
		_saveGameForceField = saveGameType != null ? AccessTools.Field(saveGameType, "force") : null;

		var savesystemPatchType = AccessTools.TypeByName("KrokoshaCasualtiesMP.SavesystemPatch");
		_savedataPathField = savesystemPatchType != null
			? AccessTools.Field(savesystemPatchType, "savedatapathreplacement")
			: null;

		_available = _saveGameForceField != null && _savedataPathField != null;
		if (!_available)
			LbLog.Warn("Capture:KrokMP", "KrokMP save reflection fields not found - SP path redirect only");
		else
			LbLog.Step("Capture:KrokMP", "KrokMP save force/path reflection resolved");
		return _available;
	}

	internal static void SetForce(bool value)
	{
		if (_saveGameForceField != null)
			_saveGameForceField.SetValue(null, value);
	}

	internal static void SetPathReplacement(string path)
	{
		if (_savedataPathField != null)
			_savedataPathField.SetValue(null, path ?? "");
	}

	internal static bool GetForce()
	{
		if (_saveGameForceField == null)
			return false;
		return _saveGameForceField.GetValue(null) is bool b && b;
	}
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
internal static class SavePathTranspiler
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var list = new List<CodeInstruction>(instructions);
		var replacement = AccessTools.Method(typeof(LeaderboardSavePath), nameof(LeaderboardSavePath.Current));
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].operand is MethodInfo mi &&
			    mi.Name == "get_persistentDataPath" &&
			    mi.DeclaringType?.FullName == "UnityEngine.Application")
			{
				list[i].operand = replacement;
			}
		}
		return list;
	}
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
internal static class VanillaSaveForcedCapturePatch
{
	private static float _lastForcedCaptureTime = -999f;
	private const float ForcedDebounceSeconds = 1f;
	private static bool _pendingSaveAndExit;
	private static bool _deferredSavePending;
	private static float _deferredSaveAt = -999f;

	internal static bool WorldIsSafeToCapture()
	{
		var world = WorldGeneration.world;
		if (world == null)
			return false;
		if (!world.worldExists)
			return false;
		if (world.generatingWorld)
			return false;
		if (world.biomeDepth < 0)
			return false;
		return true;
	}

	internal static bool TryForcedWorldCapture(string reason, bool saveAndExit = false, bool ignoreDebounce = false)
	{
		if (LeaderboardSaveCapture.IsCapturing)
			return false;
		if (LeaderboardSavePath.IsRedirectActive)
			return false;
		if (KrokMpSaveReflection.TryResolve() && KrokMpSaveReflection.GetForce())
			return false;
		if (!RunSessionManager.ShouldCapture())
			return false;
		if (!WorldIsSafeToCapture())
		{
			LbLog.Warn("Capture:Forced", $"Skipped {reason} - world is not safe to capture");
			return false;
		}
		if (!ignoreDebounce && Time.unscaledTime - _lastForcedCaptureTime < ForcedDebounceSeconds)
			return false;

		RunSessionManager.EnsureSessionForLateStart();
		if (!RunSessionManager.HasActiveSession)
			return false;

		_lastForcedCaptureTime = Time.unscaledTime;
		bool markExit = saveAndExit || _pendingSaveAndExit || string.Equals(reason, "SaveAndExit", StringComparison.Ordinal);
		LbLog.Step("Capture:Forced", $"{reason} - creating forced snapshot before terrain teardown saveAndExit={markExit}");
		return LeaderboardSaveCapture.CreateLeaderboardSave(forcedSave: true, saveAndExit: markExit);
	}

	internal static void MarkSaveAndExit()
	{
		_pendingSaveAndExit = true;
		_deferredSavePending = false;
		TryForcedWorldCapture("SaveAndExit", saveAndExit: true);
	}

	internal static void NoteVanillaSaveGame()
	{
		if (LeaderboardSaveCapture.IsCapturing)
			return;
		if (LeaderboardSavePath.IsRedirectActive)
			return;
		if (KrokMpSaveReflection.TryResolve() && KrokMpSaveReflection.GetForce())
			return;
		_deferredSaveAt = Time.unscaledTime;
		_deferredSavePending = true;
	}

	internal static void CaptureForMainMenu()
	{
		bool saveAndExit = _pendingSaveAndExit || _deferredSavePending;
		TryForcedWorldCapture("ToMainMenu", saveAndExit);
		_deferredSavePending = false;
		_pendingSaveAndExit = false;
	}

	internal static void TickDeferred()
	{
		if (!_deferredSavePending)
			return;
		if (Time.unscaledTime < _deferredSaveAt + 0.05f)
			return;
		TryForcedWorldCapture("SaveGame", saveAndExit: _pendingSaveAndExit);
		_deferredSavePending = false;
	}

	internal static void TickArrivalCapture()
	{
		var session = RunSessionManager.Current;
		if (session == null || !session.Active || !session.PendingArrivalCapture)
			return;
		if (!WorldIsSafeToCapture())
			return;
		session.PendingArrivalCapture = false;
		TryForcedWorldCapture("LayerArrival", ignoreDebounce: true);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void Prefix()
	{
		NoteVanillaSaveGame();
	}
}

[HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.SaveAndExit))]
internal static class SaveAndExitForcedCapturePatch
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void Prefix()
	{
		VanillaSaveForcedCapturePatch.MarkSaveAndExit();
	}
}
