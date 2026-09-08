using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using LeaderboardMod.Logging;
using UnityEngine;

namespace LeaderboardMod.Session;

/// <summary>
/// krokmp stuff via reflection, no compile time dependancy
/// </summary>
internal static class KrokMpOptional
{
	private static bool _resolved;
	private static bool _present;
	private static Assembly _assembly;
	private static Type _netType;
	private static Type _netPlayerType;
	private static Type _utilType;
	private static Type _multiplayerType;
	private static Type _pluginType;
	private static Type _savesystemPatchType;
	private static FieldInfo _lastWoundItemUserField;
	private static FieldInfo _allLivingPlayersField;
	private static FieldInfo _clientIdToPlayerDictField;
	private static FieldInfo _localPlayerField;
	private static FieldInfo _steamIdField;
	private static FieldInfo _bodyField;
	private static FieldInfo _serverPlrStateField;
	private static FieldInfo _isLoadedInField;
	private static FieldInfo _didGiveSpawnField;
	private static PropertyInfo _isLocalProp;
	private static PropertyInfo _clientIdProp;
	private static bool _playerFieldsResolved;
	private static PropertyInfo _runningProp;
	private static FieldInfo _runningField;
	private static PropertyInfo _isServerProp;
	private static FieldInfo _isServerField;
	private static PropertyInfo _networkSystemProp;
	private static FieldInfo _networkSystemField;
	private static FieldInfo _successfullyInitField;
	private static MethodInfo _isBodyLocalMethod;
	private static bool _staticMembersResolved;
	private static int _flagFrame = -1;
	private static int _mainThreadId;
	private static bool _cachedRunning;
	private static bool _cachedServer;
	private static bool _cachedNetworkSystem;

	internal static void RememberMainThread()
	{
		_mainThreadId = Thread.CurrentThread.ManagedThreadId;
	}

	private static bool IsMainThread()
	{
		return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
	}

	internal static bool IsPresent
	{
		get
		{
			Resolve();
			return _present;
		}
	}

	internal static void Resolve()
	{
		if (_resolved)
			return;
		_resolved = true;

		foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.GetName().Name == "KrokoshaCasualtiesMP")
			{
				_assembly = asm;
				break;
			}
		}

		if (_assembly == null)
		{
			LbLog.Step("KrokMP", "Not installed - SP-only mode");
			return;
		}

		_netType = _assembly.GetType("KrokoshaCasualtiesMP.Net");
		_netPlayerType = _assembly.GetType("KrokoshaCasualtiesMP.NetPlayer");
		_utilType = _assembly.GetType("KrokoshaCasualtiesUtils.Util")
			?? _assembly.GetType("KrokoshaCasualtiesMP.Util");
		_multiplayerType = _assembly.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
		_pluginType = _assembly.GetType("KrokoshaCasualtiesMP.Plugin");
		_savesystemPatchType = _assembly.GetType("KrokoshaCasualtiesMP.SavesystemPatch");

		Type woundPatch = _assembly.GetType("KrokoshaCasualtiesMP.PlayerCamera_ApplyWoundItem_MultiplayerPatch");
		_lastWoundItemUserField = woundPatch?.GetField("last_wounditem_user", BindingFlags.Public | BindingFlags.Static);

		_present = _netType != null;
		if (_present)
			LbLog.Step("KrokMP", "Detected - optional MP hooks enabled");
	}

	internal static bool SuccessfullyInitialized
	{
		get
		{
			if (!IsPresent || _pluginType == null)
				return false;
			EnsureStaticMembers();
			try
			{
				return _successfullyInitField != null && _successfullyInitField.GetValue(null) is bool b && b;
			}
			catch
			{
				return false;
			}
		}
	}

	internal static bool IsNetworkRunning
	{
		get
		{
			RefreshCachedFlags();
			return _cachedRunning;
		}
	}

	internal static bool IsServer
	{
		get
		{
			RefreshCachedFlags();
			return _cachedServer;
		}
	}

	internal static bool NetworkSystemRunning
	{
		get
		{
			RefreshCachedFlags();
			return _cachedNetworkSystem;
		}
	}

	internal static bool IsBodyLocal(Body body)
	{
		if (body == null)
			return false;
		if (!IsNetworkRunning)
			return PlayerCamera.main != null && body == PlayerCamera.main.body;

		if (_utilType == null)
			return PlayerCamera.main != null && body == PlayerCamera.main.body;

		EnsureStaticMembers();
		try
		{
			if (_isBodyLocalMethod != null)
				return _isBodyLocalMethod.Invoke(null, new object[] { body }) is bool b && b;
		}
		catch
		{
			// fall through
		}

		return PlayerCamera.main != null && body == PlayerCamera.main.body;
	}

	internal static int GetMpPlayerCount()
	{
		if (!IsNetworkRunning || _netPlayerType == null)
			return 1;
		try
		{
			FieldInfo dictField = _netPlayerType.GetField("ClientIdToPlayerDict", BindingFlags.Public | BindingFlags.Static);
			if (dictField?.GetValue(null) is System.Collections.IDictionary dict)
			{
				int count = dict.Count;
				return count > 0 ? count : 1;
			}
		}
		catch
		{
			// ignore
		}
		return 1;
	}

	internal static int GetMpPlayerLimit()
	{
		if (!IsNetworkRunning || _multiplayerType == null)
			return 1;
		try
		{
			FieldInfo rulesField = _multiplayerType.GetField("rules", BindingFlags.Public | BindingFlags.Static);
			object rules = rulesField?.GetValue(null);
			if (rules == null)
				return 1;
			FieldInfo limitField = rules.GetType().GetField("PLAYER_COUNT_LIMIT", BindingFlags.Public | BindingFlags.Instance);
			if (limitField?.GetValue(rules) is int limit && limit > 0)
				return limit;
		}
		catch
		{
			// ignore
		}
		return 1;
	}

	internal static bool IsSaveOurs()
	{
		if (!IsNetworkRunning || _netPlayerType == null || _savesystemPatchType == null)
			return true;
		try
		{
			FieldInfo localPlayerField = _netPlayerType.GetField("LOCAL_PLAYER", BindingFlags.Public | BindingFlags.Static);
			object localPlayer = localPlayerField?.GetValue(null);
			if (localPlayer == null)
				return false;

			MethodInfo getPid = localPlayer.GetType().GetMethod("GetPersistentId");
			string pid = getPid?.Invoke(localPlayer, null) as string;
			if (string.IsNullOrWhiteSpace(pid))
				return false;

			FieldInfo folderField = _savesystemPatchType.GetField("mpsavefolder", BindingFlags.Public | BindingFlags.Static);
			string folder = folderField?.GetValue(null) as string;
			if (string.IsNullOrWhiteSpace(folder))
				return false;

			return File.Exists(Path.Combine(folder, pid, "save.sv"));
		}
		catch
		{
			return false;
		}
	}

	internal static Body GetLastWoundItemUser()
	{
		if (_lastWoundItemUserField == null)
			return null;
		try
		{
			return _lastWoundItemUserField.GetValue(null) as Body;
		}
		catch
		{
			return null;
		}
	}

	internal readonly struct RemotePlayerPose
	{
		private const float OriginStubTiles = 8f;

		internal readonly ulong TrailId;
		internal readonly Vector2 Position;
		internal readonly bool Alive;
		internal readonly bool LoadedIn;

		internal RemotePlayerPose(ulong trailId, Vector2 position, bool alive, bool loadedIn)
		{
			TrailId = trailId;
			Position = position;
			Alive = alive;
			LoadedIn = loadedIn;
		}

		/// <summary>joining clients sit at 0,0 until the server actually places them</summary>
		internal bool IsOriginStub => Position.sqrMagnitude <= OriginStubTiles * OriginStubTiles;
	}

	/// <summary>other players with a body, dead ones too</summary>
	internal static void CollectRemotePlayerPoses(List<RemotePlayerPose> dest)
	{
		if (dest == null)
			return;
		dest.Clear();
		if (!IsNetworkRunning || _netPlayerType == null)
			return;

		EnsurePlayerFields();
		object localPlayer = null;
		try
		{
			localPlayer = _localPlayerField?.GetValue(null);
		}
		catch
		{
			localPlayer = null;
		}

		Body localBody = PlayerCamera.main != null ? PlayerCamera.main.body : null;
		var seen = new HashSet<ulong>();

		void consider(object player)
		{
			if (player == null || ReferenceEquals(player, localPlayer))
				return;
			try
			{
				if (_isLocalProp?.GetValue(player) is bool isLocal && isLocal)
					return;
				if (_bodyField?.GetValue(player) is not Body body || body == null || body == localBody)
					return;
				ulong trailId = ReadTrailId(player);
				if (trailId == 0 || !seen.Add(trailId))
					return;
				dest.Add(new RemotePlayerPose(trailId, body.transform.position, body.alive, ReadLoadedIn(player)));
			}
			catch
			{
				// ignore a single bad player object
			}
		}

		try
		{
			if (_clientIdToPlayerDictField?.GetValue(null) is IDictionary dict)
			{
				foreach (object player in dict.Values)
					consider(player);
			}
		}
		catch
		{
			// ignore
		}

		try
		{
			if (_allLivingPlayersField?.GetValue(null) is IEnumerable living)
			{
				foreach (object player in living)
					consider(player);
			}
		}
		catch
		{
			// ignore
		}
	}

	private static void EnsurePlayerFields()
	{
		if (_playerFieldsResolved || _netPlayerType == null)
			return;
		_playerFieldsResolved = true;
		const BindingFlags stat = BindingFlags.Public | BindingFlags.Static;
		const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
		_allLivingPlayersField = _netPlayerType.GetField("AllLivingPlayers", stat);
		_clientIdToPlayerDictField = _netPlayerType.GetField("ClientIdToPlayerDict", stat);
		_localPlayerField = _netPlayerType.GetField("LOCAL_PLAYER", stat);
		_steamIdField = _netPlayerType.GetField("steam_id", inst);
		_bodyField = _netPlayerType.GetField("body", inst);
		_serverPlrStateField = _netPlayerType.GetField("server_plrstate", inst);
		_isLocalProp = _netPlayerType.GetProperty("is_local", inst);
		_clientIdProp = _netPlayerType.GetProperty("clientId", inst);
	}

	private static bool ReadLoadedIn(object player)
	{
		try
		{
			object state = _serverPlrStateField?.GetValue(player);
			if (state == null)
				return true;
			if (_isLoadedInField == null && _didGiveSpawnField == null)
			{
				Type stateType = state.GetType();
				const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
				_isLoadedInField = stateType.GetField("is_loaded_in", inst);
				_didGiveSpawnField = stateType.GetField("did_give_spawn_location", inst);
			}

			bool loaded = _isLoadedInField == null || (_isLoadedInField.GetValue(state) is bool lb && lb);
			bool spawned = _didGiveSpawnField == null || (_didGiveSpawnField.GetValue(state) is bool sb && sb);
			return loaded && spawned;
		}
		catch
		{
			return true;
		}
	}

	private static ulong ReadTrailId(object player)
	{
		try
		{
			object steam = _steamIdField?.GetValue(player);
			if (steam is ulong steamU && steamU != 0)
				return steamU;
			if (steam is long steamL && steamL != 0)
				return unchecked((ulong)steamL);
		}
		catch
		{
			// fall through
		}

		try
		{
			object clientId = _clientIdProp?.GetValue(player);
			if (clientId != null)
			{
				try
				{
					return Convert.ToUInt64(clientId, CultureInfo.InvariantCulture);
				}
				catch
				{
					object nested = clientId.GetType().GetField("id")?.GetValue(clientId);
					if (nested is ushort u && u != 0)
						return u;
					if (nested is int i && i != 0)
						return unchecked((ulong)i);
				}
			}
		}
		catch
		{
			// ignore
		}

		return 0;
	}

	internal static bool TryGetNetPlayerBody(object clientId, out Body body)
	{
		body = null;
		if (!IsPresent || _netPlayerType == null || clientId == null)
			return false;
		try
		{
			MethodInfo method = _netPlayerType.GetMethod(
				"TryGetNetPlayerAndBodyFromClientId",
				BindingFlags.Public | BindingFlags.Static);
			if (method == null)
				return false;

			object[] args = { clientId, null, null };
			if (method.Invoke(null, args) is bool ok && ok && args[1] != null)
			{
				MethodInfo getBody = args[1].GetType().GetMethod("get_body") ?? args[1].GetType().GetProperty("body")?.GetGetMethod();
				body = getBody?.Invoke(args[1], null) as Body;
				if (body == null)
				{
					PropertyInfo bodyProp = args[1].GetType().GetProperty("body");
					body = bodyProp?.GetValue(args[1]) as Body;
				}
				return body != null;
			}
		}
		catch
		{
			// ignore
		}
		return false;
	}

	private static void EnsureStaticMembers()
	{
		if (_staticMembersResolved)
			return;
		_staticMembersResolved = true;
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
		ResolveBoolMember(_netType, "running", out _runningProp, out _runningField);
		ResolveBoolMember(_netType, "is_server", out _isServerProp, out _isServerField);
		ResolveBoolMember(_multiplayerType, "network_system_is_running", out _networkSystemProp, out _networkSystemField);
		_successfullyInitField = _pluginType?.GetField("SUCCESFULLY_INITIALIZED", flags);
		_isBodyLocalMethod = _utilType?.GetMethod(
			"IsBodyLocal", flags, null, new[] { typeof(Body) }, null);
	}

	private static void ResolveBoolMember(Type type, string name, out PropertyInfo prop, out FieldInfo field)
	{
		prop = null;
		field = null;
		if (type == null)
			return;
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
		try
		{
			PropertyInfo found = type.GetProperty(name, flags);
			if (found != null && found.GetIndexParameters().Length == 0)
				prop = found;
		}
		catch
		{
			// fall through
		}

		if (prop != null)
			return;
		try
		{
			field = type.GetField(name, flags);
		}
		catch
		{
			field = null;
		}
	}

	private static void RefreshCachedFlags()
	{
		if (!IsMainThread())
			return;

		int frame = Time.frameCount;
		if (frame == _flagFrame)
			return;
		_flagFrame = frame;
		Resolve();
		EnsureStaticMembers();
		_cachedRunning = ReadStaticBool(_runningProp, _runningField);
		_cachedServer = ReadStaticBool(_isServerProp, _isServerField);
		_cachedNetworkSystem = ReadStaticBool(_networkSystemProp, _networkSystemField);
	}

	private static bool ReadStaticBool(PropertyInfo prop, FieldInfo field)
	{
		try
		{
			if (prop != null)
				return prop.GetValue(null, null) is bool pb && pb;
			if (field != null)
				return field.GetValue(null) is bool b && b;
		}
		catch
		{
			return false;
		}
		return false;
	}
}
