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

- **`The name 'SetMember' does not exist in the current context`:** the
  reflection helpers live on `ConsoleHook`, but `ConsoleOpener` (a separate
  class in the same file) calls one. They are now `internal static` and the
  cross-class call is qualified (`ConsoleHook.SetMember(...)`). If you copy
  these classes into separate files, keep that qualification.
- **Architecture warning, round 3:** a repo-root `Directory.Build.props` now
  also forces `x64`, so the setting applies however the projects are built.
  Know that MSB3270 is a *warning only* here: MelonLoader loads mod DLLs as
  managed IL into the (x64/ARM64) game process, and an AnyCPU/MSIL build
  still runs -- the x64 target just silences the mismatch properly.

- **`'IEnumerator<NetworkPrefab>' does not contain a definition for
  'MoveNext'` / foreach errors on Quest:** Il2CppSystem collections don't
  satisfy C#'s foreach pattern (that's why ArmorUpdate ships
  `NativeEnumerable`). Quest loops over game collections only through
  `NativeSnapshot.Items(...)` in `quest/NativeSnapshot.cs`. The same foreach
  is fine in `pchost/` -- do not "unify" them by copying the PC line back.
- **`System.Action<string,string> cannot be used as type parameter
  'TIl2Cpp' in ConvertDelegate<TIl2Cpp>`:** this Il2CppInterop version wants
  the IL2CPP delegate type: `ConvertDelegate<Il2CppSystem.Action<string,
  string>>(...)`. (The reference AdminConsole wrote `Action<...>` because its
  file had `using Il2CppSystem;`.)
- **`SERVER_IATTBuildingQuestTALL could not be found` / `_running is never
  assigned`:** a generator placeholder token that survived into
  `quest/Main.cs`; removed. If you ever see a `...TALL` identifier again it
  is leftover template text, not a type to reference.

## If the game hard-closes (no error in the Melon log)

A native crash leaves no managed exception -- the log just stops. The Quest
build writes its last step to `UserData/ATTBuilding/crumb.txt` before each
bubble/console milestone. Reproduce, then read that file: whatever it names
is where it died. Fixed in 0.1.1: the bubble previously cloned the *first
entry* of `quickAccessMenus`; if that entry is a scene component,
`Object.Instantiate` clones its whole GameObject (the wrist-menu rig) on
every menu rebuild. It now clones only a ScriptableObject action asset
(the native Equipment Mode action, as ArmorUpdate does), reuses that one
clone, and adds nothing if no asset template is loaded. The old
`ArmorQuest.dll` in the same log uses the same safe pattern.

## 0.2.0 crash hunt (game closes, guaranteed on opening the wrist menu)

Hypothesis, from ArmorUpdate's own comment ("leave Run/Open/Close/IsValid
untouched"): our Harmony prefixes on `QuickAccessMenuAction.Run/Open` --
which the controller calls *while the menu opens* -- were the crash. 0.2.0
therefore contains **no Run/Open patches at all**. The bubble is revealed
with Armor's `IsIgnored` postfix (proven in the same log by ArmorRestored),
and activation is detected by polling our clone's bool state for an
open/active flip, then calling the action's own `Close()`.

Logging added (this is the part that ends the guessing):
- `UserData/ATTBuilding/debug.log` -- init line, a one-time dump of the real
  `QuickAccessMenuAction` method/bool-member surface on your game build,
  every bubble milestone, every action state change, and a 20 s heartbeat.
- `UserData/ATTBuilding/crumb.txt` -- last milestone reached (native crashes
  show nothing in the Melon log; these two files are the evidence).
- Preferences (editable in TavernEssentials' Mods board): `BubbleEnabled`
  and `ConsoleEnabled`. To bisect: turn `BubbleEnabled` off, restart -- if
  it still closes, turn `ConsoleEnabled` off too and tell us which pair
  reproduces it.

If 0.2.0 still closes on menu open, send `debug.log` + `crumb.txt`: the API
dump and state changes in there name the exact member/method involved.

## 0.3.0 (from live testing)

- **No duplicate equipment dummy:** our bubble is a clone of the native
  EquipmentMode action, whose own `Open` spawns the equipment dummy. A
  Harmony prefix on the *concrete* `EquipmentModeQuickAccess.Open/Run`
  (base-class patching stays banned -- that was the menu-open crash)
  diverts only our clone to the console; your real equipment bubble and
  its dummy are untouched. Pref: `SuppressDummy` (restart to change).
- **Keyboard returns after every command:** after handling a command the
  console clears and immediately re-focuses its input field
  (`ActivateInputField`/`Select` via reflection); if submitting dismissed
  the panel, it respawns at once.
- **Movable console:** the summoned console gets a gravity-free
  `Rigidbody`+`Pickup`, so you can grab and move it like an item
  (best-effort, logged in `debug.log`).
- **Spawns a tad up:** head height +0.12 m at 1 m out, instead of 0.25 m
  below the head.
