using System;
using System.Collections.Generic;

namespace LeaderboardMod.Session;

internal static class ConsoleCommandPolicy
{
	// leftover from when we killed runs client-side for console cmds. this shit dont work,
	// vanilla already ran the command. server invalidates now. list stays so i remember what was "ok"
	private static readonly HashSet<string> Whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"saveandquit",
		"log",
		"lbnew",
		"lbload",
		"lbcapture",
		"lbquit",
		"lbstatus",
		"lbdisablemp",
	};

	internal static string GetCommandName(string[] args)
	{
		if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
			return string.Empty;

		return args[0].Trim();
	}

	// nobody calls this anymore. this shit dont work as a gate, leaving it so i dont rewire it
	internal static bool IsWhitelisted(string[] args)
	{
		string name = GetCommandName(args);
		if (string.IsNullOrEmpty(name))
			return true;

		return Whitelist.Contains(name);
	}
}
