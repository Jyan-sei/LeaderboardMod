using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using LeaderboardMod.Logging;
using Steamworks;
using UnityEngine;

namespace LeaderboardMod.Identity;

/// <summary>
/// steamworks.net for steamid when krokmp isnt around.
/// if krokmp already init steam we just read the id, no double init.
/// </summary>
internal static class SteamBootstrap
{
	private const string NativeDllName = "steam_api64.dll";

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string lpFileName);

	private static bool _nativeLoaded;
	private static bool _initialized;
	private static bool _callbacksEnabled;
	private static bool _initFailed;

	internal static bool IsReady => _initialized && !_initFailed;

	internal static void Initialize(string pluginDirectory)
	{
		if (_initialized || _initFailed)
			return;

		if (TryGetSteamIdViaKrokMp(out _))
		{
			_initialized = true;
			_callbacksEnabled = false;
			LbLog.Step("Steam", "Using Steam ID from KrokMP (already initialized)");
			return;
		}

		try
		{
			EnsureNativeLibrary(pluginDirectory);
			if (!Packsize.Test())
			{
				LbLog.Warn("Steam", "Packsize.Test failed - Steam ID unavailable");
				_initFailed = true;
				return;
			}
			if (!DllCheck.Test())
			{
				LbLog.Warn("Steam", "DllCheck.Test failed - Steam ID unavailable");
				_initFailed = true;
				return;
			}

			if (SteamAPI.InitEx(out var err) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
			{
				LbLog.Warn("Steam", $"SteamAPI.InitEx failed: {err}");
				_initFailed = true;
				return;
			}

			_initialized = true;
			_callbacksEnabled = true;
			LbLog.Step("Steam", $"Steamworks initialized steamId={GetSteamId()}");
		}
		catch (Exception ex)
		{
			_initFailed = true;
			LbLog.Warn("Steam", $"Steam bootstrap failed: {ex.Message}");
		}
	}

	internal static void Tick()
	{
		if (!_callbacksEnabled)
			return;
		try
		{
			SteamAPI.RunCallbacks();
		}
		catch (Exception ex)
		{
			LbLog.Warn("Steam", $"RunCallbacks failed: {ex.Message}");
		}
	}

	internal static void Shutdown()
	{
		if (!_callbacksEnabled)
			return;
		try
		{
			SteamAPI.Shutdown();
		}
		catch
		{
			// ignore
		}
		_initialized = false;
		_callbacksEnabled = false;
	}

	internal static string GetSteamId()
	{
		if (TryGetSteamIdViaKrokMp(out var viaKrok))
			return viaKrok;
		if (!_initialized)
			return null;
		try
		{
			return SteamUser.GetSteamID().m_SteamID.ToString();
		}
		catch (Exception ex)
		{
			LbLog.Warn("Steam", $"GetSteamID failed: {ex.Message}");
			return null;
		}
	}

	private static bool TryGetSteamIdViaKrokMp(out string steamId)
	{
		steamId = null;
		try
		{
			Type ksteam = AccessToolsTypeByName("KrokoshaCasualtiesMP.KSteam");
			if (ksteam == null)
				return false;

			PropertyInfo loadedProp = ksteam.GetProperty("Loaded", BindingFlags.Public | BindingFlags.Static);
			if (loadedProp == null || !(loadedProp.GetValue(null) is bool loaded) || !loaded)
				return false;

			MethodInfo getId = ksteam.GetMethod("GetLocalUserSteamID", BindingFlags.Public | BindingFlags.Static);
			if (getId == null)
				return false;

			object idObj = getId.Invoke(null, null);
			if (idObj == null)
				return false;

			FieldInfo steamIdField = idObj.GetType().GetField("m_SteamID");
			if (steamIdField == null)
				return false;

			steamId = steamIdField.GetValue(idObj)?.ToString();
			return !string.IsNullOrWhiteSpace(steamId);
		}
		catch
		{
			return false;
		}
	}

	private static void EnsureNativeLibrary(string pluginDirectory)
	{
		if (_nativeLoaded)
			return;

		string dir = pluginDirectory;
		if (!string.IsNullOrEmpty(dir) && File.Exists(dir))
			dir = Path.GetDirectoryName(dir) ?? dir;

		string nativePath = Path.Combine(dir, NativeDllName);
		if (!File.Exists(nativePath))
			throw new FileNotFoundException($"{NativeDllName} not found in plugin folder", nativePath);

		if (LoadLibrary(nativePath) == IntPtr.Zero)
			throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary failed for " + nativePath);

		_nativeLoaded = true;
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
}
