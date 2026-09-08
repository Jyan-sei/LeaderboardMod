using System;
using System.Collections.Generic;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
namespace LeaderboardMod.Tracking;

internal static class RunEventTracker
{
	internal static RunSession Session => RunSessionManager.Current;

	internal static bool TryGetSession(out RunSession session)
	{
		session = RunSessionManager.Current;
		return session != null && session.Active && RunSessionManager.ShouldCapture();
	}

	internal static void RecordConsoleCommand(string[] args)
	{
		if (!TryGetSession(out var session))
			return;
		if (args == null || args.Length == 0)
			return;
		var copy = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			copy[i] = args[i];
		session.ConsoleCommandsUsed.Add(copy);
	}

	internal static void RecordTraderDiscovery(int instanceId, int character)
	{
		if (!TryGetSession(out var session))
			return;
		if (!session.DiscoveredTraderInstanceIds.Add(instanceId))
			return;

		switch (character)
		{
			case 0: session.ExperimentTradersDiscovered++; break;
			case 1: session.MilkyTradersDiscovered++; break;
			case 2: session.DuneTradersDiscovered++; break;
		}
		LbLog.Step("Patch:Trader", $"Discovered character={character} (experiment={session.ExperimentTradersDiscovered} milky={session.MilkyTradersDiscovered} dune={session.DuneTradersDiscovered})");
	}

	internal static void RecordTraderKill(int instanceId)
	{
		if (!TryGetSession(out var session))
			return;
		if (!session.KilledTraderInstanceIds.Add(instanceId))
			return;
		session.TradersKilled++;
		LbLog.Step("Patch:Trader", $"Kill recorded (total={session.TradersKilled})");
	}

	internal static void RecordElderKill(int instanceId)
	{
		if (!TryGetSession(out var session))
			return;
		if (!session.KilledElderInstanceIds.Add(instanceId))
			return;
		session.ElderThornbacksKilled++;
	}

	internal static void RecordSessionDeath()
	{
		if (!TryGetSession(out var session))
			return;
		session.SessionDeathCount++;
	}

	internal static void RecordCraft(Recipe recipe)
	{
		if (!TryGetSession(out var session))
			return;
		session.TotalCrafts++;
		if (!session.FirstCraftUtc.HasValue)
		{
			session.FirstCraftUtc = DateTime.UtcNow;
			LbLog.Step("Patch:Craft", $"First craft at {session.FirstCraftUtc:o}");
		}
		LbLog.Step("Patch:Craft", $"Successful craft recorded (total={session.TotalCrafts}, recipeIndex={recipe?.index})");
	}

	internal static void RecordFirstInjury()
	{
		if (!TryGetSession(out var session))
			return;
		if (session.FirstInjuryUtc.HasValue)
			return;
		session.FirstInjuryUtc = DateTime.UtcNow;
		LbLog.Step("Patch:Injury", $"First injury at {session.FirstInjuryUtc:o}");
	}

	internal static void RecordTrapSpawned()
	{
		if (!TryGetSession(out var session))
			return;
		session.TrapsSpawnedCurrentLayer++;
		session.TrapsSpawnedEver++;
	}

	internal static void ResetTrapsForNewLayer()
	{
		if (!TryGetSession(out var session))
			return;
		session.TrapsSpawnedCurrentLayer = 0;
		LeaderboardMod.Capture.TrapPositionTracker.ResetLayer();
		LbLog.Step("Patch:Trap", "Layer regen - reset trapsSpawnedCurrentLayer");
	}

	internal static void RecordDrillPodUsed()
	{
		if (!TryGetSession(out var session))
			return;
		session.DrillPodsUsed++;
		LbLog.Step("Patch:DrillPod", $"Drill pod used (total={session.DrillPodsUsed})");
	}

	internal static void RecordMedicalUsedOnSelf(Item item)
	{
		if (!TryGetSession(out var session))
			return;
		session.MedicalItemsUsedOnSelf++;
		LbLog.Step("Patch:Medical", $"Medical used on self (total={session.MedicalItemsUsedOnSelf}, item={item?.id})");
	}

	internal static void RecordMedicalUsedOnTeammate(Item item)
	{
		if (!TryGetSession(out var session))
			return;
		session.MedicalItemsUsedOnTeammates++;
		LbLog.Step("Patch:Medical", $"Medical used on teammate (total={session.MedicalItemsUsedOnTeammates}, item={item?.id})");
	}

	internal static void RecordLayerTransition(int oldDepth, int newDepth)
	{
		if (!TryGetSession(out var session))
			return;
		session.LayerTransitionCount++;
		if (oldDepth == 0 && newDepth == 1)
			session.JungleToDeepGravelTransitions++;
		session.LastRecordedBiomeDepth = newDepth;
	}
}
