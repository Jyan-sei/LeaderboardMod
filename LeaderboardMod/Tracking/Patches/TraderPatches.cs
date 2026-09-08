using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Tracking;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(TraderScript), "OnWillRenderObject")]
internal static class TraderPatches
{
	[HarmonyPostfix]
	private static void Postfix(TraderScript __instance)
	{
		if (!RunEventTracker.TryGetSession(out _))
			return;

		var build = __instance.GetComponent<BuildingEntity>();
		if (build == null)
			return;

		int id = __instance.GetInstanceID();

		if (build.health > 200f)
		{
			RunEventTracker.RecordTraderDiscovery(id, __instance.character);
			return;
		}

		RunEventTracker.RecordTraderKill(id);
	}
}

[HarmonyPatch(typeof(TraderScript), "MeetPlayer")]
internal static class TraderMeetPlayerPatch
{
	[HarmonyPostfix]
	private static void Postfix(TraderScript __instance)
	{
		if (!RunEventTracker.TryGetSession(out _))
			return;
		var build = __instance.GetComponent<BuildingEntity>();
		if (build == null || build.health <= 200f)
			return;
		RunEventTracker.RecordTraderDiscovery(__instance.GetInstanceID(), __instance.character);
	}
}

[HarmonyPatch(typeof(PlayerCamera), "ToggleTradeMenu")]
internal static class TraderTradeMenuPatch
{
	[HarmonyPostfix]
	private static void Postfix(PlayerCamera __instance)
	{
		if (!RunEventTracker.TryGetSession(out _))
			return;
		if (__instance.currentTrader == null)
			return;
		var trader = __instance.currentTrader.GetComponent<TraderScript>();
		if (trader == null)
			return;
		var build = trader.GetComponent<BuildingEntity>();
		if (build == null || build.health <= 200f)
			return;
		RunEventTracker.RecordTraderDiscovery(trader.GetInstanceID(), trader.character);
		LbLog.Step("Patch:Trader", $"Trade menu opened - character={trader.character}");
	}
}
