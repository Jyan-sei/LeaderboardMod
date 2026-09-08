using System;
using System.IO;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeaderboardMod.Session;

/// <summary>
/// console cmds: lbnew | lbload [1-3|autosave] | lbcapture | lbquit | lbstatus
/// </summary>
internal static class RemoteRunControl
{
	internal static string Execute(string raw)
	{
		string command = (raw ?? "").Trim();
		if (string.IsNullOrEmpty(command))
			return "empty command";

		string[] parts = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		string verb = parts[0].ToLowerInvariant();
		if (verb.StartsWith("lb", StringComparison.Ordinal))
			verb = verb.Substring(2);

		LbLog.Step("Remote", $"Execute '{command}' scene={SceneManager.GetActiveScene().name}");

		try
		{
			switch (verb)
			{
				case "new":
					return StartNewRun();
				case "load":
					return LoadSlot(parts.Length > 1 ? parts[1] : "1");
				case "capture":
					return ForceCapture();
				case "disablemp":
					return DisableMpAndRestart();
				case "quit":
					Application.Quit();
					return "quitting";
				case "status":
					return DescribeWorld();
				default:
					return $"unknown command '{command}'";
			}
		}
		catch (Exception ex)
		{
			LbLog.Error("Remote", $"Execute failed: {command}", ex);
			return ex.Message;
		}
	}

	private static bool KrokMpBlocksSingleplayer()
	{
		return KrokMpOptional.SuccessfullyInitialized;
	}

	private static string DisableMpAndRestart()
	{
		PlayerPrefs.SetInt("didbasiccourse", 1);
		PlayerPrefs.SetInt("tutorial", 0);
		PlayerPrefs.SetInt("KrokoshaCasualtiesMP_FORCE_DISABLE_MP_MOD", 1);
		PlayerPrefs.Save();
		Application.Quit();
		return "restarting";
	}

	private static string StartNewRun()
	{
		PlayerPrefs.SetInt("didbasiccourse", 1);
		PlayerPrefs.SetInt("tutorial", 0);
		PlayerPrefs.Save();

		if (KrokMpBlocksSingleplayer())
			return DisableMpAndRestart();

		if (PreRunScript.instance != null)
		{
			if (PreRunScript.instance.runSettings == null)
				PreRunScript.instance.runSettings = new System.Collections.Generic.Dictionary<string, object>(
					RunSettings.GetPreset("normal").presetValues);
			PreRunScript.instance.StartRun();
			return "started";
		}

		SaveSystem.loadedRun = false;
		WorldGeneration.runSettings = RunSettings.GetPreset("normal").presetValues;
		SceneManager.LoadScene("SampleScene");
		return "started";
	}

	private static string LoadSlot(string slotToken)
	{
		if (KrokMpBlocksSingleplayer())
			return DisableMpAndRestart();

		string fileName = ResolveSlotFile(slotToken);
		string folder = Application.persistentDataPath;
		string source = Path.Combine(folder, fileName);
		string dest = Path.Combine(folder, "save.sv");
		if (!File.Exists(source))
			return $"missing {source}";

		File.Copy(source, dest, overwrite: true);
		SaveSystem.loadedRun = true;

		if (PreRunScript.instance != null)
		{
			PreRunScript.instance.LoadRun();
			return $"loading {fileName}";
		}

		SceneManager.LoadScene("SampleScene");
		return $"loading {fileName}";
	}

	private static string ResolveSlotFile(string token)
	{
		if (string.Equals(token, "autosave", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(token, "a", StringComparison.OrdinalIgnoreCase))
			return "autosave.sv";
		if (int.TryParse(token, out int n) && n >= 1 && n <= 3)
			return $"slot_{n}.sv";
		if (token.EndsWith(".sv", StringComparison.OrdinalIgnoreCase))
			return token;
		return $"slot_{token}.sv";
	}

	private static string ForceCapture()
	{
		if (!RunSessionManager.ShouldCapture() || !RunSessionManager.HasActiveSession)
			return "no active capturable session";
		bool ok = LeaderboardSaveCapture.CreateLeaderboardSave(forcedSave: true);
		return ok ? "captured" : "CreateLeaderboardSave failed";
	}

	private static string DescribeWorld()
	{
		var session = RunSessionManager.Current;
		return
			$"scene={SceneManager.GetActiveScene().name} " +
			$"body={(PlayerCamera.main != null && PlayerCamera.main.body != null)} " +
			$"mpHosted={RunSessionManager.IsMultiplayerHosted()} " +
			$"session={(session == null ? "none" : session.SaveId)} " +
			$"tracking={(session != null && session.TrackingEnabled)} " +
			$"seq={(session == null ? 0 : session.SnapshotSeq)}";
	}
}
