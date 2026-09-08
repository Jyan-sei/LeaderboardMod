using UnityEngine;

namespace LeaderboardMod.Session;

internal static class RunEligibility
{
	internal static bool IsWorldEligible()
	{
		if (WorldGeneration.world == null)
			return false;

		if (WorldGeneration.runSettings == null)
			return false;

		if (WorldGeneration.world.biomeOverride != WorldGeneration.OverrideSceneType.None)
			return false;

		try
		{
			if (WorldGeneration.GetRunSettingBool("debugworld"))
				return false;
		}
		catch
		{
			return false;
		}

		return true;
	}

	internal static bool AllowsTracking(RunSession session)
	{
		return session != null
			&& session.Active
			&& session.TrackingEnabled
			&& IsWorldEligible();
	}

	internal static double GetPlayerTotalDepthMeters()
	{
		if (WorldGeneration.world == null)
			return -1;
		return WorldGeneration.world.PlayerTotalDepthMeters();
	}

	internal static string GetBiomeOverrideLabel()
	{
		if (WorldGeneration.world == null)
			return "Unknown";
		return WorldGeneration.world.biomeOverride.ToString();
	}
}
