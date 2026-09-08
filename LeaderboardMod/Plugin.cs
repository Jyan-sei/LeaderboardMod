using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Storage;
using LeaderboardMod.Sync;
using LeaderboardMod.Tracking.Patches;
using UnityEngine;

namespace LeaderboardMod;

[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
	private const float CaptureIntervalSeconds = 60f;

	private Harmony _harmony;
	private float _nextCaptureTime;

	internal static BepInEx.Configuration.ConfigEntry<bool> BackendUploadEnabled;
	internal static BepInEx.Configuration.ConfigEntry<string> BackendUploadUrl;
	internal static BepInEx.Configuration.ConfigEntry<string> BackendFingerprintSecret;
	internal static BepInEx.Configuration.ConfigEntry<bool> BackendCertPinningEnabled;
	internal static BepInEx.Configuration.ConfigEntry<string> BackendCertPinSha256Base64;

	private void Awake()
	{
		LbLog.Source = Logger;
		KrokMpOptional.RememberMainThread();
		LbLog.Step("Init", $"LeaderboardMod v{PluginInfo.Version} Awake");

		BackendUploadEnabled = Config.Bind("Backend", "UploadEnabled", true,
			"post leaderboard.db after each sqlite insert");
		BackendUploadUrl = Config.Bind("Backend", "UploadUrl", "https://fch-toolkit.com/api/cu/sqlite",
			"backend url for sqlite uploads");
		BackendFingerprintSecret = Config.Bind("Backend", "FingerprintSecret", "",
			"optional hmac override. empty = derive from hardware hash (must match server)");
		BackendCertPinningEnabled = Config.Bind("Backend", "CertPinningEnabled", true,
			"pin the backend tls cert");
		BackendCertPinSha256Base64 = Config.Bind("Backend", "CertPinSha256Base64",
			CertificatePinValidator.DefaultProductionCertPinBase64,
			"cert pin (base64 sha256 of leaf der). empty = prod default");

		string location = Assembly.GetExecutingAssembly().Location ?? "";
		LeaderboardPaths.Initialize(location);
		MachineIdProvider.Initialize();
		LbLog.Step("Init", $"machineId={MachineIdProvider.ShortPrefix} hash={MachineIdProvider.FullHash.Substring(0, 16)}...");
		try
		{
			BackendSync.Configure(
				BackendUploadEnabled.Value,
				BackendUploadUrl.Value,
				BackendFingerprintSecret.Value,
				BackendCertPinningEnabled.Value,
				BackendCertPinSha256Base64.Value);
		}
		catch (Exception ex)
		{
			LbLog.Error("Backend:Init", "BackendSync.Configure failed - uploads disabled until reload", ex);
		}

		try
		{
			CaptureDatabase.Initialize(location);
		}
		catch (Exception ex)
		{
			LbLog.Error("Init", "CaptureDatabase.Initialize failed - continuing without SQLite", ex);
		}

		KrokMpOptional.Resolve();
		SteamBootstrap.Initialize(location);

		_harmony = new Harmony(PluginInfo.GUID);
		try
		{
			_harmony.PatchAll(typeof(Plugin).Assembly);
			LbLog.Step("Init", "Harmony PatchAll complete");
		}
		catch (Exception ex)
		{
			LbLog.Warn("Init", $"Harmony PatchAll partial failure - continuing: {ex.Message}");
		}

		QolSaveSlotDisplayPatch.Begin(_harmony);
		KrokMpHarmonyPatches.Begin(_harmony);

		if (!RunSessionManager.ShouldCapture())
			LbLog.Warn("Init", "Running as MP client - patches loaded but capture disabled until host session");
		else
			LbLog.Step("Init", "Awake OK - capture enabled");

		// hot reload calls Awake but not always Start so kick the scheduler here too
		_nextCaptureTime = Time.unscaledTime + CaptureIntervalSeconds;
		LbLog.Step("Capture:Scheduler", $"Started on Plugin - interval={CaptureIntervalSeconds}s");
	}

	// leftover after moving the scheduler into Awake. this shit dont work as the only kick,
	// hot reload skips Start. awake already set _nextCaptureTime so this almost never fires
	private void Start()
	{
		if (_nextCaptureTime <= 0f)
			_nextCaptureTime = Time.unscaledTime + CaptureIntervalSeconds;
	}

	private void Update()
	{
		VanillaSaveForcedCapturePatch.TickDeferred();
		VanillaSaveForcedCapturePatch.TickArrivalCapture();
		PathTrailCapture.Tick();
		ElderTrailCapture.Tick();
		MpPlayerTrailCapture.Tick();
		MedicalEventCapture.Tick();
		QolSaveSlotDisplayPatch.Tick();
		SteamBootstrap.Tick();

		if (Time.unscaledTime < _nextCaptureTime)
			return;

		_nextCaptureTime = Time.unscaledTime + CaptureIntervalSeconds;

		if (!RunSessionManager.ShouldCapture())
			return;

		if (!RunSessionManager.HasActiveSession)
		{
			RunSessionManager.EnsureSessionForLateStart();
			if (!RunSessionManager.HasActiveSession)
			{
				LbLog.Warn("Capture:Scheduler", "Skipped - no active session");
				return;
			}
		}

		LbLog.Step("Capture:Scheduler", "Timer fired - creating scheduled snapshot");
		LeaderboardSaveCapture.CreateLeaderboardSave(forcedSave: false);
	}

	private void OnDestroy()
	{
		SteamBootstrap.Shutdown();
		BackendSync.Shutdown();
		RunSessionManager.EndSession();
		_harmony?.UnpatchSelf();
		LbLog.Step("Init", "OnDestroy");
	}
}
