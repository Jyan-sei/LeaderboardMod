using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using LeaderboardMod.Logging;
using Newtonsoft.Json.Linq;

namespace LeaderboardMod.Storage;

internal static class SaveSvReader
{
	internal static JObject ReadSaveRoot(string saveSvPath)
	{
		if (string.IsNullOrEmpty(saveSvPath) || !File.Exists(saveSvPath))
			return null;

		try
		{
			byte[] raw = File.ReadAllBytes(saveSvPath);
			string json = UnzipToString(raw);
			return string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
		}
		catch (Exception ex)
		{
			LbLog.Error("Capture:Sqlite", $"Failed parsing {saveSvPath}", ex);
			return null;
		}
	}

	private static string UnzipToString(byte[] compressed)
	{
		using var input = new MemoryStream(compressed);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		gzip.CopyTo(output);
		return Encoding.UTF8.GetString(output.ToArray());
	}
}
