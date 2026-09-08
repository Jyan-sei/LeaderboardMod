using System;
using System.IO;
using LeaderboardMod.Capture;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;

namespace LeaderboardMod.Sync;

internal static class SessionTokenStore
{
	private static string _tokenDir;

	internal static void Initialize()
	{
		_tokenDir = Path.Combine(LeaderboardPaths.GetPluginDirectory(), "session_tokens");
		Directory.CreateDirectory(_tokenDir);
	}

	internal static string GetToken(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return null;

		string path = GetTokenPath(saveId);
		if (!File.Exists(path))
			return null;

		try
		{
			string token = File.ReadAllText(path).Trim();
			return string.IsNullOrEmpty(token) ? null : token;
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:Token", $"Failed reading token for save {saveId}: {ex.Message}");
			return null;
		}
	}

	internal static void SaveToken(string saveId, string token)
	{
		if (string.IsNullOrEmpty(saveId) || string.IsNullOrEmpty(token))
			return;

		try
		{
			Directory.CreateDirectory(_tokenDir);
			File.WriteAllText(GetTokenPath(saveId), token);
			LbLog.Step("Backend:Token", $"Saved session token for save {saveId}");
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:Token", $"Failed saving token for save {saveId}: {ex.Message}");
		}
	}

	internal static void ClearToken(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		try
		{
			string path = GetTokenPath(saveId);
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:Token", $"Failed clearing token for save {saveId}: {ex.Message}");
		}
	}

	private static string GetTokenPath(string saveId)
	{
		string steam = PlayerIdentity.SanitizePathSegment(PlayerIdentity.GetSteamId());
		string save = PlayerIdentity.SanitizePathSegment(saveId);
		return Path.Combine(_tokenDir, $"{steam}_{save}.token");
	}
}
