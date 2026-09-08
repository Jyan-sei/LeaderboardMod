using System;
using System.Collections.Generic;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal static class MpPlayerTrailCapture
{
	internal const float SampleTiles = 5f;
	internal const int CoordMax = 1023;
	internal const int MaxPoints = 4000;
	private static readonly byte[] Magic = { (byte)'M', (byte)'P', (byte)'v', (byte)'1' };
	private static readonly List<KrokMpOptional.RemotePlayerPose> PoseScratch = new List<KrokMpOptional.RemotePlayerPose>(8);
	private static int _loggedCount = -1;
	private static int _lastSampleFrame = -60;
	private const int SampleEveryFrames = 60;

	internal static void Reset(RunSession session)
	{
		if (session == null)
			return;
		session.MpPlayerTrails.Clear();
		session.MpPlayerTrailBiomeDepth = int.MinValue;
	}

	internal static bool HasStartedTrail(RunSession session, ulong trailId)
	{
		return session?.MpPlayerTrails != null
			&& session.MpPlayerTrails.TryGetValue(trailId, out var state)
			&& state != null
			&& !double.IsNaN(state.LastBlockX);
	}

	internal static void Tick()
	{
		if (!RunSessionManager.ShouldCapture())
			return;
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return;
		if (Time.frameCount - _lastSampleFrame < SampleEveryFrames)
			return;
		_lastSampleFrame = Time.frameCount;
		TrySampleAll(session, force: false);
	}

	internal static string Flush(RunSession session)
	{
		if (session == null)
			return "";
		TrySampleAll(session, force: true);
		string packed = Pack(session.MpPlayerTrails);
		foreach (var state in session.MpPlayerTrails.Values)
			state.Pending.Clear();
		return packed;
	}

	private static void TrySampleAll(RunSession session, bool force)
	{
		if (!KrokMpOptional.IsNetworkRunning)
			return;

		var world = WorldGeneration.world;
		if (world == null || !world.worldExists || world.generatingWorld)
			return;

		if (session.MpPlayerTrailBiomeDepth != world.biomeDepth)
		{
			foreach (var state in session.MpPlayerTrails.Values)
			{
				state.Pending.Clear();
				state.LayerPath.Clear();
				state.LastBlockX = double.NaN;
				state.LastBlockY = double.NaN;
			}
			session.MpPlayerTrailBiomeDepth = world.biomeDepth;
		}

		KrokMpOptional.CollectRemotePlayerPoses(PoseScratch);
		if (PoseScratch.Count != _loggedCount)
		{
			_loggedCount = PoseScratch.Count;
			LbLog.Step("MpTrail", $"tracking {_loggedCount} remote player(s)");
		}

		for (int i = 0; i < PoseScratch.Count; i++)
		{
			var pose = PoseScratch[i];
			if (!HasStartedTrail(session, pose.TrailId))
			{
				if (!pose.LoadedIn || pose.IsOriginStub)
					continue;
			}
			if (!LayerPositionCapture.TryCaptureWorld(pose.Position, out var pos))
				continue;
			Sample(session, pose.TrailId, PathSample.From(pos), pose.Alive, force);
		}
	}

	private static void Sample(RunSession session, ulong trailId, PathSample sample, bool alive, bool force)
	{
		if (!session.MpPlayerTrails.TryGetValue(trailId, out var state))
		{
			state = new ElderTrailState();
			session.MpPlayerTrails[trailId] = state;
		}

		if (!alive)
		{
			if (state.LastAlive != false)
				Append(state, new PathSample(sample.BlockX, sample.BlockY, sample.NormalizedX, sample.NormalizedY, PathEvent.Death));
			state.LastAlive = false;
			return;
		}

		if (state.LastAlive == false)
		{
			Append(state, new PathSample(sample.BlockX, sample.BlockY, sample.NormalizedX, sample.NormalizedY, PathEvent.Revive));
			state.LastAlive = true;
			return;
		}

		state.LastAlive = true;
		if (double.IsNaN(state.LastBlockX))
		{
			Append(state, sample);
			return;
		}

		double dx = sample.BlockX - state.LastBlockX;
		double dy = sample.BlockY - state.LastBlockY;
		double distSq = dx * dx + dy * dy;
		if (IsJoinTeleport(state, distSq))
		{
			state.Pending.Clear();
			state.LayerPath.Clear();
			state.LastBlockX = double.NaN;
			state.LastBlockY = double.NaN;
			Append(state, sample);
			return;
		}

		double min = force ? 0.5d : SampleTiles;
		if (distSq >= min * min)
			Append(state, sample);
	}

	private static bool IsJoinTeleport(ElderTrailState state, double distSq)
	{
		const double joinTeleportTiles = 40d;
		if (distSq < joinTeleportTiles * joinTeleportTiles)
			return false;
		return (state.LayerPath?.Count ?? 0) <= 1;
	}

	private static void Append(ElderTrailState state, PathSample sample)
	{
		state.Pending.Add(sample);
		state.LayerPath.Add(sample);
		state.LastBlockX = sample.BlockX;
		state.LastBlockY = sample.BlockY;
	}

	private static string Pack(Dictionary<ulong, ElderTrailState> trails)
	{
		if (trails == null || trails.Count == 0)
			return "";

		int remaining = MaxPoints;
		var chunks = new List<byte[]>();
		int playerCount = 0;
		foreach (var kv in trails)
		{
			var pending = kv.Value.Pending;
			if (pending.Count == 0 || remaining <= 0)
				continue;
			int count = Math.Min(pending.Count, remaining);
			var bytes = new byte[10 + count * 4];
			unchecked
			{
				ulong id = kv.Key;
				bytes[0] = (byte)(id & 0xff);
				bytes[1] = (byte)((id >> 8) & 0xff);
				bytes[2] = (byte)((id >> 16) & 0xff);
				bytes[3] = (byte)((id >> 24) & 0xff);
				bytes[4] = (byte)((id >> 32) & 0xff);
				bytes[5] = (byte)((id >> 40) & 0xff);
				bytes[6] = (byte)((id >> 48) & 0xff);
				bytes[7] = (byte)((id >> 56) & 0xff);
			}
			bytes[8] = (byte)(count & 0xff);
			bytes[9] = (byte)((count >> 8) & 0xff);
			for (int i = 0; i < count; i++)
				PathPack.WritePoint(bytes, 10 + i * 4, pending[i]);
			chunks.Add(bytes);
			remaining -= count;
			playerCount += 1;
		}

		if (playerCount == 0)
			return "";

		int total = Magic.Length + 2;
		for (int i = 0; i < chunks.Count; i++)
			total += chunks[i].Length;
		var packed = new byte[total];
		Buffer.BlockCopy(Magic, 0, packed, 0, Magic.Length);
		packed[4] = (byte)(playerCount & 0xff);
		packed[5] = (byte)((playerCount >> 8) & 0xff);
		int offset = 6;
		for (int i = 0; i < chunks.Count; i++)
		{
			Buffer.BlockCopy(chunks[i], 0, packed, offset, chunks[i].Length);
			offset += chunks[i].Length;
		}
		return Convert.ToBase64String(packed);
	}
}
