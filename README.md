# ATTBuilding

A Township Tale building mod for **PC (Mono)** and **Quest (IL2CPP)**, built
with the same dual-project layout as
[fizzjay/ArmorUpdate](https://github.com/fizzjay/ArmorUpdate): one `shared/`
core linked into both builds, a legacy Mono project in `pchost/`, and an
SDK-style net8.0 IL2CPP project in `quest/`.

## What it does

Open your **wrist (quick-access) menu** and pick the new **ATT Building
Console** bubble. That summons the game's console in front of you. Type:

| Command | Effect |
|---|---|
| `build spawn <prefab name> [count]` (or just `spawn <name>`) | Spawns that prefab on the ground in front of you. Names match `PrefabManager` prefabs, case-insensitively; a hash works too. |
| `build nick <prefab name\|id> <nickname>` | Saves a personal nickname for a prefab, e.g. `build nick anvil anv` -- then `spawn anv` spawns an Anvil. Nicknames live in `UserData/ATTBuilding/nicks.txt` on your own machine and work anywhere you play. |
| `build nicks` / `build del nick <nickname>` | List your saved prefab nicknames / delete one. |
| `build mode on` / `buildmode on` | **Build mode**: anything *this console spawned* can be grabbed and moved exactly like an item. World items are never touched. |
| `snapping true` / `snapping false` | While you move a spawned object, its rotation is locked to set angles, so it stays straight — you can still point it any way, just on the angle steps. |
| `build snapangle <deg>` | Angle step (default `15`; `45` is coarser, `0` disables rotation snapping). |
| `build grid <metres>` | Position grid applied when you let go (default `0.25`, `0` = off). |
| `build livesnap true` | Also snap position every frame while held (off by default — it can fight the hand joint). |
| `build list [filter]` | Prefab names/hashes you can spawn. |
| `build spawned` | How many console-spawned objects are tracked. |
| `build clear` | Remove everything this console spawned. |
| `build help` | All of the above, in-game. |

## How spawning works (important)

Only a server (or a local/host session) may authoritatively spawn networked
prefabs — that's the game's rule (`SpawnHelper` / `NetworkSceneManager`),
not this mod's. So:

- **Local / host / server**: spawning happens directly where you stand.
- **Quest or PC client on a multiplayer server**: the client resolves the
  name to a prefab hash and routes `build spawn <you> <hash>` through
  `CommandSync` to the server. If that server runs the **PC build** of
  ATTBuilding, its `Alta.Console` module spawns it in front of you. If not,
  the console tells you after ~10 s instead of pretending it worked.

## Layout

```
shared/   BuildState, CommandParser, SnapMath, VectorSnap, TrackedRegistry
          (no game references beyond UnityEngine maths -- identical in both builds)
pchost/   Mono build: console hook, wrist bubble (reflection-bound),
          spawner, build mode, plus ServerCommands (the server spawn module)
quest/    IL2CPP build: same features with Il2Cpp interop types
          (EquipmentMenu-style bubble, ShardClient-style console hook,
          DelegateSupport-converted CommandSync routing)
tests/    python contract tests (parity, command coverage, snap maths)
```

See `BUILD.md` for build/install steps and the seams most likely to need a
one-line fix after a game update.
