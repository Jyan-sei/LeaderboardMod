using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;

namespace LeaderboardMod.Capture;

internal static class LeaderboardPaths
{
	private static string _pluginDir;

	internal static void Initialize(string pluginAssemblyLocation)
	{
		_pluginDir = Path.GetDirectoryName(pluginAssemblyLocation) ?? "";
		LbLog.Step("Paths:Init", $"pluginDir={_pluginDir}");
	}

	internal static string GetRunDirectory(string saveId)
	{
		string steam = PlayerIdentity.SanitizePathSegment(PlayerIdentity.GetSteamId());
		string save = PlayerIdentity.SanitizePathSegment(saveId);
		return Path.Combine(_pluginDir, "runs", steam, save);
	}

	internal static string GetPluginDirectory() => _pluginDir ?? "";
}

internal static class LeaderboardSavePath
{
	private static readonly Stack<string> Stack = new Stack<string>();

	internal static void Push(string directory)
	{
		Stack.Push(directory ?? "");
	}

	internal static void Pop()
	{
		if (Stack.Count > 0)
			Stack.Pop();
	}

	internal static string Current()
	{
		if (Stack.Count > 0)
			return Stack.Peek();
		return UnityEngine.Application.persistentDataPath;
	}

	internal static bool IsRedirectActive => Stack.Count > 0;
}
