using System;
using System.Reflection;
using HarmonyLib;

namespace LeaderboardMod.Capture;

internal readonly struct WorldSeedSnapshot
{
	internal WorldSeedSnapshot(int seed, string input, bool isSeeded)
	{
		Seed = seed;
		Input = input;
		IsSeeded = isSeeded;
	}

	internal int Seed { get; }
	internal string Input { get; }
	internal bool IsSeeded { get; }
}

/// <summary>
/// world seed from qol SeedManager if its there (reflection, no compile time dep)
/// </summary>
internal static class WorldSeedCapture
{
	internal static WorldSeedSnapshot Capture()
	{
		try
		{
			Type seedManager = AccessToolsTypeByName("QoL_Unknown.SeedManager");
			if (seedManager == null)
				return default;

			int seed = ReadStaticInt(seedManager, "CurrentSeed");
			bool isSeeded = ReadStaticBool(seedManager, "IsSeeded");
			string input = ReadStaticString(seedManager, "InputString");
			return new WorldSeedSnapshot(seed, input, isSeeded);
		}
		catch
		{
			return default;
		}
	}

	private static Type AccessToolsTypeByName(string name)
	{
		foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type type = asm.GetType(name, throwOnError: false);
			if (type != null)
				return type;
		}
		return null;
	}

	private static int ReadStaticInt(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		if (field == null)
			return 0;
		object value = field.GetValue(null);
		if (value == null)
			return 0;
		return Convert.ToInt32(value);
	}

	private static bool ReadStaticBool(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		return field != null && field.GetValue(null) is bool b && b;
	}

	private static string ReadStaticString(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		if (field?.GetValue(null) is not string raw)
			return null;
		raw = raw.Trim();
		return raw.Length > 0 ? raw : null;
	}
}
