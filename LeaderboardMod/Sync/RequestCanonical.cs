using System;
using System.Text;

namespace LeaderboardMod.Sync;

internal static class Base64Url
{
	internal static string Encode(byte[] data)
	{
		if (data == null || data.Length == 0)
			return "";
		return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}

internal static class RequestCanonical
{
	internal static string Build(
		string steamId,
		string saveId,
		int snapshotSeq,
		string timestampUtc,
		string modListSha256,
		string modVersion,
		byte[] payload)
	{
		string contentSha256 = PayloadFingerprint.ContentSha256Hex(payload);
		return string.Join("\n",
			"POST",
			"/api/sqlite",
			steamId ?? "",
			saveId ?? "",
			snapshotSeq.ToString(),
			timestampUtc ?? "",
			modListSha256 ?? "",
			modVersion ?? "",
			contentSha256);
	}
}
