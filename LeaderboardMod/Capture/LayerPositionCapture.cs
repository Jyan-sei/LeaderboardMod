using System;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal readonly struct LayerPositionSnapshot
{
	internal readonly double BlockX;
	internal readonly double BlockY;
	internal readonly double NormalizedX;
	internal readonly double NormalizedY;
	internal readonly int LayerWidth;
	internal readonly int LayerHeight;

	internal LayerPositionSnapshot(
		double blockX, double blockY, double normalizedX, double normalizedY, int layerWidth, int layerHeight)
	{
		BlockX = blockX;
		BlockY = blockY;
		NormalizedX = normalizedX;
		NormalizedY = normalizedY;
		LayerWidth = layerWidth;
		LayerHeight = layerHeight;
	}
}

internal static class LayerPositionCapture
{
	/// <summary>
	/// layer block space. x left to right (0 = west), y bottom to top.
	/// same as WorldToBlockPos.
	/// </summary>
	internal static bool TryCapture(Body body, out LayerPositionSnapshot snapshot)
	{
		if (body == null)
		{
			snapshot = default;
			return false;
		}
		return TryCaptureWorld(body.transform.position, out snapshot);
	}

	internal static bool TryCaptureWorld(Vector2 worldPos, out LayerPositionSnapshot snapshot)
	{
		snapshot = default;
		if (WorldGeneration.world == null)
			return false;

		var world = WorldGeneration.world;
		Vector2 pos = worldPos;

		double width = world.width;
		double height = world.height;
		if (width <= 0 || height <= 0)
			return false;

		double blockX = pos.x + world.halfWidth;
		double blockY = pos.y + world.halfHeight;
		blockX = Math.Max(0d, Math.Min(width, blockX));
		blockY = Math.Max(0d, Math.Min(height, blockY));

		snapshot = new LayerPositionSnapshot(
			blockX,
			blockY,
			blockX / width,
			blockY / height,
			(int)width,
			(int)height);
		return true;
	}
}
