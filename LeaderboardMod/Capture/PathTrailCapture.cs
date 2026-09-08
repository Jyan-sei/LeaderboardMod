using System;
using System.Collections.Generic;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal enum PathEvent : byte
{
	None = 0,
	Death = 1,
	Revive = 2,
}

internal readonly struct PathSample
{
	internal readonly double BlockX;
	internal readonly double BlockY;
	internal readonly double NormalizedX;
	internal readonly double NormalizedY;
	internal readonly PathEvent Event;

	internal PathSample(double blockX, double blockY, double normalizedX, double normalizedY, PathEvent evt = PathEvent.None)
	{
		BlockX = blockX;
		BlockY = blockY;
		NormalizedX = normalizedX;
		NormalizedY = normalizedY;
		Event = evt;
	}

	internal static PathSample From(in LayerPositionSnapshot pos, PathEvent evt = PathEvent.None)
	{
		return new PathSample(pos.BlockX, pos.BlockY, pos.NormalizedX, pos.NormalizedY, evt);
	}
}

internal static class PathPack
{
	internal const int CoordMax = 1023;
	internal const int EventShift = 10;

	internal static int ClampCoord(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
			return 0;
		return Math.Max(0, Math.Min(CoordMax, (int)Math.Round(value * CoordMax)));
	}

	internal static void WritePoint(byte[] bytes, int offset, PathSample sample)
	{
		int nx = ClampCoord(sample.NormalizedX) | ((int)sample.Event << EventShift);
		int ny = ClampCoord(sample.NormalizedY);
		bytes[offset] = (byte)(nx & 0xff);
		bytes[offset + 1] = (byte)((nx >> 8) & 0xff);
		bytes[offset + 2] = (byte)(ny & 0xff);
		bytes[offset + 3] = (byte)((ny >> 8) & 0xff);
	}
}

internal static class PathTrailCapture
{
	internal const float SampleTiles = 5f;
	internal const int CoordMax = 1023;

	internal static void Reset(RunSession session)
	{
		if (session == null)
			return;
		session.LayerPath.Clear();
		session.PendingPathFlush.Clear();
		session.LastPathBlockX = double.NaN;
		session.LastPathBlockY = double.NaN;
		session.LastPathAlive = null;
		session.PathTrailBiomeDepth = int.MinValue;
	}

	internal static void Tick()
	{
		if (!RunSessionManager.ShouldCapture())
			return;
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return;
		TrySample(session, force: false);
	}

	internal static string Flush(RunSession session)
	{
		if (session == null)
			return "";
		TrySample(session, force: true);
		if (session.PendingPathFlush.Count == 0)
		{
			var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
			if (body != null && body.alive && LayerPositionCapture.TryCapture(body, out var pos))
				Append(session, PathSample.From(pos));
		}

		string packed = Pack(session.PendingPathFlush);
		session.PendingPathFlush.Clear();
		return packed;
	}

	private static void TrySample(RunSession session, bool force)
	{
		var world = WorldGeneration.world;
		if (world == null || !world.worldExists || world.generatingWorld)
			return;
		var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
		if (body == null)
			return;
		bool alive = body.alive;
		bool havePos = LayerPositionCapture.TryCapture(body, out var pos);

		if (session.PathTrailBiomeDepth != world.biomeDepth)
		{
			session.LayerPath.Clear();
			session.PendingPathFlush.Clear();
			session.LastPathBlockX = double.NaN;
			session.LastPathBlockY = double.NaN;
			session.PathTrailBiomeDepth = world.biomeDepth;
		}

		if (!alive)
		{
			if (session.LastPathAlive != false)
				MarkEvent(session, PathEvent.Death, havePos ? pos : (LayerPositionSnapshot?)null);
			session.LastPathAlive = false;
			return;
		}

		if (session.LastPathAlive == false)
		{
			if (havePos)
				Append(session, PathSample.From(pos, PathEvent.Revive));
			session.LastPathAlive = true;
			return;
		}

		session.LastPathAlive = true;
		if (!havePos)
			return;

		if (double.IsNaN(session.LastPathBlockX))
		{
			Append(session, PathSample.From(pos));
			return;
		}

		double dx = pos.BlockX - session.LastPathBlockX;
		double dy = pos.BlockY - session.LastPathBlockY;
		double min = force ? 0.5d : SampleTiles;
		if (dx * dx + dy * dy >= min * min)
			Append(session, PathSample.From(pos));
	}

	internal static void MarkDeath()
	{
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return;
		if (LastEvent(session) == PathEvent.Death)
			return;
		var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
		LayerPositionSnapshot? pos = null;
		if (LayerPositionCapture.TryCapture(body, out var captured))
			pos = captured;
		MarkEvent(session, PathEvent.Death, pos);
		session.LastPathAlive = false;
	}

	private static PathEvent LastEvent(RunSession session)
	{
		if (session.PendingPathFlush.Count > 0)
			return session.PendingPathFlush[session.PendingPathFlush.Count - 1].Event;
		if (session.LayerPath.Count > 0)
			return session.LayerPath[session.LayerPath.Count - 1].Event;
		return PathEvent.None;
	}

	private static void MarkEvent(RunSession session, PathEvent evt, LayerPositionSnapshot? captured)
	{
		if (captured.HasValue)
		{
			Append(session, PathSample.From(captured.Value, evt));
			return;
		}
		if (session.LayerPath.Count == 0)
			return;
		var last = session.LayerPath[session.LayerPath.Count - 1];
		Append(session, new PathSample(last.BlockX, last.BlockY, last.NormalizedX, last.NormalizedY, evt));
	}

	private static void Append(RunSession session, PathSample sample)
	{
		session.LayerPath.Add(sample);
		session.PendingPathFlush.Add(sample);
		session.LastPathBlockX = sample.BlockX;
		session.LastPathBlockY = sample.BlockY;
	}

	private static string Pack(List<PathSample> samples)
	{
		if (samples == null || samples.Count == 0)
			return "";
		int count = Math.Min(samples.Count, 4000);
		var bytes = new byte[count * 4];
		for (int i = 0; i < count; i++)
			PathPack.WritePoint(bytes, i * 4, samples[i]);
		return Convert.ToBase64String(bytes);
	}
}
