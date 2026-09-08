using System;
using BepInEx.Logging;

namespace LeaderboardMod.Logging;

internal static class LbLog
{
	internal static ManualLogSource Source;

	internal static void Step(string step, string message)
	{
		Source?.LogInfo($"[{step}] {message}");
	}

	internal static void Warn(string step, string message)
	{
		Source?.LogWarning($"[{step}] {message}");
	}

	internal static void Error(string step, string message, Exception ex = null)
	{
		if (ex != null)
			Source?.LogError($"[{step}] {message}\n{ex}");
		else
			Source?.LogError($"[{step}] {message}");
	}
}
