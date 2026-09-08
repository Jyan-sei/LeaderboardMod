using System;
using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Tracking;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(ConsoleScript), "TryExecuteCommand")]
internal static class ConsolePatches
{
	[HarmonyPrefix]
	private static bool Prefix(string[] args)
	{
		if (args == null || args.Length == 0)
			return true;
		string name = ConsoleCommandPolicy.GetCommandName(args);
		if (!name.StartsWith("lb", StringComparison.OrdinalIgnoreCase))
			return true;
		RemoteRunControl.Execute(string.Join(" ", args));
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix(string[] args)
	{
		if (args != null && args.Length > 0 &&
		    ConsoleCommandPolicy.GetCommandName(args).StartsWith("lb", StringComparison.OrdinalIgnoreCase))
			return;

		if (!RunEventTracker.TryGetSession(out _))
			return;

		// used to IsWhitelisted + InvalidateLocally here. this shit dont work,
		// command already ran. we just record it now and let the server yell
		RunEventTracker.RecordConsoleCommand(args);
		LbLog.Step("Patch:Console", args != null && args.Length > 0 ? string.Join(" ", args) : "(empty)");
	}
}
