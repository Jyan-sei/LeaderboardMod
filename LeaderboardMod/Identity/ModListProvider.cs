using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using Newtonsoft.Json;
using LeaderboardMod.Sync;

namespace LeaderboardMod.Identity;

internal static class ModListProvider
{
	internal sealed class ModEntry
	{
		public string guid;
		public string name;
		public string version;
	}

	internal static string GetHeaderJson()
	{
		return GetHeaderWithFingerprint().Json;
	}

	internal static string GetSha256Hex()
	{
		return GetHeaderWithFingerprint().Sha256Hex;
	}

	/// <summary>
	/// json + sha256 of the same bytes we send in X-Mod-List (server hashes the raw header)
	/// </summary>
	internal static (string Json, string Sha256Hex) GetHeaderWithFingerprint()
	{
		string json = JsonConvert.SerializeObject(GetEntries());
		string sha256 = PayloadFingerprint.ContentSha256Hex(Encoding.UTF8.GetBytes(json));
		return (json, sha256);
	}

	internal static List<ModEntry> GetEntries()
	{
		if (Chainloader.PluginInfos == null || Chainloader.PluginInfos.Count == 0)
		{
			return new List<ModEntry>
			{
				new ModEntry
				{
					guid = PluginInfo.GUID,
					name = PluginInfo.Name,
					version = PluginInfo.Version,
				},
			};
		}

		return Chainloader.PluginInfos.Values
			.Where(plugin => plugin?.Metadata != null)
			.Select(plugin => new ModEntry
			{
				guid = plugin.Metadata.GUID ?? "",
				name = plugin.Metadata.Name ?? "",
				version = plugin.Metadata.Version?.ToString() ?? "",
			})
			.Where(entry => !string.IsNullOrWhiteSpace(entry.guid))
			.OrderBy(entry => entry.guid, StringComparer.Ordinal)
			.ToList();
	}
}
