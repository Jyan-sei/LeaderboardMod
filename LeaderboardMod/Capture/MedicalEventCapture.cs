using System;
using System.Collections.Generic;
using System.Globalization;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using Newtonsoft.Json;
using UnityEngine;

namespace LeaderboardMod.Capture;

internal sealed class MedicalEventItem
{
	public string item;
	public string timestamp;
	public double x;
	public double y;
	public int biome;
	[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
	public string limb;
}

internal static class MedicalEventCapture
{
	private const float InfectionStart = 1f;
	private const float SepsisStart = 0.01f;
	private const float KnockoutThreshold = 30f;
	private const int MaxPending = 40;

	private static bool _seeded;
	private static int _limbCount;
	private static bool[] _infected;
	private static bool[] _broken;
	private static bool[] _dislocated;
	private static bool[] _dismembered;
	private static bool _sepsis;
	private static bool _knockedOut;

	internal static void Reset()
	{
		_seeded = false;
		_limbCount = 0;
		_infected = null;
		_broken = null;
		_dislocated = null;
		_dismembered = null;
		_sepsis = false;
		_knockedOut = false;
	}

	internal static void Tick()
	{
		if (!RunSessionManager.ShouldCapture())
			return;
		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return;
		var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
		if (body == null || body.limbs == null || body.limbs.Length == 0)
			return;

		if (!_seeded || _limbCount != body.limbs.Length)
		{
			Seed(body);
			return;
		}

		Compare(session, body);
	}

	internal static string Flush(RunSession session)
	{
		if (session == null || session.PendingMedicalEvents.Count == 0)
			return "";
		string json = JsonConvert.SerializeObject(session.PendingMedicalEvents);
		session.PendingMedicalEvents.Clear();
		return json;
	}

	private static void Seed(Body body)
	{
		int n = body.limbs.Length;
		_limbCount = n;
		_infected = new bool[n];
		_broken = new bool[n];
		_dislocated = new bool[n];
		_dismembered = new bool[n];
		for (int i = 0; i < n; i++)
		{
			var limb = body.limbs[i];
			if (limb == null)
				continue;
			_dismembered[i] = limb.dismembered;
			_broken[i] = limb.broken;
			_dislocated[i] = limb.dislocated;
			_infected[i] = limb.infectionAmount > InfectionStart;
		}
		_sepsis = body.septicShock > SepsisStart;
		_knockedOut = body.alive && body.consciousness < KnockoutThreshold;
		_seeded = true;
	}

	private static void Compare(RunSession session, Body body)
	{
		int n = Math.Min(_limbCount, body.limbs.Length);
		for (int i = 0; i < n; i++)
		{
			var limb = body.limbs[i];
			if (limb == null)
				continue;

			bool amputated = limb.dismembered;
			if (amputated && !_dismembered[i])
				Emit(session, body, "amputation", LimbLabel(limb));
			else if (!amputated)
			{
				if (limb.broken && !_broken[i])
					Emit(session, body, "bone_broken", LimbLabel(limb));
				if (limb.dislocated && !_dislocated[i])
					Emit(session, body, "fracture", LimbLabel(limb));
				if (limb.infectionAmount > InfectionStart && !_infected[i])
					Emit(session, body, "infection", LimbLabel(limb));
			}

			_dismembered[i] = amputated;
			_broken[i] = limb.broken;
			_dislocated[i] = limb.dislocated;
			_infected[i] = limb.infectionAmount > InfectionStart;
		}

		bool septic = body.septicShock > SepsisStart;
		if (septic && !_sepsis)
			Emit(session, body, "sepsis", null);
		_sepsis = septic;

		bool knocked = body.alive && body.consciousness < KnockoutThreshold;
		if (knocked && !_knockedOut)
			Emit(session, body, "knockout", null);
		_knockedOut = knocked;
	}

	private static void Emit(RunSession session, Body body, string item, string limb)
	{
		if (session.PendingMedicalEvents.Count >= MaxPending)
			return;

		TryPos(session, body, out double x, out double y, out int biome);
		var ev = new MedicalEventItem
		{
			item = item,
			timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
			x = x,
			y = y,
			biome = biome,
			limb = string.IsNullOrEmpty(limb) ? null : limb,
		};
		session.PendingMedicalEvents.Add(ev);
		LbLog.Step("Capture:Medical",
			limb == null ? $"{item} at {ev.timestamp}" : $"{item} {limb} at {ev.timestamp}");
	}

	private static void TryPos(RunSession session, Body body, out double x, out double y, out int biome)
	{
		biome = WorldGeneration.world != null ? WorldGeneration.world.biomeDepth : -1;
		if (LayerPositionCapture.TryCapture(body, out var pos))
		{
			x = pos.NormalizedX;
			y = pos.NormalizedY;
			return;
		}
		if (session.LayerPath.Count > 0)
		{
			var last = session.LayerPath[session.LayerPath.Count - 1];
			x = last.NormalizedX;
			y = last.NormalizedY;
			return;
		}
		x = 0.5d;
		y = 0.5d;
	}

	private static string LimbLabel(Limb limb)
	{
		if (limb == null)
			return "";
		if (!string.IsNullOrEmpty(limb.shortName))
			return limb.shortName;
		if (!string.IsNullOrEmpty(limb.fullName))
			return limb.fullName;
		return limb.gameObject != null ? limb.gameObject.name : "";
	}
}
