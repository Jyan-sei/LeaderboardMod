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

## File layout (for review)

Start at `Plugin.cs` / `PluginInfo.cs`. That's boot, config, harmony PatchAll, and the 60s snapshot timer.

| folder | what it is |
|---|---|
| `Session/` | run lifetime. save ids, "should we even track this", MP host vs client, load/continue rules, invalidation |
| `Tracking/Patches/` | harmony hooks into the game (start run, death, crafts, traps, medical, traders, console, QoL slots, KrokMP if present) |
| `Tracking/` | counters those patches write into. `RunEventTracker` is the main dump |
| `Capture/` | actually building a snapshot. sidecar fields, path/elder/mp trails, terrain lod, medical events, isolated save path |
| `Storage/` | sqlite. one table per run, insert after capture |
| `Sync/` | upload worker. hmac + ecdsa + jwt + cert pin. retry / rewind if the server is ahead |
| `Identity/` | steam id + machine hash + loaded mod list |
| `Data/` | `SidecarSnapshot` dto. that's the blob we persist |
| `Logging/` | `LbLog`. noisy on purpose |

`KrokMpOptional.cs` is the reflection glue so we don't compile against KrokMP. If that dll isn't loaded, those hooks just no-op.

A couple leftovers are still in tree (console whitelist, `EnsureSessionForLateStart`, empty `terrainOverview`). They don't do anything useful anymore. Comments on those spots say why.
