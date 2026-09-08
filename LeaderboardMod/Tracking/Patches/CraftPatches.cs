using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Tracking;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(Recipe), nameof(Recipe.TryMake))]
internal static class CraftTryMakePatch
{
	[HarmonyPrefix]
	private static void Prefix(Recipe __instance, ref bool __state)
	{
		__state = __instance.GetItemsForRecipe() != null;
	}

	[HarmonyPostfix]
	private static void Postfix(Recipe __instance, bool __state)
	{
		if (!__state)
			return;
		if (KrokMpOptional.NetworkSystemRunning)
			return;
		if (!RunEventTracker.TryGetSession(out _))
			return;
		RunEventTracker.RecordCraft(__instance);
	}
}
