using System;
using System.Collections.Generic;
using LeaderboardMod.Capture;

namespace LeaderboardMod.Session;

internal sealed class RunSession
{
	internal string SaveId;
	internal DateTime RunStartedUtc;
	internal bool IsPartialRun;
	internal bool TrackingEnabled;
	internal bool Active;

	internal int SnapshotSeq;
	internal int TradersKilled;
	internal int ElderThornbacksKilled;
	internal int DuneTradersDiscovered;
	internal int ExperimentTradersDiscovered;
	internal int MilkyTradersDiscovered;
	internal int JungleToDeepGravelTransitions;
	internal int LayerTransitionCount;
	internal int SessionDeathCount;
	internal int TotalCrafts;

	internal DateTime? FirstCraftUtc;
	internal DateTime? FirstInjuryUtc;
	internal int TrapsSpawnedCurrentLayer;
	internal int TrapsSpawnedEver;
	internal int DrillPodsUsed;
	internal int MedicalItemsUsedOnSelf;
	internal int MedicalItemsUsedOnTeammates;

	internal readonly HashSet<int> DiscoveredTraderInstanceIds = new HashSet<int>();
	internal readonly HashSet<int> KilledTraderInstanceIds = new HashSet<int>();
	internal readonly HashSet<int> KilledElderInstanceIds = new HashSet<int>();
	internal readonly List<string[]> ConsoleCommandsUsed = new List<string[]>();

	internal int LastRecordedBiomeDepth = -1;
	internal bool ExpectingLayerTransition;
	internal bool PendingArrivalCapture;
	internal int PendingOldBiomeDepth;
	internal int TerrainStoredForBiomeDepth = int.MinValue;
	internal bool TerrainKeepIncludedRemotes;
	internal int TerrainStoredPathCount;
	internal int PathTrailBiomeDepth = int.MinValue;
	internal double LastPathBlockX = double.NaN;
	internal double LastPathBlockY = double.NaN;
	internal bool? LastPathAlive;
	internal readonly List<PathSample> LayerPath = new List<PathSample>();
	internal readonly List<PathSample> PendingPathFlush = new List<PathSample>();
	internal readonly List<MedicalEventItem> PendingMedicalEvents = new List<MedicalEventItem>();
	internal int ElderTrailBiomeDepth = int.MinValue;
	internal readonly Dictionary<int, ElderTrailState> ElderTrails = new Dictionary<int, ElderTrailState>();
	internal int MpPlayerTrailBiomeDepth = int.MinValue;
	internal readonly Dictionary<ulong, ElderTrailState> MpPlayerTrails = new Dictionary<ulong, ElderTrailState>();
}
