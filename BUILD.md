# Building ATTBuilding

Reference assemblies are proprietary, machine-local, and **never committed**
(the ArmorUpdate convention): `refs/quest/`, `refs/pc/`, `quest/Quest.Local.props`,
and all `bin/`/`obj/` are gitignored.

## Quest (IL2CPP) -- `quest/ATTBuilding.csproj`

1. Use VS2022 17.8+ or the .NET 8 SDK (`dotnet build quest/ATTBuilding.csproj -c Release`).
2. Copy your Quest install's generated interop assemblies, Unity references,
   MelonLoader, Il2CppInterop.Runtime and 0Harmony into `refs/quest/` at the
   repo root (the exact DLL names are the `<Reference>` list in the csproj),
   or write a gitignored `quest/Quest.Local.props`:
   ```xml
   <Project><PropertyGroup>
     <QuestReferenceDir>C:\MyQuestReferences</QuestReferenceDir>
   </PropertyGroup></Project>
   ```
3. Output: `quest/bin/Release/ATTBuilding.dll` -> Quest `Mods` folder.
   Remove any older ATTBuilding DLL so it isn't loaded twice.

## PC (Mono) -- `pchost/ATTBuilding.csproj`

1. Open in Visual Studio (legacy .NET Framework project, like ArmorUpdate's
   pchost project) with the PC game's `Managed` folder + MelonLoader on the
   reference search path (or DLLs copied under `refs/pc/`).
2. Build Release. Output `pchost/bin/Release/ATTBuilding.dll` -> the game's
   `Mods` folder, **and** onto any server you want client spawn requests to
   work on (it registers the `build` Alta.Console module there).

## Verify before you ship

```bash
python3 tests/test_build_logic.py   # parity + snap-maths contracts, no game needed
```

## Seams most likely to move in a game update

Each is isolated in one file per platform, deliberately:

- `ConsoleHook.cs` -- `RuntimeConsole.OnInputSubmitted`, `InputField.InputText`,
  `outputBuffer`/`CopyBufferToOutput` (ShardClient pattern).
- `Bubble.cs` -- `QuickAccessMenuController.quickAccessMenus`,
  `QuickAccessMenuAction.Run/Open/IsIgnored` (ArmorUpdate EquipmentMenu pattern).
- `Spawner.cs` -- `PrefabManager.AllPrefabs`, `SpawnHelper.Spawn`,
  `NetworkSceneManager.IsServer/IsLocalTest`, `CommandSync.RouteCommand`
  (server fallback). Server twin: `pchost/ServerCommands.cs`.

## Known best-effort behaviour

- Prefabs that already have a `Pickup` (most items/props) grab natively in
  build mode. A spawned prefab with **no** `Pickup` gets a runtime
  `Rigidbody`+`Pickup`; with no author-defined grab points it uses the game's
  free-grab fallback and may sit oddly in the hand -- rotation snapping still
  applies. This is logged when it happens.
- Live rotation snapping runs every frame while held; final placement is
  re-snapped (rotation + grid) and velocities zeroed on release, which is the
  part that guarantees the placed result is exactly straight.
