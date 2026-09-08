using HarmonyLib;
using LeaderboardMod.Capture;
using LeaderboardMod.Tracking;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

[HarmonyPatch(typeof(WorldGeneration), "DistributeEntities")]
internal static class TrapDistributeEntitiesPatch
{
	[HarmonyPrefix]
	private static void Prefix(bool isTrap)
	{
		TrapSpawnContext.Counting = isTrap && RunEventTracker.TryGetSession(out _);
	}

	[HarmonyFinalizer]
	private static void Finalizer()
	{
		TrapSpawnContext.Counting = false;
	}
}

[HarmonyPatch(typeof(Object), "Instantiate", typeof(Object), typeof(Vector3), typeof(Quaternion))]
internal static class TrapInstantiatePatch
{
	[HarmonyPostfix]
	private static void Postfix(Object __result)
	{
		if (!TrapSpawnContext.Counting || __result == null)
			return;
		RunEventTracker.RecordTrapSpawned();
		if (__result is GameObject go)
			TrapPositionTracker.Register(go);
		else if (__result is Component component)
			TrapPositionTracker.Register(component.gameObject);
	}
}
