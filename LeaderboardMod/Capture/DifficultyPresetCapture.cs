using System;
using System.Collections.Generic;
using System.Globalization;
using LeaderboardMod.Logging;

namespace LeaderboardMod.Capture;

internal readonly struct DifficultyPresetSnapshot
{
	internal DifficultyPresetSnapshot(string presetName, int presetIndex, bool isCustom)
	{
		PresetName = presetName ?? "unknown";
		PresetIndex = presetIndex;
		IsCustom = isCustom;
	}

	internal string PresetName { get; }
	internal int PresetIndex { get; }
	internal bool IsCustom { get; }
}

/// <summary>
/// grab the prerun difficulty preset from PreRunScript if we can,
/// otherwise just match runSettings against the presets
/// </summary>
internal static class DifficultyPresetCapture
{
	private const int CustomPresetIndex = 6;

	internal static DifficultyPresetSnapshot Capture()
	{
		try
		{
			if (PreRunScript.instance != null)
			{
				int index = PreRunScript.instance.currentPreset;
				if (index == CustomPresetIndex)
					return new DifficultyPresetSnapshot("custom", index, isCustom: true);

				if (index >= 0 && index < RunSettings.presets.Count)
				{
					string name = RunSettings.presets[index].presetName;
					return new DifficultyPresetSnapshot(name, index, isCustom: false);
				}
			}
		}
		catch (Exception ex)
		{
			LbLog.Warn("Capture:Difficulty", $"PreRunScript preset read failed: {ex.Message}");
		}

		return InferFromWorldRunSettings();
	}

	private static DifficultyPresetSnapshot InferFromWorldRunSettings()
	{
		Dictionary<string, object> current = WorldGeneration.runSettings;
		if (current == null || current.Count == 0)
			return new DifficultyPresetSnapshot("unknown", -1, isCustom: false);

		try
		{
			Dictionary<string, object> normalBase = RunSettings.GetPreset("normal").presetValues;
			for (int i = 0; i < RunSettings.presets.Count; i++)
			{
				RunSettingsPreset preset = RunSettings.presets[i];
				if (SettingsMatchPreset(current, normalBase, preset))
					return new DifficultyPresetSnapshot(preset.presetName, i, isCustom: false);
			}
		}
		catch (Exception ex)
		{
			LbLog.Warn("Capture:Difficulty", $"Preset inference failed: {ex.Message}");
		}

		return new DifficultyPresetSnapshot("custom", CustomPresetIndex, isCustom: true);
	}

	private static bool SettingsMatchPreset(
		Dictionary<string, object> current,
		Dictionary<string, object> normalBase,
		RunSettingsPreset preset)
	{
		foreach (KeyValuePair<string, object> kv in preset.presetValues)
		{
			if (!current.TryGetValue(kv.Key, out object cur))
				return false;
			if (!ValuesEqual(cur, kv.Value))
				return false;
		}

		foreach (KeyValuePair<string, object> kv in normalBase)
		{
			if (preset.presetValues.ContainsKey(kv.Key))
				continue;
			if (!current.TryGetValue(kv.Key, out object cur))
				continue;
			if (!ValuesEqual(cur, kv.Value))
				return false;
		}

		return true;
	}

	private static bool ValuesEqual(object left, object right)
	{
		if (left == null && right == null)
			return true;
		if (left == null || right == null)
			return false;

		if (left is bool lb && right is bool rb)
			return lb == rb;

		if (TryToDouble(left, out double ld) && TryToDouble(right, out double rd))
			return Math.Abs(ld - rd) < 0.0001;

		return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture),
			Convert.ToString(right, CultureInfo.InvariantCulture),
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryToDouble(object value, out double result)
	{
		result = 0;
		if (value == null)
			return false;

		switch (value)
		{
			case double d:
				result = d;
				return true;
			case float f:
				result = f;
				return true;
			case int i:
				result = i;
				return true;
			case long l:
				result = l;
				return true;
			default:
				return double.TryParse(
					Convert.ToString(value, CultureInfo.InvariantCulture),
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out result);
		}
	}
}
