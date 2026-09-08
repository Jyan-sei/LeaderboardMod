using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace LeaderboardMod.Storage;

internal static class SqliteBootstrap
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string lpFileName);

	internal static void Initialize(string pluginDir)
	{
		string nativePath = Path.Combine(pluginDir, "e_sqlite3.dll");
		if (!File.Exists(nativePath))
			nativePath = Path.Combine(pluginDir, "runtimes", "win-x64", "native", "e_sqlite3.dll");

		if (!File.Exists(nativePath))
			throw new FileNotFoundException("e_sqlite3.dll not found in plugin folder", nativePath);

		if (LoadLibrary(nativePath) == IntPtr.Zero)
			throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary failed for " + nativePath);

		raw.SetProvider(new SQLite3Provider_e_sqlite3());
	}
}
