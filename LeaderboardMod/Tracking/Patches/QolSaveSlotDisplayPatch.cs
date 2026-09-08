using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using UnityEngine;

namespace LeaderboardMod.Tracking.Patches;

/// <summary>
/// qol pause menu slots - third row (timestamp) shows leaderboard status from leaderboardSaveId.
/// patched late, qol loads after our Awake.
/// </summary>
internal static class QolSaveSlotDisplayPatch
{
	internal const string TrackedLabel = "[Leaderboard Tracked]";
	internal const string NotTrackedLabel = "[Not Leaderboard Tracked]";

	private static readonly Regex SeedIdTagRegex = new Regex(
		@"<color=yellow>\[ID:\s*[^\]]+\]</color>\s*",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex LeaderboardLabelRegex = new Regex(
		@"\s*\[(?:Not )?Leaderboard Tracked\]",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private const float ResolveDeadlineSeconds = 30f;

	private static Harmony _harmony;
	private static bool _applied;
	private static bool _gaveUp;
	private static float _deadlineUnscaled;

	internal static void Begin(Harmony harmony)
	{
		_harmony = harmony;
		TryApply();
	}

	internal static void Tick()
	{
		if (_applied || _gaveUp || _harmony == null)
			return;

		TryApply();
	}

	private static void TryApply()
	{
		if (_applied || _gaveUp || _harmony == null)
			return;

		Type patcher = AccessTools.TypeByName("QoL_Unknown.SaveSaveMenuPatcher");
		if (patcher == null)
		{
			if (_deadlineUnscaled <= 0f)
				_deadlineUnscaled = Time.unscaledTime + ResolveDeadlineSeconds;
			if (Time.unscaledTime >= _deadlineUnscaled)
			{
				_gaveUp = true;
				LbLog.Warn("QoL:SlotUI", "QoL Unknown not present - save slot label patch skipped");
			}
			return;
		}

		MethodInfo refresh = AccessTools.Method(patcher, "RefreshSlots");
		if (refresh == null)
		{
			LbLog.Warn("QoL:SlotUI", "SaveSaveMenuPatcher.RefreshSlots not found");
			_gaveUp = true;
			return;
		}

		_harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(QolSaveSlotDisplayPatch), nameof(RefreshSlotsPostfix)));
		_applied = true;
		LbLog.Step("QoL:SlotUI", "Patched SaveSaveMenuPatcher.RefreshSlots");
	}

	private static void RefreshSlotsPostfix()
	{
		Type patcher = AccessTools.TypeByName("QoL_Unknown.SaveSaveMenuPatcher");
		if (patcher == null)
			return;

		FieldInfo slotContainerField = AccessTools.Field(patcher, "slotContainer");
		Transform slotContainer = slotContainerField?.GetValue(null) as Transform;
		if (slotContainer == null)
			return;

		string saveFolder = Application.persistentDataPath;
		for (int i = 1; i < slotContainer.childCount; i++)
		{
			Transform slot = slotContainer.GetChild(i);
			Transform info = slot.Find("Content/Info");
			if (info == null)
				continue;

			Type tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
			if (tmpType == null)
				continue;

			Component tmp = ((Component)info).GetComponent(tmpType);
			if (tmp == null)
				continue;

			PropertyInfo textProp = AccessTools.Property(tmpType, "text");
			if (textProp == null)
				continue;

			string savePath = Path.Combine(saveFolder, $"slot_{i}.sv");
			if (!File.Exists(savePath))
				continue;

			string text = textProp.GetValue(tmp) as string;
			if (string.IsNullOrEmpty(text))
				continue;

			string saveId = LeaderboardSaveId.TryReadFromSavePath(savePath);
			text = StripSeedIdTag(text);
			text = StripRawSaveId(text, saveId);
			text = StripLeaderboardLabels(text);

			string label = LeaderboardSaveId.IsValid(saveId) ? TrackedLabel : NotTrackedLabel;
			text = AppendLabelToTimestampRow(text, label);
			textProp.SetValue(tmp, text);
		}
	}

	private static string StripSeedIdTag(string text)
	{
		if (string.IsNullOrEmpty(text))
			return text;

		return SeedIdTagRegex.Replace(text, string.Empty);
	}

	private static string StripRawSaveId(string text, string saveId)
	{
		if (string.IsNullOrEmpty(text) || !LeaderboardSaveId.IsValid(saveId))
			return text;

		return text.Replace(saveId, string.Empty);
	}

	private static string StripLeaderboardLabels(string text)
	{
		if (string.IsNullOrEmpty(text))
			return text;

		return LeaderboardLabelRegex.Replace(text, string.Empty);
	}

	/// <summary>qol row 3 is the grey timestamp line, we append status after it</summary>
	private static string AppendLabelToTimestampRow(string text, string label)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(label))
			return text;

		const string rowClose = "</color></size>";
		int closeIndex = text.LastIndexOf(rowClose, StringComparison.Ordinal);
		if (closeIndex < 0)
			return text + $"\n<size=60%>{label}</size>";

		return text.Insert(closeIndex, " " + label);
	}
}
