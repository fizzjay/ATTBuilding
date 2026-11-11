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

## Troubleshooting the three classic first-build errors

- **`'Main' is an ambiguous reference between 'ATTBuilding.Main' and
  'MelonLoader.Main'` (CS0104):** `Properties/AssemblyInfo.cs` sits in the
  global namespace and MelonLoader ships its own `Main` type. Fixed by fully
  qualifying `typeof(ATTBuilding.Main)` / `typeof(ATTBuildingQuest.Main)` and
  `MelonLoader.MelonInfo` there -- do not re-add `using MelonLoader;` to that
  file or shorten the `typeof`.
- **`MSIL vs AMD64` architecture mismatch (MSB3270, warning only):**
  MelonLoader.dll is AMD64. Both projects now set
  `<PlatformTarget>x64</PlatformTarget>` unconditionally -- do not switch a
  configuration back to AnyCPU/MSIL.
- **`The type or namespace name 'InputField' could not be found` (CS0246):**
  that type's namespace differs between the Mono and IL2CPP game assemblies.
  The console hook deliberately never names it: the Harmony prefix takes the
  argument as `object field` (injected by parameter name) and reads
  `InputText`/`SetText` by reflection. Do not "fix" this by re-adding a
  using for it in one project only.

- **`'RuntimeConsole' does not contain a definition for 'outputBuffer' /
  'outputField' / 'CopyBufferToOutput' / 'logFilter'`, and
  `'GameObject'/'Pickup' does not contain a definition for 'WasCollected'`:**
  those members only exist on the IL2CPP interop surface -- Il2CppInterop
  exposes the game's private fields as public properties, and every interop
  object carries `WasCollected`. On Mono the fields are private and Unity
  objects have no `WasCollected`. Fixed structurally: PC uses Unity's
  `== null` destroyed-object check, and *both* builds now reach the console's
  buffer/field/log-filter purely by reflection (`GetMember`/`SetMember`/
  `Call` in `ConsoleHook.cs`), which works against private Mono fields and
  interop properties alike. Do not reintroduce direct member access copied
  from IL2CPP decompilations into `pchost/`.
- **`'Main' is an ambiguous reference...` (again):** the entry class has been
  renamed `ATTBuildingMod` on both platforms, so no file can collide with
  `MelonLoader.Main` again. If you still see CS0104, you have a stale
  duplicate `AssemblyInfo.cs` (e.g. in `obj/` or a second Properties folder)
  referencing `Main` -- delete it and rebuild.
- **Architecture warning persisting:** the PC project now also carries
  `x64` groups for Visual Studio's `Any CPU` (with a space) platform name.
  If MSB3270 still shows, do a Clean + Rebuild (an up-to-date MSIL DLL in
  `bin/` can make the warning look current) and check Configuration Manager
  has no per-project platform override.
