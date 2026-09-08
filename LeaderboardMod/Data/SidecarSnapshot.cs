using System.Collections.Generic;

namespace LeaderboardMod.Data;

internal sealed class SidecarSnapshot
{
	public string steamId;
	public string machineId;
	public string machineIdHash;
	public string timestampUtc;
	public string runStartedUtc;
	public string saveId;
	public bool isMultiplayerHosted;
	public int mpPlayerCount;
	public int mpPlayerLimit;
	public bool isSaveOurs;
	public bool isPartialRun;
	public bool forcedSave;
	public bool saveAndExit;
	public string terrainOverview; // always empty now. this shit dont work, ui reads v3
	public string terrainDetail;
	public string trapPositions;
	public string pathPositions;
	public string elderPathPositions;
	public string mpPlayerPathPositions;
	public string medicalEvents;
	public int sessionDeathCount;
	public int totalDeathCount;
	public int tradersKilled;
	public int elderThornbacksKilled;
	public int duneTradersDiscovered;
	public int experimentTradersDiscovered;
	public int milkyTradersDiscovered;
	public int jungleToDeepGravelTransitions;
	public int layerTransitionCount;
	public int totalCrafts;
	public double runTimeElapsedSeconds;
	public string firstCraftUtc;
	public string firstInjuryUtc;
	public int trapsSpawnedCurrentLayer;
	public int trapsSpawnedEver;
	public int drillPodsUsed;
	public int medicalItemsUsedOnSelf;
	public int medicalItemsUsedOnTeammates;
	public int currentBiomeDepth;
	public double layerPosX;
	public double layerPosY;
	public double layerPosXNormalized;
	public double layerPosYNormalized;
	public int layerWidth;
	public int layerHeight;
	public string biomeOverride;
	public bool debugWorld;
	public double playerTotalDepthMeters;
	public bool runTrackingEnabled;
	public List<string[]> consoleCommandsUsed;
	public Dictionary<string, object> runSettings;
	public int worldSeed;
	public string worldSeedInput;
	public bool worldSeedIsSet;
	public string difficultyPreset;
	public int difficultyPresetIndex;
	public bool difficultyIsCustom;
	public int snapshotSeq;
}
