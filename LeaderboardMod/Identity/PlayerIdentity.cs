namespace LeaderboardMod.Identity;

internal static class PlayerIdentity
{
	internal static string GetSteamId()
	{
		string steamId = SteamBootstrap.GetSteamId();
		if (!string.IsNullOrWhiteSpace(steamId))
			return steamId;
		return "LOCAL";
	}

	internal static string GetPersistentId()
	{
		return "LOCAL_" + MachineIdProvider.ShortPrefix;
	}

	internal static string SanitizePathSegment(string value)
	{
		if (string.IsNullOrEmpty(value))
			return "unknown";
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
			value = value.Replace(c, '_');
		return value;
	}
}
