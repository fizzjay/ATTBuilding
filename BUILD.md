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
- `Builder.cs` + the prebuilt `TabletUILib` mod DLL -- the Social Tablet
  pages, the wrist-wheel bubble injection, and (in `Builder.TriggerInput`)
  the XR trigger, read purely by reflection so no project needs a
  `UnityEngine.XRModule` reference. Both projects **reference** the DLL
  (Quest: `$(QuestReferenceDir)\TabletUILib.dll`, i.e. drop it in
  `refs/quest`; PC: plain `TabletUILib` reference like the others), and
  the DLL must **also be in the game's `Mods` folder**: its own mod
  entrypoint does the Harmony patching and per-frame ticking. The DLLs
  are named `TabletUILib-PC.dll` / `TabletUILib-Quest.dll`; the PC one is
  built against **.NET Framework 4.8**, so the PC project targets v4.8 --
  on a lower target MSBuild drops the reference with warning MSB3274 and
  every `TabletUILib`/`Page` type then errors CS0246. Our code
  only uses the DLL's *public* API (`AddBubble`/`AddApp`, `IsShowing`,
  `Refresh`, `Close`, `Page`, `TabletBubble`) -- `TabletUI.Patch`,
  `TabletUI.Tick`, `TabletWheel` and `ServerMods.Start/Tick` are internal
  to the DLL, so calling them from here is a compile error (CS0117 /
  CS0122) by design. The uploaded sources under `tabletquest/` /
  `tabletpc/` are kept in the repo for reference only and are not
  compiled into either project.

## Build mode (0.5.0): Prefabulizer tablet, no grabbing

The 0.1-0.4 hand-grab pipeline is deleted. Build mode now works like this:

- Wrist wheel -> **Prefabulizer** bubble summons the game's Social Tablet
  with the Prefabulizer pages (built on the provided TabletUILib). Opening
  it turns build mode on; `build mode on` in the console does the same and
  arms the finger ray immediately.
- **Finger ray** (tablet toggle): a ray leaves the pointer fingertip (or
  hand origin when the rig has no fingertip bone). Hovering one of *our*
  spawned prefabs lights up its **actual meshes** amber (cloned materials
  with an emissive pulse -- never a bounding box); pulling the trigger
  selects it (steady green) and the tablet's **Move / Rotate / Scale**
  pages drive it. Point + trigger another piece to switch. On rigs where
  the XR trigger can't be resolved, holding the ray on a piece for ~1.25 s
  selects it instead (dwell-select; logged once).
- The selected piece's rigidbodies are made kinematic while controlled, so
  a build piece can never fall through the world mid-edit (there is a
  "Frozen" toggle per control page to hand physics back).
- Snapping is unchanged maths (`shared/SnapMath.cs`): with snapping on,
  tablet moves land on the position grid and rotations quantise to the
  snap angle, so pieces stay straight.

## 0.5.4 notes

- Hover lag round 3. The hit path is now pure float math and nothing
  else: targeting is `Bounds.IntersectRay` against cached per-piece
  AABBs (the `Collider.Raycast` mesh confirm is gone too), probes
  reconcile incrementally (new pieces only; moved pieces slide their
  cached box instead of rebuilding), and probe sync also self-heals
  every 5 s.
- The cloned-material mesh glow is DELETED. Cloning every material on
  a prefab (shader-variant compiles) and swapping `sharedMaterials`
  arrays on the Quest GPU was a prime suspect; the highlight is now
  one reusable wire-box LineRenderer around the piece's bounds,
  rewritten only when the target/box changes.
- No file IO on hover change (hover logging was a synchronous
  open/write/close on flash, fired by fingertip-ray flicker), and the
  reflection XR trigger poll now counts failures and disables itself
  after 3 (dwell-select takes over) instead of throwing across the
  IL2CPP boundary forever. Trigger types resolve once at startup.
- The ray tick is 10 Hz and timed: debug.log gains a
  `builder perf: raytick avg X ms, max Y ms ...` line every 15 s
  while build mode is on. If hover ever lags again, that line shows
  whether our tick is the cost or the cost is elsewhere.

## 0.5.2 notes

- Ray targeting no longer uses world physics at all. A `Physics.Raycast`
  crosses the broadphase of everything near the ray (in a forested town
  that is thousands of colliders), which is why hovering a prefab still
  tanked frames in 0.5.1. Each tracked piece now has a cached probe
  (world AABB + collider list): the 20 Hz tick does float math per box,
  then confirms with `Collider.Raycast` on the winning piece only.
  Highlight materials are likewise built once per piece and cached
  (hover changes just reassign arrays), the emissive pulse is deleted,
  and the trigger poll reads only the ray hand's XR device.
- New UI (test): Settings toggle `NewUi`. The control home becomes a
  D-pad (FORWARD / LEFT / CLONE / RIGHT / BACK + RAISE / LOWER) with a
  MORE page for rotate/scale/pieces. The tablet renders text rows, so
  the quarter-circle pad is evoked by layout, not drawn.
- Dual tablets: ATT gives each player exactly one Social Tablet item
  (see `SocialTablet : ActionItem` -- one entity, one `Window`), and
  TabletUILib binds its single view to the first local tablet it finds,
  so two physical tablets cannot both run our UI. Instead the Control
  and Settings tablets cross-open from each other's first row, share
  all state, and never reset each other; the physical tablet also
  hovers in front of you (`HoverPickup`) while you work the ray.

## 0.5.1 notes

- Ray cost: the raycast + trigger poll run at 20 Hz from the chosen hand
  (right by default; Settings tablet can flip it, resize the ray, and set
  move/rotate/scale amounts). The line + impact dot still update every
  frame from the cached hit, so aiming feels instant. The 0.5.0 lag when
  pointing at a prefab was the hover-only per-frame work (reflective
  trigger polls + material pulsing at full frame rate) -- both are gone
  from the frame loop.
- Two tablets: **Prefabulizer** (control) and **Prefab Settings** (wheel
  APPS bubble). ATT has one physical Social Tablet, so they share its
  screen as separate home stacks -- cross-linked, shared state, nothing
  resets when switching.
- Build memory: spawns are recorded in `spawned-ledger.txt` (network
  entity id + prefab hash + transform). `Spawner.TickLedger` re-adopts
  remembered pieces as they stream in after a rejoin (hash-checked, so a
  different server reusing an entity id can't hand you someone else's
  piece), restoring the recorded placement if it drifted. Despawns and
  `build clear` forget their records; the ledger caps at 400 pieces.

## Known best-effort behaviour

- The finger ray uses the local hands' `Interactor` transforms; direction
  is the hand's forward. If a fingertip bone (`*index*tip*`, `*pointer*`)
  exists it is used as the ray origin, otherwise the hand origin is.
- Only prefabs this mod spawned (console or server-routed) can be
  selected; the ray ignores everything else, including world items.
- The console panel is static where it is summoned (in front of you): the
  old grab ball / grab-to-move console went away with the rest of grabbing.

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

## 0.4.0 (second round of live testing)

- **Server spawn actually registered:** `[Module]` classes are not
  auto-discovered -- ArmorRestored calls
  `CommandService.CommandCollection.Collect(assembly)` from a
  `NetworkScene.FirstInitialize` server seam. We never did, so routed
  client spawns hit an unknown server command and only ever timed out.
  The PC build now registers identically (`pchost/ServerSeam.cs`); the
  server log prints `console commands registered: build`, and its
  `debug.log` logs every routed request. (The server must run the **PC**
  build, updated to 0.4.0+, for this to exist.)
- **Console no longer opens with the menu:** `Open` on a quick-access
  action is the menu-open lifecycle (called on the remembered action each
  time the wheel opens), not "user picked this". Our clone's `Open` is now
  suppress-only (no dummy, no console); `Run` -- the actual pick -- is the
  toggle, and polled activation stands down when `Run` is patched.
- **Movable console, take 2:** a runtime-added `Pickup` has empty
  editor-injected fields (`rigidbody`, `childrenColliders`), and the empty
  rigidbody field makes `Pickup.CheckInteractAllowedAndReplace` refuse
  every grab. Both are now filled by reflection, with log lines at each step.
- **Keyboard reset after each command:** the instant refocus raced the
  keyboard closing; it's now a delayed (0.35 s) panel off/on cycle plus
  input activation, processed from the per-frame tick.

## 0.4.1 red bubble (from the game's own QuickAccessMenuAction source)

`QuickAccessMenuBubble.Run()` refuses an action whose `IsValid` is false
(that's the red tint), and the EquipmentMode clone's native validity is
tied to the equipment session we suppress. 0.4.0's `Run` patch therefore
never fired. 0.4.1 patches the concrete `IsValid` getter with a postfix
that returns true **only for our clone's instance**, keeps `Open`
suppress-only and `Run` as the toggle, and only patches methods the
concrete `EquipmentModeQuickAccess` itself declares (`DeclaringType`
check, logged). The clone's serialized `displayName` field is also set to
"Building" (the property is get-only).

## 0.4.2 console stays put unless grabbed

The movable console previously had a dynamic, gravity-free `Rigidbody`.
Any touch or collision could give it velocity, and with no gravity or
meaningful drag it simply kept flying. It is now locked as a kinematic,
`FreezeAll` body with its position and rotation remembered. Movement is
unlocked only while a local player's hand is actually interacting with
the console's `Pickup`; on release both velocities are cleared and it
locks again at the release pose. Merely touching, bumping, or clicking
the console cannot move it.

## 0.4.3 spawn timeout: log every hop, register only as server, `build ping`

Diagnosis from the 16:44 log: the client resolves the prefab and routes
`build spawn ...`, then no callback ever arrives. Three weaknesses fixed:

- **Registration timing:** the PC build used to `Collect` at melon-init
  and latch `_registered`. If the command collection is (re)created when
  the server scene loads, that early Collect is discarded and the latch
  blocked the real one. Registration now happens ONLY once this process
  is a server: the `FirstInitialize` seam (priority 800, logged), with a
  5 s server-state poll in `OnUpdate` as fallback. After Collect, the
  server dumps the `CommandCollection` contents to its `debug.log`
  (search `BUILD/SPAWN?` / "ZERO build commands").
- **Server handler always replies:** `build spawn`/`build ping` are
  wrapped end-to-end (ArmorRestored's `Run` pattern); entry, player match
  (username OR numeric identifier; on a miss it lists who the server
  sees), placement, and the `SpawnHelper` result all log, and exceptions
  return an error string instead of vanishing into the client's timeout.
- **Client side:** sends now log the resolved hash, authority flags
  (`IsServer`/`IsLocalTest`), the local player token, `CommandSync`
  state, callback arrivals, and the exact pending command on timeout.

Field test: type **`build ping`** in the console. A server running this
build answers with its version and whether your player matched. If ping
times out too, the server either has no ATTBuilding PC build loaded
(check its Melon log for `ATTBuilding 0.4.3 started (PC Mono)`) or the
module never registered (its `debug.log` lacks `server: registering
'build' module` / shows the "ZERO build commands" dump line).

## 0.4.4 direct channel (the lever was the clue)

The game's own `RuntimeConsole` source shows `RunCommandOnServer` is just
`CommandSync.RouteCommand(command, null)` -- the **dev console's** routing,
which the server only honours for developer/admin-flagged players. The
0.4.3 logs proved the consequence: server module registered, client routed,
handler never fired, no reply. (The console's Run-on-Client/Server lever is
therefore irrelevant to this mod -- our prefix intercepts input first; leave
it on Client.)

Spawn/ping now ride ATTBuilding's own message-139 wire, modelled on
ArmorRestored's message-137 channel: the client captures its server
connection at `JoinServerPipeline.ConnectionApprovedByServer`, the server
installs a handler at `ServerHandler.ConnectionCreated`, identifies the
requester from `connection.Player` (no roles, no name matching), spawns via
the shared `ServerCommands.SpawnForPlayer`, and replies on the same message.
The Alta route remains as a logged fallback. Watch for `buildnet:` lines on
both ends; a server without the new PC build simply never answers message
139, which the client timeout now says explicitly.

## 0.4.5 invisible spawns + nicknames

Spawning worked end-to-end in 0.4.4 (server SUCCESS, client replies) but
the items weren't visible in game. Against the game's own
`SpawnCommandModule`, our spawn missed two things: the pivot was placed
exactly on the surface point (prefabs whose pivot isn't at their base
spawn buried), and the native path's `entity.Chunk =
LocationChunkHelper.GetBestContaining(position)` assignment after
`SpawnHelper.Spawn`. Both are now done (server + local-authority paths),
with the chunk result logged.

Nicknames: `build nick <player name|id> <nickname>` saves
`nickname=identifier|username` to the server's
`UserData/ATTBuilding/nicks.txt` (survives restarts, hand-editable);
`build del nick <nickname>` removes one. Saved nicknames resolve anywhere
a player token is accepted (spawn routing, `build nick` itself), and all
changes ride the direct channel and are logged server-side.

## 0.4.6 prefab nicknames (corrected)

0.4.5's nicknames targeted players; the actual ask is prefab shortcuts:
`build nick anvil anv`, then `spawn anv`. Reworked accordingly -- the
server-side player-nickname registry and its BuildNet kinds are removed.
Nicknames are personal and client-side: `shared/PrefabNicks.cs` saves
`nickname=hash|Prefab Name` to your own `UserData/ATTBuilding/nicks.txt`,
and `Spawner.Resolve` consults them after exact prefab names/IDs but
before substring matching (so a nickname beats ambiguous partials). The
prefab name may be multi-word (`build nick weapon rack wall rack`); the
nickname is always the last word, one token, never a pure number, and may
not shadow a real prefab name. `build nicks` lists them. The 0.4.5
chunk-assignment fix for invisible spawns is unchanged.

## 0.4.7 remote tracking, real grabbing, console grab ball

Three field fixes. (1) 'build clear' said '0 spawned': the client never
tracked server-spawned objects -- Track() only ran in the spawning
process. The server now returns each spawned entity's network Identifier
in the BuildNet reply; the client adopts those entities as they stream
in (Spawner.TickRemoteTracking), so spawned/clear/build mode see them.
'build clear' on a remote server now sends a despawn request, and the
server only destroys entities that same player asked it to spawn.
(2) 'Cannot pick up the anvils': build-mode EnsureGrabbable added a bare
Pickup with empty [GetComponent] fields (CheckInteractAllowed refuses
those) and no grab points. It now fills rigidbody/childrenColliders/
networkRigidbody by reflection and, when a pickup has no grab points,
installs one native surface GrabPoint (bounds centre/extents,
angle ignored -- the ArmorSurfaceGrip idea without Harmony). Applied
when remote spawns are adopted and on 'build mode on'.
(3) The console gets an orange grab ball beside the panel: a child
sphere collider on the console's own Pickup, so grabbing it moves the
console through the existing lock/unlock path.

## 0.4.8 IL2CPP grab-array fix

0.4.7 field results: remote tracking, server despawn ('Removed N of your
spawned object(s)') and the console grab ball all work. Grabbing still
failed with 'grabbable fixup FAILED: Object must implement
IConvertible': on IL2CPP, Pickup.grabPoints is an
Il2CppReferenceArray<GrabPoint>, so the managed GrabPoint[] built for
Mono fell into Convert.ChangeType and threw. SetMember now tries a
direct reflection set before converting; Quest constructs the native
array type; every fixup step (rigidbody / childrenColliders /
grabPoints / ...) logs its own failure so the next log names the exact
step if one still refuses.

## 0.4.9 grab-pipeline diagnostics

Field test of 0.4.8 proved the grab fixup now succeeds end to end
(`surface grab point on 'Anvil(Clone)'`, `pickup ADDED, grabPoints=1`,
no FAILED lines, remote adoption working) yet the anvil still cannot be
grabbed — so the refusal is inside the game's grab pipeline, downstream
of our fixup. 0.4.9 instruments it instead of guessing:

- Log-only Harmony postfixes on `Pickup.AssessScore`,
  `Pickup.CheckInteractAllowedAndReplace` and `Pickup.Interact`,
  restricted to tracked (console-spawned) objects and rate-limited per
  object. The next field log will contain `grabdiag:` lines naming the
  exact check that refuses (or show that no check ever runs, meaning
  the hand never considers the object at all).
- Two known runtime-`AddComponent` gaps closed in `EnsureGrabbable`:
  `Initialize()` is now called on pickups we add (a network behaviour
  only joins its entity there; entity setup ran long before us), and
  the `Colliders` set is filled by hand (`Pickup.Awake` copies
  `childrenColliders` into it, but Awake ran at `AddComponent`, before
  the reflection fixup filled that field).
- The `grabbable ...` log line now also dumps `entityLinked`,
  `rigidbodyField`, `collidersSet`, `networkRigidbody` and `enabled`
  so one field log answers most follow-up questions.

## 0.4.10 crash fix: never Harmony-patch Pickup methods

0.4.9 crashed Quest ~5 s after launch:
`InvalidProgramException: Invalid IL code in DMD<>... AssessScore
(intptr,intptr,single&,Il2CppMethodInfo*)`. The diagnostic postfixes
patched `Pickup.AssessScore`, which takes a `ref float` parameter;
Harmony's IL2CPP wrapper for ref-parameter methods emits invalid IL
and detonates on the method's first call. All three Pickup patches
(`AssessScore`, `CheckInteractAllowedAndReplace`, `Interact`) are
removed. Grab diagnostics are now polling-only: `BuildMode.Tick`
watches each tracked pickup's `Interactors.Count`, `IsDocked`,
rigidbody kinematic flag, entity link and nearest-local-hand distance,
logging `grabdiag:` lines on every state transition. Interpretation:
hand distance dropping under ~0.3 with zero interactors/docked change
means the hand's candidate scan never selects the object; an
interactors flip with no hold means `Interact` itself refuses. The
0.4.9 runtime-Pickup fixes (`Initialize()`, filling `Colliders`) are
unchanged.

## 0.4.11 link + enable runtime Pickups before Initialize

The 0.4.10 field log (no crash -- the polling replacement works)
showed the anvil's runtime-added Pickup starting `enabled=False,
entityLinked=False`, and `Initialize()` NRE-ing inside `Pickup.Awake`
at `add_Undocked`. A disabled Interactable is invisible to the hand's
candidate scan, and `Interact()` dereferences `Entity`, so the grab
never stood a chance. The game's spawn pipeline links behaviours to
their NetworkEntity before Awake; 0.4.11 copies that order for added
pickups: find the object's NetworkEntity and reflect it into the
Pickup's Entity member, force `enabled = true`, then call
`Initialize()`, then re-enable if Initialize disabled it again. The
grabbable dump line also reports `hasNetEntity=`. Still open whether
the missing NetworkRigidbody matters for carrying the object.

## 0.4.12 entity-link by hierarchy walk + read-back

0.4.11's entity link reported `attempted=True, entityLinked=False`:
`ConsoleHook.SetMember` silently no-ops when a member is missing and
does not walk base types for non-public members, so the name guesses
never touched the real storage (`Entity` lives on the network
behaviour base class). 0.4.12 walks Pickup's whole type hierarchy,
tries every field/property whose type is `NetworkEntity`, and verifies
each attempt by reading `pickup.Entity` back. If none sticks, it dumps
every candidate member name (`member?`/`entity member?` log lines)
once per session so the registration path can be read from the log.
Also hardened the grab poller: each property is read through a guard
(`Safe(...)`), because `get_IsDocked` NREs on a Pickup whose Awake
never completed and one bad getter was blinding the whole poll.
`Initialize()` still NREs in `Pickup.Awake` (`add_Undocked`) while the
entity is unlinked -- expected to clear once the link sticks.

## 0.4.13 replicate Awake instead of running it

0.4.12 cracked the entity link (`entity linked via property 'entity'
(NetworkEntityBehaviour)`, entityLinked=True, enabled=True) and the
poller watched a live grab attempt: handDist fell to 0.50 while
interactors stayed 0 and `docked=?` -- `get_IsDocked` still NREs,
because Pickup.Awake never completed (Initialize dies in it at
add_Undocked, on something other than Entity) so the collections
Awake creates don't exist. Since the 'entity' setter already
registers the behaviour, 0.4.13 stops calling Initialize() when
linked and replicates Awake's field setup: every null array/
collection field on the Pickup is hydrated with an empty instance,
remaining null fields are dumped by name (`still null:` lines), and
the poller logs a one-shot `grabdiag ref:` snapshot of the nearest
real game-spawned Pickup as ground truth for comparison.

## 0.4.14 whole-mesh grabbing + property-aware hydration

0.4.13's field test: the entity link and skip-Initialize hold
(`entityLinked=True, grabPoints=1, collidersSet=6, enabled=True`), the
reference Pickup comparison shows a working Pickup reads
`docked=False` while ours still throws (`docked=?`), and `interactors`
stays 0 even with the hand at 0.25 m. Two blind spots found: the
hydration logged NOTHING (on IL2CPP the interop wrappers expose native
fields as properties, so the field-only walk saw an empty class), and
grab coverage was a single surface point.

0.4.14 therefore:
- Hydration enumerates fields AND properties over the full Pickup
  hierarchy (`HydratePickupGraph`), fills null arrays/collections/
  parameterless classes with empties, and `DumpNullMembers` lists what
  is still null by name (`still null:` lines).
- Whole-mesh grabbing per user request ("grab the whole mesh not just
  one point"): `InstallSurfaceGrabPoints` covers the collider bounds
  with 19 surface GrabPoints (centre, 6 face centres, 12 edge centres;
  surface rotation mode, angle checks ignored), and each point gets its
  owner back-reference to the Pickup via a hierarchy-walking set
  (`GrabPoint owner set via '...'` log) -- Pickup.Awake normally wires
  that; we set the array directly, so the points plausibly pointed at
  nothing and CheckScore failed/threw silently.
- One-shot `DumpGrabSignatures` writes the exact `sig:`/`prop:` shapes
  of the grab pipeline (Pickup/Interactable/GrabPoint methods incl.
  ref/out params, to the debug file only), so the game's own scoring
  can be invoked reflectively next if still needed. Harmony patching of
  ref-parameter methods remains forbidden (0.4.9 crash).

Field-check in the next log: `hydrated N empty Pickup members (...)`,
any remaining `still null:` names, `GrabPoint owner set via '...'`,
`whole-mesh grab ... 19 surface grab points`, and whether
`grabdiag:` flips `interactors`/`docked` when the hand touches the
mesh anywhere.

## 0.4.15 wire GrabPoint transform, retry Awake, probe the game's scoring

0.4.14 field results: property-aware hydration works (7 members filled:
WedgeJoints, docked/undocked events, breakEvent, bestGrab, docked
memories), 19 points installed, but still `interactors=0`,
`docked=?`. The signature dump changed the picture:

- `GrabPoint` has NO owner back-reference member -- that theory is
  dead. But editor-authored points carry a serialized `transform`
  member that code-created points leave null. 0.4.15 wires it
  (`WireGrabPoint`: transform + isUsingLocal + point hydration).
- The `add_Undocked` NRE that killed Awake was Awake touching the
  `docked`/`undocked` event fields -- exactly what hydration now
  pre-creates BEFORE the init step. So 0.4.15 retries the real
  `Initialize()` after hydration + entity link: genuine Awake state
  (docker, dockingChecker, pickupScaler -- still null, only Awake
  makes them) beats replicating it member by member.
- Instead of guessing further, the poller now RUNS the game's own
  pipeline reflectively when a hand is within 0.8 m: `grabprobe:`
  lines report `CheckInteractAllowedAndReplace` and
  `AssessScore(interactor, out score)` verdicts (reflective Invoke
  handles out params; Harmony on them remains forbidden -- 0.4.9).
  The same probe runs against the reference real Pickup (`ref`) to
  validate the harness, and `refnull:` dumps the real Pickup's null
  members so our `still null:` list can be diffed against ground
  truth. Poller state gains `canInteract`/`netwk`/`chan` (read
  reflectively; compile-safe on Mono).

Field-check: `Initialize() RAN` vs `still FAILED`, diff of
`still null post-init:` vs `refnull:`, ours-vs-ref `grabprobe:`
verdicts at close hand range, and whether `docked=` finally reads
False.

## 0.4.16 learn from a native item; stop inventing bestGrab; freeze pieces

0.4.15 + user ground truth split the problem decisively:

- `Initialize() RAN`, post-init nulls match a real Pickup, and the
  game's own scoring invoked directly says yes to everything
  (`allowed=True assess=True`). Yet the user could grab ONLY the
  Stick -- the one object whose Pickup shipped in its prefab. So our
  added Pickups fail at candidate DISCOVERY (the hand never presents
  them), and station prefabs (Anvil/Map Board/Trade Deck) presumably
  don't present item-style colliders to the hand's scan.
- Our points score ~827k == the hand's squared distance from the
  world ORIGIN: the winning point evaluates at (0,0,0). Prime
  suspect: hydration invented `bestGrab` as an empty GrabPoint
  (native Pickups keep it null until scored -- see `refnull:`).
  0.4.16 stops hydrating `bestGrab`/`BestGrab`/`dockedMemory*`, and
  sets GrabPoint members by direct assignment (`gp.position = ...`;
  fields on Mono, auto-props on IL2CPP) instead of the reflected
  setter path that silently no-ops on a miss.
- New: `DumpObjectProfile` (ours vs native: root/collider layers,
  trigger flags, rigidbody, every GrabPoint member value) and
  `CaptureNativeTemplate` -- the first tracked prefab-native Pickup
  donates its collider layers + GrabPoint design values, which are
  then applied to our pieces, retroactively (layers re-set, surface
  points rebuilt from the template) when the template arrives late.
- `DumpInteractorMembers` (debug file, `imember:`) lists where an
  Interactor keeps its state, guiding a candidate-list probe next.
- A dynamic Map Board fell through the world: `FreezeBody` makes
  pieces we lend a Pickup to kinematic (the console has been
  grabbable-while-kinematic since 0.4.2).
- Poller state no longer includes handDist (the 0.4.15 log flood).

Field-check: `profile ours:` vs `profile native:` (layers + gp0
members), `native grab template captured`, whether `grabprobe:`
scores drop from ~827k to single digits, and whether the Anvil
finally grabs. If it still doesn't, the `imember:` list says where
the hand's candidate list lives for a direct read next round.

## 0.4.17 PC build fix: no direct GrabPoint member assignment

0.4.16 assigned `gp.position` / `gp.poleForward` / `gp.isIgnoringAngle`
directly; the Mono (PC) `GrabPoint` has no members under those names
(the IL2CPP interop surface differs -- the reason this codebase routes
member writes through reflection everywhere). Those three writes now
go through `TrySetMemberWalk` (hierarchy-walking, type-checked) like
everything else. `gp.rotationMode` stays direct: it exists on both
targets and the compiler did not flag it. The MSIL/AMD64 MelonLoader
architecture warning is the known-harmless MSB3270; ignore it.
Everything else from 0.4.16 (native template, profiles, freeze,
bestGrab hydration ban) is unchanged -- see that section for the
field-check list.

## 0.4.18 match native exactly, read the hand's scan, backup grab

0.4.16 field results split the problem further:

- The native template WAS captured and applied (layers, GrabPoint
  design values), and pieces we add are frozen kinematic -- yet our
  Anvil still scored ~827k while the Stick scored ~0.6. So the
  template copy was not the whole story.
- Diffing our gp0 against the native gp0 after the copy left exactly
  the fields we IMPOSE for whole-mesh grabbing: `transform` wired
  (native: null), `rotationMode=Surface` (native: Pole),
  `isIgnoringAngle=True` (native: False), `position` at our surface
  layout (native: its authored spot).

0.4.18 attacks on three fronts:

- **Match native exactly.** When a template exists, GrabPoint
  `transform` is now CLEARED (`TryClearMemberWalk`) instead of wired:
  native points carry null and work in Pickup-local space. Trigger
  colliders (e.g. an Anvil 'Detection Area') are no longer re-layered
  onto the grab layer -- only solid colliders are.
- **Interrogate, don't guess.** Every probe now also logs `handscan:` --
  the hand Interactor's own `collisionMask`/`collisionLayer`/`radius`/
  `length`, how many candidates sit in `_OverlapInteractables`, and
  whether our piece is among them. That splits "never discovered" from
  "discovered but scored out". And each probe of our piece rebuilds its
  GrabPoints as the next A/B/C variant -- A: whole-mesh Surface,
  B: a single pure native clone, C: whole-mesh positions with native
  Pole semantics -- then settles back on A, so one session shows which
  imposed value (if any) is the poison.
- **Backup grab ("grab legit everything").** In build mode, hold a
  free hand on any tracked spawned piece for ~0.7s and the mod calls
  the game's own `Interact(hand)` / `GrabWith(hand)` reflectively
  (polling + Invoke only -- still no Harmony on Pickup). If the game
  takes it, its own grip release, snapping and Place behave exactly
  like a normal item grab. If even that no-ops, manual carry engages:
  the piece follows your hand (kinematic) until you hold your hand
  still for ~1s, which places it back down frozen. Watch for
  `backupgrab:` lines to see which path each grab took.

Field-check: `handscan:` (does `containsTarget` ever read True for our
pieces?), `grabprobe:` score per `variant=`, and whether grabbing now
works natively; if not, whether dwell-grab / manual carry gives a
usable way to move Anvils and Map Boards.

## 0.4.19 backup grab proven; template from any native Pickup

The 0.4.18 field session answered the big questions:

- **The backup grab works.** Dwell-grab called the game's own
  `Interact(hand)`, which registered an interactor immediately; the
  hand only reported `IsInteracting` ~2.3 s later (network
  confirmation) and the native hold then took over. When it didn't,
  manual carry picked the Anvil up and put it down cleanly, twice.
- **Discovery is still dead for our pieces:** `handscan:` showed
  `candidates=0 containsTarget=False` throughout -- the hand's own
  scan never lists a runtime-added Pickup.
- **The A/B/C isolation silently never ran:** the user spawned only
  Anvils that session, and the native template was only captured from
  console-spawned items, so points were built the pre-template way
  and the score stayed ~832k (~912 squared: the winning point still
  evaluates at the world origin).
- New suspect for the origin score: `grabPointsScale`, a float the
  prefab sets that a runtime-added Pickup leaves at the C# default.
  Never compared ours-vs-native until now.

0.4.19 changes:

- The native template is now captured from **any** real Pickup the
  poller finds in the scene (except the console's own 'Grab' handle),
  so retrofit no longer depends on spawning a Stick first; the A/B/C
  variant cycle will now actually run.
- The template also captures the Pickup's `grabPointsScale` and
  applies it to our pieces; profiles log `gpScale=`/`tight=`/`joint=`
  for ours-vs-native comparison.
- Each probe now dumps the winning point (`bestgrab:` -- position,
  LastPosition, mode, transform, scale) so we can see exactly which
  GrabPoint the game picked and where it evaluated.
- Backup grab now waits before manual carry: 3 s when `Interact()`
  registered an interactor (the game took the Anvil natively inside
  that window in 0.4.18), 0.75 s otherwise -- no more fighting the
  game's incoming hold.
- Handscan also reads the backing-field candidate list and the raw
  physics `overlap` collider count.

Field-check: do `whole-mesh grab ... variant=B-single-native` /
`C-mesh-pole` probe scores drop below 832k, what does `profile ours:`
say for `gpScale=` vs native, what does `bestgrab:` show, and does
the Anvil now grab natively (or via dwell) without a manual-carry
fight?
