using System;
using System.Security.Cryptography;
using System.Text;
using LeaderboardMod.Identity;

namespace LeaderboardMod.Sync;

/// <summary>
/// hmac over upload metadata + sha256 of the body. key is sha256("cu-leaderboard-fp:" + hardware hash)
/// so casual sqlite edits fail and a different machine wont bind to the same session.
/// can override with Backend/FingerprintSecret (has to match LB_FINGERPRINT_SECRET on the server).
/// </summary>
internal static class PayloadFingerprint
{
	private const string KeyPrefix = "cu-leaderboard-fp:";
	private static string _secretOverride;

	internal static void Configure(string secretOverride)
	{
		_secretOverride = secretOverride;
	}

	internal static string Compute(
		byte[] payload,
		string steamId,
		string saveId,
		int snapshotSeq,
		string timestampUtc,
		string modListSha256,
		string modVersion)
	{
		string contentHash = ContentSha256Hex(payload);
		string message = string.Join("|",
			steamId ?? "",
			saveId ?? "",
			snapshotSeq.ToString(),
			timestampUtc ?? "",
			modListSha256 ?? "",
			modVersion ?? "",
			contentHash);

		using var hmac = new HMACSHA256(GetSecretKey());
		return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
	}

	internal static string ContentSha256Hex(byte[] payload)
	{
		using var sha = SHA256.Create();
		return ToHex(sha.ComputeHash(payload ?? Array.Empty<byte>()));
	}

	private static byte[] GetSecretKey()
	{
		if (!string.IsNullOrWhiteSpace(_secretOverride))
		{
			using var sha = SHA256.Create();
			return sha.ComputeHash(Encoding.UTF8.GetBytes(_secretOverride.Trim()));
		}

		string machineHash = MachineIdProvider.FullHash ?? "";
		using var derive = SHA256.Create();
		return derive.ComputeHash(Encoding.UTF8.GetBytes(KeyPrefix + machineHash));
	}

	private static string ToHex(byte[] bytes)
	{
		var sb = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
			sb.Append(b.ToString("x2"));
		return sb.ToString();
	}
}
