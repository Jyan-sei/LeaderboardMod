# LeaderboardMod

BepInEx plugin for Casualties Unknown. Captures run snapshots locally and syncs them to the leaderboard backend.

Version: **0.7.40**

## Requirements

- Casualties Unknown (Steam)
- [BepInEx 5](https://github.com/BepInEx/BepInEx)
- .NET SDK (build target: `net48`)

Game assemblies are referenced from the Steam install. Override the path if needed:

```bat
dotnet build -p:CUGameDir="D:\Steam\steamapps\common\Casualties Unknown Demo"
```

KrokMP is optional. The plugin reflects it at runtime when present.

## Install (players)

Copy `LeaderboardMod.dll` plus the bundled SQLite native files into `BepInEx/plugins/LeaderboardMod/`.
