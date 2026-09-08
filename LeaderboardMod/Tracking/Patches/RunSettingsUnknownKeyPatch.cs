using System.Collections.Generic;
using HarmonyLib;
using LeaderboardMod.Logging;

namespace LeaderboardMod.Tracking.Patches;

/// <summary>
/// vanilla GetSetting uses First() and blows up on unknown names.
/// TupleListToDic / qol restore / krokmp worldgen all hit this.
/// return null so leftover keys (old leaderboardSaveId, other mods) get skipped.
/// </summary>
[HarmonyPatch(typeof(RunSettings), nameof(RunSettings.GetSetting))]
internal static class RunSettingsUnknownKeyPatch
{
	private static readonly HashSet<string> LoggedUnknown = new HashSet<string>();

	[HarmonyPrefix]
	private static bool Prefix(string name, ref RunSetting __result)
	{
		List<RunSetting> types = RunSettings.settingTypes;
		if (types != null)
		{
			for (int i = 0; i < types.Count; i++)
			{
				RunSetting setting = types[i];
				if (setting != null && setting.name == name)
				{
					__result = setting;
					return false;
				}
			}
		}

		__result = null;
		if (!string.IsNullOrEmpty(name) && LoggedUnknown.Add(name))
			LbLog.Step("RunSettings:Skip", $"unknown key '{name}' - skipped (vanilla First would throw)");

		return false;
	}
}
