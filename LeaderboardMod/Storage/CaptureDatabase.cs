using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LeaderboardMod.Capture;
using LeaderboardMod.Data;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLitePCL;

namespace LeaderboardMod.Storage;

internal static class CaptureDatabase
{
	private static string _dbPath;
	private static bool _initialized;

	internal static bool IsInitialized => _initialized;
	internal static string DatabasePath => _dbPath;

	private static readonly string[] SidecarScalarColumns =
	{
		"steam_id", "machine_id", "machine_id_hash", "timestamp_utc", "run_started_utc", "save_id",
		"is_multiplayer_hosted", "mp_player_count", "mp_player_limit",
		"is_save_ours", "is_partial_run", "forced_save", "save_and_exit",
		"terrain_overview", "terrain_detail", "trap_positions", "path_positions", "elder_path_positions",
		"mp_player_path_positions", "medical_events",
		"session_death_count", "total_death_count", "traders_killed", "elder_thornbacks_killed",
		"dune_traders_discovered", "experiment_traders_discovered", "milky_traders_discovered",
		"jungle_to_deep_gravel_transitions", "layer_transition_count", "total_crafts",
		"run_time_elapsed_seconds", "first_craft_utc", "first_injury_utc",
		"traps_spawned_current_layer", "traps_spawned_ever", "drill_pods_used",
		"medical_items_used_on_self", "medical_items_used_on_teammates", "current_biome_depth",
		"layer_pos_x", "layer_pos_y", "layer_pos_x_normalized", "layer_pos_y_normalized",
		"layer_width", "layer_height",
		"biome_override", "debug_world", "player_total_depth_meters", "run_tracking_enabled",
		"world_seed", "world_seed_input", "world_seed_is_set",
		"difficulty_preset", "difficulty_preset_index", "difficulty_is_custom",
		"snapshot_seq", "console_commands_used_json", "run_settings_json",
		"sv_leaderboard_save_id"
	};

	private static readonly string[] SaveScalarColumns =
	{
		"sv_biome", "sv_total_traveled", "sv_run_time", "sv_c_height", "sv_c_age", "sv_c_id", "sv_c_ver",
		"sv_trap_rarity", "sv_loot_rarity", "sv_calories_consumed"
	};

	private static readonly string[] SaveJsonColumns =
	{
		"sv_last_happiness_json", "sv_saved_recipe_data_json", "sv_run_settings_json",
		"sv_body_json", "sv_limbs_json", "sv_items_json", "sv_item_components_json",
		"sv_body_components_json", "sv_limb_components_json"
	};

	internal static void Initialize(string pluginAssemblyLocation)
	{
		if (_initialized)
			return;

		try
		{
			string pluginDir = Path.GetDirectoryName(pluginAssemblyLocation) ?? "";
			SqliteBootstrap.Initialize(pluginDir);
			_dbPath = Path.Combine(pluginDir, "leaderboard.db");
			Directory.CreateDirectory(pluginDir);

			using var conn = OpenConnection();
			EnsureRegistryTable(conn);
			_initialized = true;
			LbLog.Step("Sqlite:Init", _dbPath);
		}
		catch (Exception ex)
		{
			LbLog.Error("Sqlite:Init", "SQLite unavailable - file captures will still work", ex);
		}
	}

	internal static void EnsureSessionTable(RunSession session)
	{
		if (session == null || !_initialized)
			return;

		string tableName = GetSessionTableName(session);
		using var conn = OpenConnection();
		if (TableExists(conn, tableName))
			return;

		var columns = new List<string>
		{
			"row_id INTEGER PRIMARY KEY AUTOINCREMENT",
			"sv_file TEXT NOT NULL",
			"json_file TEXT NOT NULL"
		};
		columns.AddRange(SidecarScalarColumns.Select(c => $"{c} TEXT"));
		columns.AddRange(SaveScalarColumns.Select(c => $"{c} REAL"));
		columns.AddRange(SaveJsonColumns.Select(c => $"{c} TEXT"));

		string ddl = $"CREATE TABLE {QuoteIdent(tableName)} ({string.Join(", ", columns)})";
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = ddl;
			cmd.ExecuteNonQuery();
		}

		RegisterSession(conn, tableName, session);
		LbLog.Step("Sqlite:Table", $"Created {tableName}");
	}

	internal static void ResetSessionTableForFreshRun(RunSession session)
	{
		if (session == null || !_initialized)
			return;

		string tableName = GetSessionTableName(session);
		using var conn = OpenConnection();
		if (!TableExists(conn, tableName))
			return;

		using (var delete = conn.CreateCommand())
		{
			delete.CommandText = $"DELETE FROM {QuoteIdent(tableName)}";
			delete.ExecuteNonQuery();
		}

		using (var update = conn.CreateCommand())
		{
			update.CommandText =
				"UPDATE lb_sessions SET run_started_utc = $started WHERE table_name = $table";
			update.Parameters.AddWithValue("$started",
				session.RunStartedUtc.ToString("o", CultureInfo.InvariantCulture));
			update.Parameters.AddWithValue("$table", tableName);
			update.ExecuteNonQuery();
		}

		LbLog.Step("Sqlite:Reset", $"Cleared snapshot rows in {tableName} for fresh run");
	}

	internal static void InsertCapture(RunSession session, SidecarSnapshot sidecar, string svPath, string jsonPath)
	{
		if (session == null || sidecar == null || !_initialized)
			return;

		EnsureSessionTable(session);
		string tableName = GetSessionTableName(session);
		JObject saveRoot = SaveSvReader.ReadSaveRoot(svPath);
		if (saveRoot == null)
		{
			LbLog.Warn("Capture:Sqlite", "Skipping row - save.sv could not be parsed");
			return;
		}

		var values = BuildRowValues(sidecar, svPath, jsonPath, saveRoot);
		string columnList = string.Join(", ", values.Keys.Select(QuoteIdent));
		string paramList = string.Join(", ", values.Keys.Select(k => "$" + k));

		using var conn = OpenConnection();
		EnsureSidecarColumns(conn, tableName);
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"INSERT INTO {QuoteIdent(tableName)} ({columnList}) VALUES ({paramList})";
		foreach (var kv in values)
			cmd.Parameters.AddWithValue("$" + kv.Key, kv.Value ?? DBNull.Value);
		cmd.ExecuteNonQuery();

		LbLog.Step("Capture:Sqlite", $"Inserted row into {tableName} seq={sidecar.snapshotSeq}");
		LeaderboardMod.Sync.BackendSync.QueueUpload(session.SaveId, sidecar.snapshotSeq, sidecar.timestampUtc);
	}

	internal static void CopyDatabaseTo(string destinationPath)
	{
		if (!_initialized || string.IsNullOrEmpty(_dbPath))
			throw new InvalidOperationException("SQLite not initialized");

		string directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		if (File.Exists(destinationPath))
			File.Delete(destinationPath);

		using var source = OpenConnection();
		using var dest = new SqliteConnection($"Data Source={destinationPath}");
		dest.Open();
		source.BackupDatabase(dest);
		dest.Close();
		source.Close();
		SqliteConnection.ClearAllPools();
	}

	private static Dictionary<string, object> BuildRowValues(
		SidecarSnapshot sidecar, string svPath, string jsonPath, JObject saveRoot)
	{
		var row = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["sv_file"] = svPath,
			["json_file"] = jsonPath,
			["steam_id"] = sidecar.steamId,
			["machine_id"] = sidecar.machineId,
			["machine_id_hash"] = sidecar.machineIdHash,
			["timestamp_utc"] = sidecar.timestampUtc,
			["run_started_utc"] = sidecar.runStartedUtc,
			["save_id"] = sidecar.saveId,
			["is_multiplayer_hosted"] = sidecar.isMultiplayerHosted ? 1 : 0,
			["mp_player_count"] = sidecar.mpPlayerCount,
			["mp_player_limit"] = sidecar.mpPlayerLimit,
			["is_save_ours"] = sidecar.isSaveOurs ? 1 : 0,
			["is_partial_run"] = sidecar.isPartialRun ? 1 : 0,
			["forced_save"] = sidecar.forcedSave ? 1 : 0,
			["save_and_exit"] = sidecar.saveAndExit ? 1 : 0,
			["terrain_overview"] = sidecar.terrainOverview ?? "",
			["terrain_detail"] = sidecar.terrainDetail ?? "",
			["trap_positions"] = sidecar.trapPositions ?? "",
			["path_positions"] = sidecar.pathPositions ?? "",
			["elder_path_positions"] = sidecar.elderPathPositions ?? "",
			["mp_player_path_positions"] = sidecar.mpPlayerPathPositions ?? "",
			["medical_events"] = sidecar.medicalEvents ?? "",
			["session_death_count"] = sidecar.sessionDeathCount,
			["total_death_count"] = sidecar.totalDeathCount,
			["traders_killed"] = sidecar.tradersKilled,
			["elder_thornbacks_killed"] = sidecar.elderThornbacksKilled,
			["dune_traders_discovered"] = sidecar.duneTradersDiscovered,
			["experiment_traders_discovered"] = sidecar.experimentTradersDiscovered,
			["milky_traders_discovered"] = sidecar.milkyTradersDiscovered,
			["jungle_to_deep_gravel_transitions"] = sidecar.jungleToDeepGravelTransitions,
			["layer_transition_count"] = sidecar.layerTransitionCount,
			["total_crafts"] = sidecar.totalCrafts,
			["run_time_elapsed_seconds"] = sidecar.runTimeElapsedSeconds,
			["first_craft_utc"] = sidecar.firstCraftUtc,
			["first_injury_utc"] = sidecar.firstInjuryUtc,
			["traps_spawned_current_layer"] = sidecar.trapsSpawnedCurrentLayer,
			["traps_spawned_ever"] = sidecar.trapsSpawnedEver,
			["drill_pods_used"] = sidecar.drillPodsUsed,
			["medical_items_used_on_self"] = sidecar.medicalItemsUsedOnSelf,
			["medical_items_used_on_teammates"] = sidecar.medicalItemsUsedOnTeammates,
			["current_biome_depth"] = sidecar.currentBiomeDepth,
			["layer_pos_x"] = sidecar.layerPosX,
			["layer_pos_y"] = sidecar.layerPosY,
			["layer_pos_x_normalized"] = sidecar.layerPosXNormalized,
			["layer_pos_y_normalized"] = sidecar.layerPosYNormalized,
			["layer_width"] = sidecar.layerWidth,
			["layer_height"] = sidecar.layerHeight,
			["biome_override"] = sidecar.biomeOverride,
			["debug_world"] = sidecar.debugWorld ? 1 : 0,
			["player_total_depth_meters"] = sidecar.playerTotalDepthMeters,
			["run_tracking_enabled"] = sidecar.runTrackingEnabled ? 1 : 0,
			["world_seed"] = sidecar.worldSeed,
			["world_seed_input"] = sidecar.worldSeedInput,
			["world_seed_is_set"] = sidecar.worldSeedIsSet ? 1 : 0,
			["difficulty_preset"] = sidecar.difficultyPreset,
			["difficulty_preset_index"] = sidecar.difficultyPresetIndex,
			["difficulty_is_custom"] = sidecar.difficultyIsCustom ? 1 : 0,
			["snapshot_seq"] = sidecar.snapshotSeq,
			["console_commands_used_json"] = JsonConvert.SerializeObject(sidecar.consoleCommandsUsed ?? new List<string[]>()),
			["run_settings_json"] = JsonConvert.SerializeObject(sidecar.runSettings ?? new Dictionary<string, object>())
		};

		row["sv_leaderboard_save_id"] = LeaderboardSaveId.TryReadFromSaveRoot(saveRoot) ?? sidecar.saveId;
		row["sv_biome"] = TokenDouble(saveRoot["biome"]);
		row["sv_total_traveled"] = TokenDouble(saveRoot["totalTraveled"]);
		row["sv_run_time"] = TokenDouble(saveRoot["runTime"]);
		row["sv_c_height"] = TokenDouble(saveRoot["cHeight"]);
		row["sv_c_age"] = TokenDouble(saveRoot["cAge"]);
		row["sv_c_id"] = TokenDouble(saveRoot["cId"]);
		row["sv_c_ver"] = TokenDouble(saveRoot["cVer"]);
		row["sv_trap_rarity"] = TokenDouble(saveRoot["trapRarity"]);
		row["sv_loot_rarity"] = TokenDouble(saveRoot["lootRarity"]);
		row["sv_calories_consumed"] = TokenDouble(saveRoot["caloriesConsumed"]);
		row["sv_last_happiness_json"] = TokenJson(saveRoot["lastHappiness"]);
		row["sv_saved_recipe_data_json"] = TokenJson(saveRoot["savedRecipeData"]);
		row["sv_run_settings_json"] = TokenJson(saveRoot["runSettings"]);
		row["sv_body_json"] = TokenJson(saveRoot["body"]);
		row["sv_limbs_json"] = LimbsJsonWithDismembered(saveRoot["limbs"]);
		row["sv_items_json"] = TokenJson(saveRoot["items"]);
		row["sv_item_components_json"] = TokenJson(saveRoot["itemComponents"]);
		row["sv_body_components_json"] = TokenJson(saveRoot["bodyComponents"]);
		row["sv_limb_components_json"] = TokenJson(saveRoot["limbComponents"]);

		return row;
	}

	private static double? TokenDouble(JToken token)
	{
		if (token == null || token.Type == JTokenType.Null)
			return null;
		if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
			return token.Value<double>();
		if (double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			return v;
		return null;
	}

	private static string TokenJson(JToken token)
	{
		if (token == null || token.Type == JTokenType.Null)
			return null;
		return token.ToString(Formatting.None);
	}

	/// <summary>
	/// save.sv doesnt serialize Limb.dismembered so we stamp it from the live body.
	/// amputated bits (and kids, like the foot after a thigh) count as 0 for trail color.
	/// </summary>
	private static string LimbsJsonWithDismembered(JToken token)
	{
		string json = TokenJson(token);
		try
		{
			Body body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
			if (body?.limbs == null || body.limbs.Length == 0)
				return json;

			JArray arr = string.IsNullOrEmpty(json) ? new JArray() : JArray.Parse(json);
			for (int i = 0; i < body.limbs.Length; i++)
			{
				while (arr.Count <= i)
					arr.Add(new JObject());
				if (arr[i] is not JObject obj)
				{
					obj = new JObject();
					arr[i] = obj;
				}
				Limb limb = body.limbs[i];
				obj["dismembered"] = limb != null && limb.dismembered;
			}
			return arr.ToString(Formatting.None);
		}
		catch
		{
			return json;
		}
	}

	private static SqliteConnection OpenConnection()
	{
		var conn = new SqliteConnection($"Data Source={_dbPath}");
		conn.Open();
		return conn;
	}

	private static void EnsureRegistryTable(SqliteConnection conn)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			"CREATE TABLE IF NOT EXISTS lb_sessions (" +
			"table_name TEXT PRIMARY KEY, steam_id TEXT NOT NULL, save_id TEXT NOT NULL, " +
			"run_started_utc TEXT, created_utc TEXT NOT NULL)";
		cmd.ExecuteNonQuery();
	}

	private static void RegisterSession(SqliteConnection conn, string tableName, RunSession session)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			"INSERT OR IGNORE INTO lb_sessions (table_name, steam_id, save_id, run_started_utc, created_utc) " +
			"VALUES ($table, $steam, $save, $started, $created)";
		cmd.Parameters.AddWithValue("$table", tableName);
		cmd.Parameters.AddWithValue("$steam", PlayerIdentity.GetSteamId());
		cmd.Parameters.AddWithValue("$save", session.SaveId);
		cmd.Parameters.AddWithValue("$started", session.RunStartedUtc.ToString("o", CultureInfo.InvariantCulture));
		cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
		cmd.ExecuteNonQuery();
	}

	private static void EnsureSidecarColumns(SqliteConnection conn, string tableName)
	{
		if (!TableExists(conn, tableName))
			return;

		var existing = GetTableColumns(conn, tableName);
		foreach (string column in SidecarScalarColumns)
		{
			if (existing.Contains(column))
				continue;

			using var cmd = conn.CreateCommand();
			cmd.CommandText = $"ALTER TABLE {QuoteIdent(tableName)} ADD COLUMN {QuoteIdent(column)} TEXT";
			cmd.ExecuteNonQuery();
			LbLog.Step("Sqlite:Alter", $"Added column {column} to {tableName}");
		}
	}

	private static HashSet<string> GetTableColumns(SqliteConnection conn, string tableName)
	{
		var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"PRAGMA table_info({QuoteIdent(tableName)})";
		using var reader = cmd.ExecuteReader();
		while (reader.Read())
			columns.Add(reader.GetString(1));
		return columns;
	}

	private static bool TableExists(SqliteConnection conn, string tableName)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
		cmd.Parameters.AddWithValue("$name", tableName);
		return cmd.ExecuteScalar() != null;
	}

	internal static string GetSessionTableName(RunSession session)
	{
		return GetSessionTableNameForSaveId(session.SaveId);
	}

	internal static string GetSessionTableNameForSaveId(string saveId)
	{
		string steam = SanitizeIdent(PlayerIdentity.SanitizePathSegment(PlayerIdentity.GetSteamId()));
		string save = SanitizeIdent(PlayerIdentity.SanitizePathSegment(saveId));
		return $"lb_{steam}_{save}";
	}

	internal sealed class SnapshotHint
	{
		internal int BiomeDepth;
		internal double DepthMeters;
		internal string PersonaKey;
	}

	internal static int GetMaxSnapshotSeq(string saveId)
	{
		if (!_initialized || string.IsNullOrEmpty(saveId))
			return 0;

		string tableName = GetSessionTableNameForSaveId(saveId);
		using var conn = OpenConnection();
		if (!TableExists(conn, tableName))
			return 0;

		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			$"SELECT MAX(CAST({QuoteIdent("snapshot_seq")} AS INTEGER)) FROM {QuoteIdent(tableName)}";
		object scalar = cmd.ExecuteScalar();
		if (scalar == null || scalar == DBNull.Value)
			return 0;

		return int.TryParse(scalar.ToString(), out int max) ? max : 0;
	}

	internal static bool HasSnapshotSeq(string saveId, int snapshotSeq)
	{
		return TryGetSnapshotRowExists(saveId, snapshotSeq);
	}

	internal static string TryGetSnapshotTimestamp(string saveId, int snapshotSeq)
	{
		if (!_initialized || string.IsNullOrEmpty(saveId) || snapshotSeq < 1)
			return null;

		string tableName = GetSessionTableNameForSaveId(saveId);
		using var conn = OpenConnection();
		if (!TableExists(conn, tableName))
			return null;

		var columns = GetTableColumns(conn, tableName);
		if (!columns.Contains("snapshot_seq") || !columns.Contains("timestamp_utc"))
			return null;

		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			$"SELECT {QuoteIdent("timestamp_utc")} FROM {QuoteIdent(tableName)} " +
			$"WHERE CAST({QuoteIdent("snapshot_seq")} AS INTEGER) = $seq LIMIT 1";
		cmd.Parameters.AddWithValue("$seq", snapshotSeq);
		object scalar = cmd.ExecuteScalar();
		if (scalar == null || scalar == DBNull.Value)
			return null;

		string text = scalar.ToString();
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}

	private static bool TryGetSnapshotRowExists(string saveId, int snapshotSeq)
	{
		if (!_initialized || string.IsNullOrEmpty(saveId) || snapshotSeq < 1)
			return false;

		string tableName = GetSessionTableNameForSaveId(saveId);
		using var conn = OpenConnection();
		if (!TableExists(conn, tableName))
			return false;

		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			$"SELECT 1 FROM {QuoteIdent(tableName)} " +
			$"WHERE CAST({QuoteIdent("snapshot_seq")} AS INTEGER) = $seq LIMIT 1";
		cmd.Parameters.AddWithValue("$seq", snapshotSeq);
		object scalar = cmd.ExecuteScalar();
		return scalar != null && scalar != DBNull.Value;
	}

	internal static List<string> ListKnownSaveIds()
	{
		var ids = new List<string>();
		if (!_initialized)
			return ids;

		try
		{
			using var conn = OpenConnection();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT DISTINCT save_id FROM lb_sessions WHERE save_id IS NOT NULL AND save_id != ''";
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				string id = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
				if (LeaderboardSaveId.IsValid(id) && !ids.Contains(id))
					ids.Add(id);
			}
		}
		catch (Exception ex)
		{
			LbLog.Warn("Sqlite:Sessions", ex.Message);
		}
		return ids;
	}

	internal static SnapshotHint TryGetLastSnapshotHint(string saveId)
	{
		if (!_initialized || string.IsNullOrEmpty(saveId))
			return null;

		string tableName = GetSessionTableNameForSaveId(saveId);
		using var conn = OpenConnection();
		if (!TableExists(conn, tableName))
			return null;

		var columns = GetTableColumns(conn, tableName);
		if (!columns.Contains("snapshot_seq"))
			return null;

		string biomeCol = columns.Contains("current_biome_depth") ? "current_biome_depth" : "0";
		string depthCol = columns.Contains("player_total_depth_meters") ? "player_total_depth_meters" : "0";
		string idCol = columns.Contains("sv_c_id") ? "sv_c_id" : "''";
		string ageCol = columns.Contains("sv_c_age") ? "sv_c_age" : "''";
		string heightCol = columns.Contains("sv_c_height") ? "sv_c_height" : "''";
		string verCol = columns.Contains("sv_c_ver") ? "sv_c_ver" : "''";

		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			$"SELECT {QuoteIdentIfColumn(biomeCol)}, {QuoteIdentIfColumn(depthCol)}, " +
			$"{QuoteIdentIfColumn(idCol)}, {QuoteIdentIfColumn(ageCol)}, " +
			$"{QuoteIdentIfColumn(heightCol)}, {QuoteIdentIfColumn(verCol)} " +
			$"FROM {QuoteIdent(tableName)} " +
			$"ORDER BY CAST({QuoteIdent("snapshot_seq")} AS INTEGER) DESC LIMIT 1";

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;

		return new SnapshotHint
		{
			BiomeDepth = ParseInt(reader.IsDBNull(0) ? null : reader.GetValue(0), 0),
			DepthMeters = ParseDouble(reader.IsDBNull(1) ? null : reader.GetValue(1), 0),
			PersonaKey = string.Join("|",
				ReadText(reader, 2),
				ReadText(reader, 3),
				ReadText(reader, 4),
				ReadText(reader, 5))
		};
	}

	private static string QuoteIdentIfColumn(string identOrLiteral)
	{
		if (identOrLiteral == "0" || identOrLiteral == "''")
			return identOrLiteral;
		return QuoteIdent(identOrLiteral);
	}

	private static string ReadText(SqliteDataReader reader, int index)
	{
		if (reader.IsDBNull(index))
			return "";
		return reader.GetValue(index)?.ToString() ?? "";
	}

	private static int ParseInt(object value, int fallback)
	{
		if (value == null)
			return fallback;
		return int.TryParse(value.ToString(), out int parsed) ? parsed : fallback;
	}

	private static double ParseDouble(object value, double fallback)
	{
		if (value == null)
			return fallback;
		return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
			? parsed
			: fallback;
	}

	internal static DateTime? TryGetRegisteredRunStartedUtc(string saveId)
	{
		if (!_initialized || string.IsNullOrEmpty(saveId))
			return null;

		string tableName = GetSessionTableNameForSaveId(saveId);
		using var conn = OpenConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText =
			"SELECT run_started_utc FROM lb_sessions WHERE table_name = $table LIMIT 1";
		cmd.Parameters.AddWithValue("$table", tableName);
		object scalar = cmd.ExecuteScalar();
		if (scalar == null || scalar == DBNull.Value)
			return null;

		return DateTime.TryParse(scalar.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime utc)
			? utc
			: (DateTime?)null;
	}

	private static string SanitizeIdent(string value)
	{
		if (string.IsNullOrEmpty(value))
			return "unknown";
		var sb = new StringBuilder(value.Length);
		foreach (char c in value)
			sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		return sb.ToString();
	}

	private static string QuoteIdent(string ident)
	{
		return "\"" + ident.Replace("\"", "\"\"") + "\"";
	}
}
