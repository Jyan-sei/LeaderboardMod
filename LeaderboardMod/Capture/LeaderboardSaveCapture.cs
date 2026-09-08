using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LeaderboardMod.Data;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Storage;
using Newtonsoft.Json;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal static class LeaderboardSaveCapture
{
	private static int _captureDepth;

	internal static bool IsCapturing => _captureDepth > 0;

	internal static bool CreateLeaderboardSave(bool forcedSave = false, int adjustTotalDeathCount = 0, bool saveAndExit = false)
	{
		LbLog.Step("Capture:Begin", forcedSave
			? "CreateLeaderboardSave called (forcedSave=true)"
			: "CreateLeaderboardSave called (scheduled)");

		if (!RunSessionManager.ShouldCapture())
		{
			LbLog.Warn("Capture:Skip", "ShouldCapture=false");
			return false;
		}

		RunSessionManager.EnsureSessionForLateStart();
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
		{
			LbLog.Warn("Capture:Skip", "No active session");
			return false;
		}

		if (PlayerCamera.main == null || PlayerCamera.main.body == null)
		{
			LbLog.Warn("Capture:Skip", "PlayerCamera.main.body missing");
			return false;
		}

		if (WorldGeneration.world == null)
		{
			LbLog.Warn("Capture:Skip", "WorldGeneration.world missing");
			return false;
		}

		if (WorldGeneration.world.biomeDepth < 0 || WorldGeneration.world.generatingWorld || !WorldGeneration.world.worldExists)
		{
			LbLog.Warn("Capture:Skip",
				$"World not ready biomeDepth={WorldGeneration.world.biomeDepth} " +
				$"generating={WorldGeneration.world.generatingWorld} exists={WorldGeneration.world.worldExists}");
			return false;
		}

		if (!RunEligibility.AllowsTracking(session))
		{
			LbLog.Warn("Capture:Skip",
				$"Tracking not allowed partial={session.IsPartialRun} tracking={session.TrackingEnabled} " +
				$"world={RunEligibility.GetBiomeOverrideLabel()}");
			return false;
		}

		session.SnapshotSeq++;
		string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		string machineId = MachineIdProvider.ShortPrefix;
		string runDir = LeaderboardPaths.GetRunDirectory(session.SaveId);
		Directory.CreateDirectory(runDir);

		string svName = $"sv_{machineId}_{timestamp}.sv";
		string jsonName = $"data_{machineId}_{timestamp}.json";
		string tempDir = Path.Combine(Path.GetTempPath(), "LB_" + Guid.NewGuid().ToString("N"));

		_captureDepth++;
		try
		{
			Directory.CreateDirectory(tempDir);
			LbLog.Step("Capture:TempDir", tempDir);

			bool krok = KrokMpSaveReflection.TryResolve();
			if (krok)
			{
				KrokMpSaveReflection.SetPathReplacement(tempDir);
				KrokMpSaveReflection.SetForce(true);
			}

			LeaderboardSavePath.Push(tempDir);
			try
			{
				LbLog.Step("Capture:SaveGame", "Calling SaveSystem.SaveGame()");
				SaveSystem.SaveGame();
			}
			finally
			{
				LeaderboardSavePath.Pop();
				if (krok)
				{
					KrokMpSaveReflection.SetForce(false);
					KrokMpSaveReflection.SetPathReplacement("");
				}
			}

			string tempSave = Path.Combine(tempDir, "save.sv");
			if (!File.Exists(tempSave))
			{
				LbLog.Error("Capture:Fail", "save.sv not written to temp dir");
				return false;
			}

			string finalSv = Path.Combine(runDir, svName);
			File.Copy(tempSave, finalSv, overwrite: true);
			LbLog.Step("Capture:SaveFile", finalSv);

			var sidecar = BuildSidecar(session, forcedSave, adjustTotalDeathCount, saveAndExit);
			string finalJson = Path.Combine(runDir, jsonName);
			File.WriteAllText(finalJson, JsonConvert.SerializeObject(sidecar, Formatting.Indented));
			LbLog.Step("Capture:Sidecar", $"{finalJson} forcedSave={forcedSave}");

			string terrainName = $"terrain_{machineId}_{timestamp}.bin";
			TerrainOverviewCapture.WritePackedFile(Path.Combine(runDir, terrainName), sidecar.terrainDetail);

			CaptureDatabase.InsertCapture(session, sidecar, finalSv, finalJson);

			return true;
		}
		catch (Exception ex)
		{
			LbLog.Error("Capture:Fail", "CreateLeaderboardSave exception", ex);
			return false;
		}
		finally
		{
			_captureDepth--;
			try
			{
				if (Directory.Exists(tempDir))
					Directory.Delete(tempDir, recursive: true);
			}
			catch (Exception ex)
			{
				LbLog.Warn("Capture:Cleanup", ex.Message);
			}
		}
	}

	private static SidecarSnapshot BuildSidecar(RunSession session, bool forcedSave, int adjustTotalDeathCount = 0, bool saveAndExit = false)
	{
		var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
		LayerPositionCapture.TryCapture(body, out LayerPositionSnapshot layerPos);

		var seed = WorldSeedCapture.Capture();
		var difficulty = DifficultyPresetCapture.Capture();
		string pathPositions = PathTrailCapture.Flush(session);
		string elderPathPositions = ElderTrailCapture.Flush(session);
		string mpPlayerPathPositions = MpPlayerTrailCapture.Flush(session);
		string medicalEvents = MedicalEventCapture.Flush(session);
		bool mpHosted = RunSessionManager.IsMultiplayerHosted();
		int mpCount = RunSessionManager.GetMpPlayerCount();
		int mpLimit = RunSessionManager.GetMpPlayerLimit();
		LbLog.Step("Capture:MP",
			$"hosted={mpHosted} count={mpCount} limit={mpLimit} remotes={session.MpPlayerTrails.Count} trailBytes={mpPlayerPathPositions.Length}");
		TerrainOverviewCapture.TryCaptureLayerOnce(session, body, out string terrainDetail, out string trapPositions);

		return new SidecarSnapshot
		{
			steamId = PlayerIdentity.GetSteamId(),
			machineId = MachineIdProvider.ShortPrefix,
			machineIdHash = MachineIdProvider.FullHash,
			timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
			runStartedUtc = session.RunStartedUtc.ToString("o", CultureInfo.InvariantCulture),
			saveId = session.SaveId,
			isMultiplayerHosted = mpHosted,
			mpPlayerCount = mpCount,
			mpPlayerLimit = mpLimit,
			isSaveOurs = RunSessionManager.IsSaveOurs(),
			isPartialRun = session.IsPartialRun,
			forcedSave = forcedSave,
			saveAndExit = saveAndExit,
			// overview blob used to live here. this shit dont work at ui scale, v3 lod replaced it.
			// column stays empty so old db rows dont explode
			terrainOverview = "",
			terrainDetail = terrainDetail,
			trapPositions = trapPositions,
			pathPositions = pathPositions,
			elderPathPositions = elderPathPositions,
			mpPlayerPathPositions = mpPlayerPathPositions,
			medicalEvents = medicalEvents,
			sessionDeathCount = session.SessionDeathCount,
			totalDeathCount = PlayerPrefs.GetInt("deathcount", 0) + adjustTotalDeathCount,
			tradersKilled = session.TradersKilled,
			elderThornbacksKilled = session.ElderThornbacksKilled,
			duneTradersDiscovered = session.DuneTradersDiscovered,
			experimentTradersDiscovered = session.ExperimentTradersDiscovered,
			milkyTradersDiscovered = session.MilkyTradersDiscovered,
			jungleToDeepGravelTransitions = session.JungleToDeepGravelTransitions,
			layerTransitionCount = session.LayerTransitionCount,
			totalCrafts = session.TotalCrafts,
			runTimeElapsedSeconds = RunMetrics.GetSessionRunTimeElapsedSeconds(session.RunStartedUtc),
			firstCraftUtc = RunMetrics.FormatUtcTimestamp(session.FirstCraftUtc),
			firstInjuryUtc = RunMetrics.FormatUtcTimestamp(session.FirstInjuryUtc),
			trapsSpawnedCurrentLayer = session.TrapsSpawnedCurrentLayer,
			trapsSpawnedEver = session.TrapsSpawnedEver,
			drillPodsUsed = session.DrillPodsUsed,
			medicalItemsUsedOnSelf = session.MedicalItemsUsedOnSelf,
			medicalItemsUsedOnTeammates = session.MedicalItemsUsedOnTeammates,
			currentBiomeDepth = WorldGeneration.world != null ? WorldGeneration.world.biomeDepth : -1,
			layerPosX = layerPos.BlockX,
			layerPosY = layerPos.BlockY,
			layerPosXNormalized = layerPos.NormalizedX,
			layerPosYNormalized = layerPos.NormalizedY,
			layerWidth = layerPos.LayerWidth,
			layerHeight = layerPos.LayerHeight,
			biomeOverride = RunEligibility.GetBiomeOverrideLabel(),
			debugWorld = WorldGeneration.GetRunSettingBool("debugworld"),
			playerTotalDepthMeters = RunEligibility.GetPlayerTotalDepthMeters(),
			runTrackingEnabled = session.TrackingEnabled,
			consoleCommandsUsed = new List<string[]>(session.ConsoleCommandsUsed),
			runSettings = CopyRunSettings(),
			worldSeed = seed.Seed,
			worldSeedInput = seed.Input,
			worldSeedIsSet = seed.IsSeeded,
			difficultyPreset = difficulty.PresetName,
			difficultyPresetIndex = difficulty.PresetIndex,
			difficultyIsCustom = difficulty.IsCustom,
			snapshotSeq = session.SnapshotSeq
		};
	}

	private static Dictionary<string, object> CopyRunSettings()
	{
		var result = new Dictionary<string, object>();
		if (WorldGeneration.runSettings == null)
			return result;
		foreach (var kv in WorldGeneration.runSettings)
		{
			if (string.Equals(kv.Key, LeaderboardSaveId.SaveFieldName, StringComparison.Ordinal))
				continue;
			result[kv.Key] = kv.Value;
		}
		return result;
	}
}
