using System;
using System.Collections.Generic;
using System.IO;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal static class TerrainOverviewCapture
{
	internal const int GridSize = 128;
	internal const int DetailTileSize = 5;
	internal const int KeepRadiusTiles = 24;
	internal const int FarChunkHi = 4;
	internal const int TrapCoordMax = 1023;

	internal static void MarkNewGeneration(RunSession session)
	{
		if (session == null)
			return;
		session.TerrainStoredForBiomeDepth = int.MinValue;
		session.TerrainKeepIncludedRemotes = false;
		session.TerrainStoredPathCount = 0;
		PathTrailCapture.Reset(session);
		ElderTrailCapture.Reset(session);
		MpPlayerTrailCapture.Reset(session);
	}

	/// <summary>
	/// write v3 lod + traps when the walked path grew (or first time on the layer).
	/// keep-hi follows the trail, not just spawn. later v3 grids replace earlier ones.
	/// overview isnt written anymore, ui just reads v3. this shit dont work at ui scale.
	/// </summary>
	internal static bool TryCaptureLayerOnce(RunSession session, Body body, out string terrainDetail, out string trapPositions)
	{
		terrainDetail = "";
		trapPositions = "";
		if (session == null)
			return false;
		var world = WorldGeneration.world;
		if (world == null || !world.worldExists || world.generatingWorld)
			return false;
		bool hasRemotes = LayerHasRemoteTrails(session);
		int pathCount = CountKeepSamples(session);
		if (session.TerrainStoredForBiomeDepth == world.biomeDepth)
		{
			bool remotesAlreadyIn = session.TerrainKeepIncludedRemotes || !hasRemotes;
			bool pathUnchanged = pathCount <= session.TerrainStoredPathCount;
			if (remotesAlreadyIn && pathUnchanged)
				return false;
		}

		terrainDetail = CaptureLayerLod(session, body);
		if (string.IsNullOrEmpty(terrainDetail))
			return false;

		trapPositions = Convert.ToBase64String(PackTraps(world, TrapPositionTracker.SnapshotNormalized(world)));
		session.TerrainStoredForBiomeDepth = world.biomeDepth;
		session.TerrainKeepIncludedRemotes = hasRemotes;
		session.TerrainStoredPathCount = pathCount;
		return true;
	}

	internal static string CaptureLayerLod(RunSession session, Body body)
	{
		var world = WorldGeneration.world;
		if (world == null || !world.worldExists || world.generatingWorld)
			return "";
		int width = (int)world.width;
		int height = (int)world.height;
		if (width < GridSize || height < GridSize)
			return "";

		int gridW = Math.Max(1, (width + DetailTileSize - 1) / DetailTileSize);
		int gridH = Math.Max(1, (height + DetailTileSize - 1) / DetailTileSize);
		var cells = new byte[gridW * gridH];
		for (int gy = 0; gy < gridH; gy++)
		{
			int gyb = gridH - 1 - gy;
			int blockY = Math.Min(height - 1, gyb * DetailTileSize + DetailTileSize / 2);
			for (int gx = 0; gx < gridW; gx++)
			{
				int blockX = Math.Min(width - 1, gx * DetailTileSize + DetailTileSize / 2);
				cells[gy * gridW + gx] = (byte)Classify(world, blockX, blockY);
			}
		}

		bool[] keep = BuildKeepMask(session, body, gridW, gridH);
		int kept = 0;
		for (int i = 0; i < keep.Length; i++)
		{
			if (keep[i])
				kept++;
		}
		DownscaleFar(cells, keep, gridW, gridH);

		byte[] packed = PackGridV3(cells, gridW, gridH);
		LbLog.Step("Capture:Terrain",
			$"v3 {gridW}x{gridH} keep={kept}/{cells.Length} bytes={packed.Length}");
		return Convert.ToBase64String(packed);
	}

	internal static void WritePackedFile(string path, string terrainBase64)
	{
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(terrainBase64))
			return;
		try
		{
			File.WriteAllBytes(path, Convert.FromBase64String(terrainBase64));
			LbLog.Step("Capture:TerrainFile", path);
		}
		catch (Exception ex)
		{
			LbLog.Warn("Capture:TerrainFile", ex.Message);
		}
	}

	private static bool[] BuildKeepMask(RunSession session, Body body, int gridW, int gridH)
	{
		var keep = new bool[gridW * gridH];
		int radiusCells = Math.Max(1, (KeepRadiusTiles + DetailTileSize - 1) / DetailTileSize);
		int radiusSq = radiusCells * radiusCells;

		void Mark(double blockX, double blockY)
		{
			int gx = ClampIndex((int)(blockX / DetailTileSize), gridW);
			int gyb = ClampIndex((int)(blockY / DetailTileSize), gridH);
			int gy = gridH - 1 - gyb;
			for (int dy = -radiusCells; dy <= radiusCells; dy++)
			{
				int y = gy + dy;
				if (y < 0 || y >= gridH)
					continue;
				for (int dx = -radiusCells; dx <= radiusCells; dx++)
				{
					if (dx * dx + dy * dy > radiusSq)
						continue;
					int x = gx + dx;
					if (x < 0 || x >= gridW)
						continue;
					keep[y * gridW + x] = true;
				}
			}
		}

		if (session?.LayerPath != null)
		{
			for (int i = 0; i < session.LayerPath.Count; i++)
				Mark(session.LayerPath[i].BlockX, session.LayerPath[i].BlockY);
		}
		if (session?.MpPlayerTrails != null)
		{
			foreach (var trail in session.MpPlayerTrails.Values)
			{
				if (trail?.LayerPath == null)
					continue;
				for (int i = 0; i < trail.LayerPath.Count; i++)
					Mark(trail.LayerPath[i].BlockX, trail.LayerPath[i].BlockY);
			}
		}
		if (LayerPositionCapture.TryCapture(body, out var current))
			Mark(current.BlockX, current.BlockY);

		if (KrokMpOptional.IsNetworkRunning)
		{
			var remotes = new List<KrokMpOptional.RemotePlayerPose>(8);
			KrokMpOptional.CollectRemotePlayerPoses(remotes);
			for (int i = 0; i < remotes.Count; i++)
			{
				var remote = remotes[i];
				if (!remote.LoadedIn)
					continue;
				if (remote.IsOriginStub && !MpPlayerTrailCapture.HasStartedTrail(session, remote.TrailId))
					continue;
				if (LayerPositionCapture.TryCaptureWorld(remote.Position, out var remotePos))
					Mark(remotePos.BlockX, remotePos.BlockY);
			}
		}
		return keep;
	}

	private static int CountKeepSamples(RunSession session)
	{
		int count = session?.LayerPath != null ? session.LayerPath.Count : 0;
		if (session?.MpPlayerTrails == null)
			return count;
		foreach (var trail in session.MpPlayerTrails.Values)
		{
			if (trail?.LayerPath != null)
				count += trail.LayerPath.Count;
		}
		return count;
	}

	private static bool LayerHasRemoteTrails(RunSession session)
	{
		if (!KrokMpOptional.IsNetworkRunning)
			return false;
		if (session?.MpPlayerTrails != null)
		{
			foreach (var trail in session.MpPlayerTrails.Values)
			{
				if (trail?.LayerPath != null && trail.LayerPath.Count > 0)
					return true;
			}
		}
		var remotes = new List<KrokMpOptional.RemotePlayerPose>(8);
		KrokMpOptional.CollectRemotePlayerPoses(remotes);
		for (int i = 0; i < remotes.Count; i++)
		{
			if (remotes[i].LoadedIn && !remotes[i].IsOriginStub)
				return true;
		}
		return false;
	}

	private static void DownscaleFar(byte[] cells, bool[] keep, int gridW, int gridH)
	{
		int chunk = FarChunkHi;
		int loW = Math.Max(1, (gridW + chunk - 1) / chunk);
		int loH = Math.Max(1, (gridH + chunk - 1) / chunk);
		var lo = new byte[loW * loH];
		for (int ly = 0; ly < loH; ly++)
		{
			for (int lx = 0; lx < loW; lx++)
			{
				int c0 = 0, c1 = 0, c2 = 0;
				int y1 = Math.Min(gridH, ly * chunk + chunk);
				int x1 = Math.Min(gridW, lx * chunk + chunk);
				for (int y = ly * chunk; y < y1; y++)
				{
					int row = y * gridW;
					for (int x = lx * chunk; x < x1; x++)
					{
						int cls = cells[row + x];
						if (cls == 0)
							c0++;
						else if (cls == 1)
							c1++;
						else
							c2++;
					}
				}

				byte maj = 2;
				int best = c2;
				if (c1 > best)
				{
					best = c1;
					maj = 1;
				}
				if (c0 > best)
					maj = 0;
				lo[ly * loW + lx] = maj;
			}
		}

		for (int i = 0; i < cells.Length; i++)
		{
			if (keep[i])
				continue;
			int y = i / gridW;
			int x = i - y * gridW;
			cells[i] = lo[(y / chunk) * loW + (x / chunk)];
		}
	}

	private static byte[] PackGridV3(byte[] cells, int gridW, int gridH)
	{
		int packedLen = (cells.Length + 3) / 4;
		var packed = new byte[6 + packedLen];
		packed[0] = 3;
		packed[1] = (byte)DetailTileSize;
		packed[2] = (byte)(gridW & 0xff);
		packed[3] = (byte)((gridW >> 8) & 0xff);
		packed[4] = (byte)(gridH & 0xff);
		packed[5] = (byte)((gridH >> 8) & 0xff);
		for (int i = 0; i < cells.Length; i++)
		{
			int byteIndex = 6 + (i >> 2);
			int shift = (i & 3) * 2;
			packed[byteIndex] |= (byte)((cells[i] & 3) << shift);
		}
		return packed;
	}

	private static int ClampIndex(int value, int size)
	{
		if (size <= 0)
			return 0;
		return Math.Max(0, Math.Min(size - 1, value));
	}

	private static int Classify(WorldGeneration world, int blockX, int blockY)
	{
		var pos = new Vector2Int(blockX, blockY);
		ushort block = world.GetBlock(pos);
		if (block == 0)
			return 0;
		return world.isSoil(pos) ? 1 : 2;
	}

	private static byte[] PackTraps(WorldGeneration world, List<Vector2> normalized)
	{
		int count = Math.Min(normalized.Count, 800);
		var bytes = new byte[count * 4];
		for (int i = 0; i < count; i++)
		{
			int nx = ClampCoord(normalized[i].x);
			int ny = ClampCoord(normalized[i].y);
			bytes[i * 4] = (byte)(nx & 0xff);
			bytes[i * 4 + 1] = (byte)((nx >> 8) & 0xff);
			bytes[i * 4 + 2] = (byte)(ny & 0xff);
			bytes[i * 4 + 3] = (byte)((ny >> 8) & 0xff);
		}
		return bytes;
	}

	private static int ClampCoord(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
			return 0;
		return Math.Max(0, Math.Min(TrapCoordMax, (int)Math.Round(value * TrapCoordMax)));
	}
}

internal static class TrapPositionTracker
{
	private static readonly List<Transform> Traps = new List<Transform>(256);

	internal static void ResetLayer()
	{
		Traps.Clear();
	}

	internal static void Register(GameObject go)
	{
		if (go == null)
			return;
		string name = go.name ?? "";
		if (name.IndexOf("drillpod", StringComparison.OrdinalIgnoreCase) >= 0)
			return;
		Traps.Add(go.transform);
	}

	internal static List<Vector2> SnapshotNormalized(WorldGeneration world)
	{
		var result = new List<Vector2>(Traps.Count);
		if (world == null || world.width <= 0 || world.height <= 0)
			return result;

		double width = world.width;
		double height = world.height;
		for (int i = Traps.Count - 1; i >= 0; i--)
		{
			var transform = Traps[i];
			if (transform == null)
			{
				Traps.RemoveAt(i);
				continue;
			}

			Vector3 pos = transform.position;
			double nx = (pos.x + world.halfWidth) / width;
			double ny = (pos.y + world.halfHeight) / height;
			if (nx < 0d || nx > 1d || ny < 0d || ny > 1d)
				continue;
			result.Add(new Vector2((float)nx, (float)ny));
		}

		return result;
	}
}
