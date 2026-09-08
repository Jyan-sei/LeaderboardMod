using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LeaderboardMod.Identity;

internal static class MachineIdProvider
{
	private const string PrefsKeyHash = "leaderboardmod.machineid.hash";
	private const string PrefsKeyPrefix = "leaderboardmod.machineid.prefix";

	internal static string FullHash { get; private set; }
	internal static string ShortPrefix { get; private set; }

	internal static void Initialize()
	{
		if (!string.IsNullOrEmpty(PlayerPrefs.GetString(PrefsKeyHash, "")))
		{
			FullHash = PlayerPrefs.GetString(PrefsKeyHash);
			ShortPrefix = PlayerPrefs.GetString(PrefsKeyPrefix);
			return;
		}

		string raw = string.Join("|",
			SystemInfo.deviceUniqueIdentifier ?? "",
			SystemInfo.processorType ?? "",
			SystemInfo.deviceModel ?? "",
			Application.persistentDataPath ?? "");

		using (var sha = SHA256.Create())
		{
			byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
			FullHash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
		}

		ShortPrefix = FullHash.Length >= 8 ? FullHash.Substring(0, 8) : FullHash;
		PlayerPrefs.SetString(PrefsKeyHash, FullHash);
		PlayerPrefs.SetString(PrefsKeyPrefix, ShortPrefix);
		PlayerPrefs.Save();
	}
}
