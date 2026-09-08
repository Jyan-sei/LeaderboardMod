using System;
using System.Globalization;

namespace LeaderboardMod.Capture;

internal static class RunMetrics
{
	/// <summary>
	/// vanilla cumulative run time (savedRunTime + realTimeElapsed).
	/// survives mp slot loads so dont use it for session timing.
	/// </summary>
	internal static float GetVanillaRunTimeElapsedSeconds()
	{
		if (WorldGeneration.world == null)
			return 0f;
		return SaveSystem.savedRunTime + WorldGeneration.world.realTimeElapsed;
	}

	/// <summary>
	/// seconds since this mod session started (StartRun), ignores mp slot history
	/// </summary>
	internal static float GetSessionRunTimeElapsedSeconds(DateTime runStartedUtc)
	{
		if (runStartedUtc == default)
			return 0f;
		var elapsed = (DateTime.UtcNow - runStartedUtc).TotalSeconds;
		return elapsed > 0 ? (float)elapsed : 0f;
	}

	internal static string FormatUtcTimestamp(DateTime? utc)
	{
		if (!utc.HasValue)
			return null;
		return utc.Value.ToString("o", CultureInfo.InvariantCulture);
	}
}
