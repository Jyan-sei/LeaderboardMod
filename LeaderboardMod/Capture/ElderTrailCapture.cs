using System;
using System.Collections.Generic;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal sealed class ElderTrailState
{
	internal double LastBlockX = double.NaN;
	internal double LastBlockY = double.NaN;
	internal bool? LastAlive;
	internal readonly List<PathSample> Pending = new List<PathSample>();
	internal readonly List<PathSample> LayerPath = new List<PathSample>();
}

internal static class ElderTrailCapture
{
	internal const float SampleTiles = 10f;
	internal const int CoordMax = 1023;
	internal const int MaxPoints = 4000;
	private const double RecycleMatchTiles = 10d;
	private static readonly byte[] Magic = { (byte)'E', (byte)'T', (byte)'v', (byte)'1' };

	private static readonly List<ElderThornbackBehaviour> Registered = new List<ElderThornbackBehaviour>();
	private static int _loggedCount = -1;

	internal static void Register(ElderThornbackBehaviour elder)
	{
		if (elder == null)
			return;
		Registered.RemoveAll(item => item == null);
		if (!Registered.Contains(elder))
			Registered.Add(elder);
	}

	internal static void Unregister(ElderThornbackBehaviour elder)
	{
		if (elder == null)
			return;
		Registered.Remove(elder);
	}

	internal static void Reset(RunSession session)
	{
		if (session == null)
			return;
		session.ElderTrails.Clear();
		session.ElderTrailBiomeDepth = int.MinValue;
	}

	internal static void Tick()
	{
		if (!RunSessionManager.ShouldCapture())
			return;
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return;
		TrySampleAll(session, force: false);
	}

	internal static string Flush(RunSession session)
	{
		if (session == null)
			return "";
		TrySampleAll(session, force: true);
		string packed = Pack(session.ElderTrails);
		foreach (var state in session.ElderTrails.Values)
			state.Pending.Clear();
		return packed;
	}

	internal static void DiscoverWorldElders()
	{
		var found = UnityEngine.Object.FindObjectsOfType<ElderThornbackBehaviour>(true);
		for (int i = 0; i < found.Length; i++)
			Register(found[i]);

		if (Registered.Count != _loggedCount)
		{
			_loggedCount = Registered.Count;
			LbLog.Step("ElderTrail", $"tracking {_loggedCount} elder(s) after world scan");
		}
	}

	private static void TrySampleAll(RunSession session, bool force)
	{
		var world = WorldGeneration.world;
		if (world == null || !world.worldExists || world.generatingWorld)
			return;

		if (session.ElderTrailBiomeDepth != world.biomeDepth)
		{
			session.ElderTrails.Clear();
			session.ElderTrailBiomeDepth = world.biomeDepth;
		}

		Registered.RemoveAll(item => item == null);

		// no player-distance gate. krokmp / chunk opt just freeze far elders;
		// transform sits at the last simulated point and we keep that trail.
		for (int i = 0; i < Registered.Count; i++)
		{
			var elder = Registered[i];
			if (elder == null)
				continue;
			var build = elder.GetComponent<BuildingEntity>();
			if (build != null && build.health <= 0f)
				continue;
			if (!LayerPositionCapture.TryCaptureWorld(elder.transform.position, out var pos))
				continue;
			var sample = PathSample.From(pos);
			Sample(session, ResolveTrailId(session, elder, sample), sample, force);
		}
	}

	private static int ResolveTrailId(RunSession session, ElderThornbackBehaviour elder, PathSample sample)
	{
		int instanceId = elder.GetInstanceID();
		if (session.ElderTrails.ContainsKey(instanceId))
			return instanceId;

		int bestId = instanceId;
		double bestDist = RecycleMatchTiles * RecycleMatchTiles;
		foreach (var kv in session.ElderTrails)
		{
			if (IsLiveInstance(kv.Key))
				continue;
			if (double.IsNaN(kv.Value.LastBlockX))
				continue;
			double dx = sample.BlockX - kv.Value.LastBlockX;
			double dy = sample.BlockY - kv.Value.LastBlockY;
			double dist = dx * dx + dy * dy;
			if (dist <= bestDist)
			{
				bestDist = dist;
				bestId = kv.Key;
			}
		}
		return bestId;
	}

	private static bool IsLiveInstance(int instanceId)
	{
		for (int i = 0; i < Registered.Count; i++)
		{
			var elder = Registered[i];
			if (elder != null && elder.GetInstanceID() == instanceId)
				return true;
		}
		return false;
	}

	private static void Sample(RunSession session, int instanceId, PathSample sample, bool force)
	{
		if (!session.ElderTrails.TryGetValue(instanceId, out var state))
		{
			state = new ElderTrailState();
			session.ElderTrails[instanceId] = state;
		}

		if (double.IsNaN(state.LastBlockX))
		{
			Append(state, sample);
			return;
		}

		double dx = sample.BlockX - state.LastBlockX;
		double dy = sample.BlockY - state.LastBlockY;
		double min = force ? 0.5d : SampleTiles;
		if (dx * dx + dy * dy >= min * min)
			Append(state, sample);
	}

	private static void Append(ElderTrailState state, PathSample sample)
	{
		state.Pending.Add(sample);
		state.LastBlockX = sample.BlockX;
		state.LastBlockY = sample.BlockY;
	}

	private static string Pack(Dictionary<int, ElderTrailState> trails)
	{
		if (trails == null || trails.Count == 0)
			return "";

		int remaining = MaxPoints;
		var chunks = new List<byte[]>();
		int elderCount = 0;
		foreach (var kv in trails)
		{
			var pending = kv.Value.Pending;
			if (pending.Count == 0 || remaining <= 0)
				continue;
			int count = Math.Min(pending.Count, remaining);
			var bytes = new byte[6 + count * 4];
			unchecked
			{
				bytes[0] = (byte)(kv.Key & 0xff);
				bytes[1] = (byte)((kv.Key >> 8) & 0xff);
				bytes[2] = (byte)((kv.Key >> 16) & 0xff);
				bytes[3] = (byte)((kv.Key >> 24) & 0xff);
			}
			bytes[4] = (byte)(count & 0xff);
			bytes[5] = (byte)((count >> 8) & 0xff);
			for (int i = 0; i < count; i++)
				PathPack.WritePoint(bytes, 6 + i * 4, pending[i]);
			chunks.Add(bytes);
			remaining -= count;
			elderCount += 1;
		}

		if (elderCount == 0)
			return "";

		int total = Magic.Length + 2;
		for (int i = 0; i < chunks.Count; i++)
			total += chunks[i].Length;
		var packed = new byte[total];
		Buffer.BlockCopy(Magic, 0, packed, 0, Magic.Length);
		packed[4] = (byte)(elderCount & 0xff);
		packed[5] = (byte)((elderCount >> 8) & 0xff);
		int offset = 6;
		for (int i = 0; i < chunks.Count; i++)
		{
			Buffer.BlockCopy(chunks[i], 0, packed, offset, chunks[i].Length);
			offset += chunks[i].Length;
		}
		return Convert.ToBase64String(packed);
	}
}
