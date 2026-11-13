"""Static contract tests, ArmorUpdate-style: no game needed.

They pin (a) the snapping maths by re-deriving it from shared/SnapMath.cs
constants, (b) the PC/Quest parity contract: both projects link the same
shared files, expose the same commands, and never cross-reference the other
platform's assemblies, and (c) the 0.5.0 tablet contract: NO grabbing
anywhere -- build mode is the Prefabulizer tablet + finger ray + trigger
select over tracked spawns only, using the provided TabletUILib.
"""
import math, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parent.parent

def read(p): return (ROOT / p).read_text(encoding="utf-8")

def quantize(v, step): return round(v / step) * step if step > 0 else v
def snap_angle(d, step):
    d %= 360
    return (quantize(d, step) % 360) if step > 0 else d

def test_quantize_guarantees_straight_angles():
    for step in (15, 45, 90):
        for deg in range(0, 360):
            out = snap_angle(deg + 0.4, step)
            assert abs(out / step - round(out / step)) < 1e-9, (deg, step, out)

def test_parser_commands_exist_in_both_platforms():
    parser = read("shared/CommandParser.cs")
    for verb in ('"spawn"', '"ping"', '"nick"', '"nicks"', '"del"', '"mode"', '"snapping"', '"snapangle"', '"grid"', '"list"', '"spawned"', '"clear"', '"help"'):
        assert verb in parser, verb
    for platform in ("pchost", "quest"):
        hook = read(f"{platform}/ConsoleHook.cs")
        for kind in ("CommandKind.Spawn", "CommandKind.ServerPing", "CommandKind.Nick", "CommandKind.DelNick", "CommandKind.Nicks", "CommandKind.BuildMode", "CommandKind.Snapping",
                     "CommandKind.SnapAngle", "CommandKind.Grid", "CommandKind.Clear"):
            assert kind in hook, (platform, kind)

def test_both_projects_link_identical_shared_files():
    shared = {p.name for p in (ROOT / "shared").glob("*.cs")}
    assert shared == {"BuildState.cs", "CommandParser.cs", "PrefabNicks.cs", "SnapMath.cs", "SpawnLedger.cs", "TrackedRegistry.cs", "VectorSnap.cs"}
    for proj in ("pchost/ATTBuilding.csproj", "quest/ATTBuilding.csproj"):
        text = read(proj)
        for name in shared:
            assert f"shared\\{name}" in text, (proj, name)

def test_no_cross_platform_references():
    pc, quest = read("pchost/ATTBuilding.csproj"), read("quest/ATTBuilding.csproj")
    assert "Il2Cpp" not in pc
    assert "net8.0" in quest and "BUILD_QUEST" in quest
    for f in (ROOT / "pchost").glob("*.cs"):
        assert "Il2Cpp" not in f.read_text(encoding="utf-8"), f
    # Spawning authority + server seam exist where each belongs.
    assert "SpawnHelper.Spawn" in read("pchost/ServerCommands.cs")
    assert 'Module("build"' in read("pchost/ServerCommands.cs")
    assert "CommandSync" in read("quest/Spawner.cs") and "DelegateSupport.ConvertDelegate" in read("quest/Spawner.cs")
    assert "NetworkSceneManager.IsServer" in read("quest/Spawner.cs")

def test_no_grabbing_anywhere():
    # 0.5.0 pivot (user: "no more grabbing... it's too much rn and laggy"):
    # the entire hand-grab pipeline is deleted, not disabled.
    for platform in ("pchost", "quest"):
        assert not (ROOT / platform / "BuildMode.cs").exists(), platform
        builder = read(f"{platform}/Builder.cs")
        code_only = "\n".join(line.split("//")[0] for line in builder.splitlines())
        assert "Pickup" not in code_only, platform  # never add/fix/touch a Pickup again
        assert "PrimitiveType.Cube" not in code_only, platform  # no box highlight proxy (Bounds math for targeting is fine)
        hook = read(f"{platform}/ConsoleHook.cs")
        for banned in ("EnsureMovable", "AddGrabHandle", "FixupPickup", "TickConsoleLock", "IsConsoleGrabbed", "ATTBuildingGrabHandle"):
            assert banned not in hook, (platform, banned)
        assert "EnsureGrabbable" not in read(f"{platform}/Spawner.cs"), platform
    all_src = ""
    for f in list((ROOT / "pchost").glob("*.cs")) + list((ROOT / "quest").glob("*.cs")) + list((ROOT / "shared").glob("*.cs")):
        all_src += f.read_text(encoding="utf-8")
    for banned in ("PollBackupGrab", "PollGrabDiag", "MANUAL CARRY", "InstallSurfaceGrabPoints",
                   "HydratePickupGraph", "CaptureNativeTemplate", "handscan:", "bestgrab ",
                   "GrabPoint", "backupgrab:"):
        assert banned not in all_src, banned
    # help text must never again tell the player to grab something
    help_section = read("shared/CommandParser.cs").split("HelpLines")[1]
    assert "grabbed" not in help_section and "grab it" not in help_section and "grab the" not in help_section

def test_ray_only_selects_tracked_spawns():
    for platform in ("pchost", "quest"):
        b = read(f"{platform}/Builder.cs")
        # selection is gated on the tracked registry at every step (ancestor walk)
        assert "TrackedRegistry.Contains" in b and "TrackedRootOf" in b
        # the highlight is ONE reusable wire-box LineRenderer: no cloned
        # materials, no sharedMaterials swaps (Quest shader compiles were
        # a frame killer); the impact dot is a plain primitive
        assert "SyncOutline" in b and "SetOutlineBox" in b and "BuildLitCache" not in b
        assert b.count("readonly Color HoverColor") == 1 and b.count("readonly Color SelectedColor") == 1
        assert "sharedMaterials" not in "\n".join(l.split("//")[0] for l in b.splitlines()) and "new Material(src)" not in b
        assert "LineRenderer" in b and "ATTBuildingHitDot" in b
        # trigger is the XR trigger read reflectively (no new assembly
        # references); a dwell-select fallback covers rigs without it
        assert "UnityEngine.XR.InputDevices" in b and "triggerButton" in b and "1.25f" in b
        # perf contract (0.5.4): the hit path is pure float math. NO
        # physics call of any kind (world Physics.Raycast AND the
        # per-collider Collider.Raycast confirm are both gone), NO
        # material cloning/swapping, NO file IO on hover change, and the
        # reflection trigger poll disables itself after 3 failures.
        # Targeting = Bounds.IntersectRay over cached per-piece probes
        # at 10 Hz, timed by a Stopwatch perf report in debug.log.
        code_only = "\n".join(l.split("//")[0] for l in b.splitlines())
        assert "Physics.Raycast(" not in code_only and ".Raycast(ray" not in code_only and "IntersectRay" in b
        assert "float d = 0f;" in b and "float d;" not in b  # CS0165: out-var must be pre-initialised
        assert "_probes" in b and "SyncProbes" in b and "ProbesDirty" in b and "ProbesDirty" in read(f"{platform}/Spawner.cs")
        assert "PressedEdge(RayHand)" in b and "_failStreak" in b and "_dead" in b and "TriggerInput.Warm()" in b
        assert "builder perf" in b and "_perfWatch" in b and "builder: ray hovering" not in b
        assert "_nextRayTick" in b and "0.1f" in b and "RayHand" in b and "ClassifyHand" in b
        # selected pieces are frozen (kinematic): a build piece never falls
        # through the world mid-edit (the falling Map Board lesson)
        assert "isKinematic" in b
    assert "only these objects can be selected" in read("shared/TrackedRegistry.cs").lower()

def test_prefabulizer_tablet_integration():
    for platform in ("quest", "pchost"):
        builder = read(f"{platform}/Builder.cs")
        main = read(f"{platform}/Main.cs")
        proj = read(f"{platform}/ATTBuilding.csproj")
        assert '"Prefabulizer"' in builder and "AddBubble" in builder and ".App(" in builder
        for page in ("ControlHomePage", "SettingsHomePage", "NewUiHomePage", "MorePage", "MovePage", "RotatePage", "ScalePage", "PiecesPage", "HelpPage", "DeleteConfirmPage"):
            assert page in builder, (platform, page)
        # New UI test home: D-pad rows around a centre CLONE, persisted toggle
        assert "CLONE (copy)" in builder and "CloneSelected" in builder and '"NewUi"' in main
        # each tablet opens the other from its first rows (one physical
        # tablet, two home stacks -- switching never resets state)
        assert '"Open SETTINGS tablet"' in builder and '"Open CONTROL tablet"' in builder
        # two tablets, one shared state: control bubble + settings app
        assert "AddApp" in builder and '"Prefab Settings"' in builder
        # opening either tablet IS entering build mode (rising edge)
        assert "IsShowing(ControlHomeFunc)" in builder and "IsShowing(SettingsHomeFunc)" in builder and "SetBuildMode(true)" in builder
        # delete lives on the control home too (with a confirm page), and
        # the settings tablet owns the amounts
        assert builder.count("DELETE selected piece") >= 2
        assert "Move amount (m)" in builder and "Rotate amount (deg)" in builder and "Scale amount (x)" in builder
        assert "Ray hand" in builder and "RayLengthMetres" in main
        assert "Builder.RegisterTablet()" in main and "Builder.Tick()" in main
        # The TabletUILib MOD dll is referenced, never source-compiled, and we
        # only touch its PUBLIC api: Patch/Tick/TabletWheel/ServerMods.Start
        # are internal to the DLL (its own mod entrypoint runs them), so
        # calling them is CS0117/CS0122 against the real DLL.
        dll = "TabletUILib-PC" if platform == "pchost" else "TabletUILib-Quest"
        assert f'<Reference Include="{dll}"' in proj, platform
        assert "TabletUILib\\TabletUI.cs" not in proj and "TabletUILib\\TabletUIMod.cs" not in proj, platform
        assert "Builder.cs" in proj and "BuildMode.cs" not in proj, platform
        code = "\n".join(l.split("//")[0] for l in (main + builder).splitlines())
        for internal_api in ("TabletUI.Patch", "TabletUI.Tick", "TabletWheel", "ServerMods.Start", "ServerMods.Tick"):
            assert internal_api not in code, (platform, internal_api)
        for public_api in ("TabletUI.AddBubble", "TabletUI.IsShowing", "TabletUI.Refresh"):
            assert public_api in builder or public_api in main, (platform, public_api)
    # the deleted field stays deleted (a stale BuildMode.cs on disk is the
    # only thing that ever referenced it -- see test_no_grabbing_anywhere)
    all_src = ""
    for f in list((ROOT / "pchost").glob("*.cs")) + list((ROOT / "quest").glob("*.cs")) + list((ROOT / "shared").glob("*.cs")):
        all_src += f.read_text(encoding="utf-8")
    assert "HeldInstanceId" not in all_src

def test_tablet_drives_the_selected_piece():
    for platform in ("pchost", "quest"):
        b = read(f"{platform}/Builder.cs")
        hook = read(f"{platform}/ConsoleHook.cs")
        spawner = read(f"{platform}/Spawner.cs")
        # movement: position nudges keep pieces straight via shared snapping
        assert "VectorSnap.SnapPosition" in b and "VectorSnap.SnapRotation" in b
        assert "transform.Rotate" in b and "localScale" in b  # rotate + scale pages
        assert "FORWARD" in b and "Yaw left" in b and "Bigger" in b  # move/rotate/scale pages exist
        # console 'build mode on' goes through the same SetBuildMode door
        assert "Builder.SetBuildMode(cmd.Flag)" in hook
        # tablet can respawn the last prefab and delete the selected one
        assert "LastSpawnName" in spawner and "LastSpawnName" in b
        assert "DespawnOne" in spawner and "DespawnOne" in b

def test_bubble_is_a_new_quick_access_action_on_both():
    assert "ATT Building Console" in read("quest/Bubble.cs")
    assert "ATT Building Console" in read("pchost/Bubble.cs")
    assert "ConsoleOpener.Toggle" in read("quest/Bubble.cs") + read("pchost/Bubble.cs")
    assert "quickAccessMenus" in read("quest/Bubble.cs") and "quickAccessMenus" in read("pchost/Bubble.cs")

def test_build_memory_persists_across_sessions():
    # Pieces spawned in earlier sessions must be editable after rejoining:
    # spawns are written to a ledger keyed by network entity id, and the
    # platform Spawner re-adopts matches as they stream in.
    ledger = read("shared/SpawnLedger.cs")
    assert "spawned-ledger.txt" in ledger and "EntityId" in ledger and "Upsert" in ledger
    for platform in ("quest", "pchost"):
        proj = read(f"{platform}/ATTBuilding.csproj")
        assert "shared\\SpawnLedger.cs" in proj, platform
        spawner = read(f"{platform}/Spawner.cs")
        assert "TickLedger" in spawner and "LedgerAdopt" in spawner and "LedgerUpdate" in spawner
        assert "SpawnLedger.Remove" in spawner  # despawn/clear forget the piece
        assert "re-adopted" in spawner
        assert "SpawnLedger.Init" in read(f"{platform}/Main.cs"), platform
        builder = read(f"{platform}/Builder.cs")
        assert "Spawner.LedgerUpdate" in builder  # tablet edits refresh the record


def test_versions_aligned():
    for f in ("quest/Main.cs", "pchost/Main.cs"):
        assert 'Version = "0.5.5"' in read(f), f
    for p in ("quest/Properties/AssemblyInfo.cs", "pchost/Properties/AssemblyInfo.cs"):
        assert '"0.5.5.0"' in read(p), p


def test_reported_build_errors_stay_fixed():
    # CS0104: bare typeof(Main) in a global-namespace AssemblyInfo is
    # ambiguous with MelonLoader.Main on both platforms.
    for proj, ns in (("pchost", "ATTBuilding"), ("quest", "ATTBuildingQuest")):
        info = read(f"{proj}/Properties/AssemblyInfo.cs")
        assert not any(l.strip() == "using MelonLoader;" for l in info.splitlines())
        assert f"typeof({ns}.ATTBuildingMod)" in info and "MelonLoader.MelonInfo" in info
    # MSB3270: MelonLoader.dll is AMD64, so both projects target x64.
    for proj in ("pchost/ATTBuilding.csproj", "quest/ATTBuilding.csproj"):
        assert "<PlatformTarget>x64</PlatformTarget>" in read(proj), proj
    # CS0117/CS1061 from IL2CPP-only surface leaking into the Mono build:
    # WasCollected, and RuntimeConsole's outputBuffer/outputField/logFilter/
    # CopyBufferToOutput (private fields on Mono) must never be named directly.
    for f in list((ROOT / "pchost").glob("*.cs")) + list((ROOT / "quest").glob("*.cs")):
        assert "WasCollected" not in f.read_text(encoding="utf-8"), f
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        for bad in ("_console.outputBuffer", "_console.outputField", "_console.CopyBufferToOutput()", "rc.logFilter"):
            assert bad not in hook, (proj, bad)
        assert 'GetMember(_console, "outputBuffer")' in hook and 'SetMember(rc, "logFilter", 0)' in hook
    # Red bubble (game source): QuickAccessMenuBubble.Run() gates on the
    # action's IsValid, and our suppressed clone reports false. Force it
    # true for our instance only; only patch concrete-declared methods.
    for proj in ("pchost", "quest"):
        bubble = read(f"{proj}/Bubble.cs")
        assert "ValidPostfix" in bubble and '"IsValid"' in bubble
        assert "PatchDeclared" in bubble and "DeclaringType" in bubble
        assert '"displayName", "Building"' in bubble
    # Spawn-timeout hunt (0.4.3): every hop logs, the server handler always
    # replies (wrapped), registration happens only once this process is a
    # server (an init-time Collect can be discarded and its latch would
    # block the real one), and there is a ping probe.
    server = read("pchost/ServerCommands.cs")
    assert "server spawn: received" in server and "server ping: received" in server
    assert "SpawnHelper.Spawn returned NULL" in server and "UNHANDLED EXCEPTION" in server
    assert "DumpCommandCollection" in server and "DescribePlayers" in server
    assert "TryRegisterWhenServer" in server and "TryRegisterWhenServer" in read("pchost/Main.cs")
    assert "ServerCommands.Register();" not in read("pchost/Main.cs")  # no init-time Collect latch
    assert "HarmonyPriority(800)" in read("pchost/ServerSeam.cs")
    for proj in ("pchost", "quest"):
        spawner = read(f"{proj}/Spawner.cs")
        assert "PingServer" in spawner and "BeginWait(print, command)" in spawner
        assert "CALLBACK from server" in spawner and "RouteCommand(" in spawner
        assert "AuthorityDescription" in spawner and "LocalPlayerToken" in spawner
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "_pendingCommand" in hook and "route: TIMEOUT after 10s" in hook
    # Direct channel (0.4.4): Alta's RouteCommand is the DEV console's
    # routing (RuntimeConsole.RunCommandOnServer -> RouteCommand(command,
    # null)) and dies for players without a server role, so spawn/ping ride
    # our own message-139 wire (ArmorRestored's message-137 pattern).
    for proj in ("pchost", "quest"):
        net = read(f"{proj}/BuildNet.cs")
        assert "139" in net and "SendSpawn" in net and "SendPing" in net
        assert "ConnectionApprovedByServer" in net
        spawner = read(f"{proj}/Spawner.cs")
        assert "BuildNet.SendSpawn" in spawner and "BuildNet.SendPing" in spawner
        assert spawner.index("BuildNet.SendSpawn") < spawner.index("NetRoute.Send")  # direct first, Alta fallback
    assert "BuildNet.cs" in read("pchost/ATTBuilding.csproj") and "BuildNet.cs" in read("quest/ATTBuilding.csproj")
    assert "SpawnForPlayer" in read("pchost/ServerCommands.cs") and "BuildNet.Install" in read("quest/Main.cs")
    # Invisible-spawn fix (0.4.5): the native spawn command assigns
    # entity.Chunk after SpawnHelper.Spawn and we now match it.
    assert "LocationChunkHelper.GetBestContaining" in read("pchost/ServerCommands.cs")
    assert "LocationChunkHelper.GetBestContaining" in read("pchost/Spawner.cs")
    # Prefab nicknames (0.4.6, corrected semantics): 'build nick anvil anv'
    # then 'spawn anv'. Personal + client-side (shared PrefabNicks, saved in
    # nicks.txt on your own machine), consulted by Resolve on both platforms
    # after exact names/IDs but before substring matching. The 0.4.5
    # player-nickname server path is gone.
    nicks = read("shared/PrefabNicks.cs")
    assert "nicks.txt" in nicks and "TryGet" in nicks and "Validate" in nicks
    parser = read("shared/CommandParser.cs")
    assert "Text2 = words[words.Length - 1]" in parser  # nickname is the last word; prefab names may be multi-word
    assert "SetNickByToken" not in read("pchost/ServerCommands.cs")
    for proj in ("pchost", "quest"):
        assert "SendNickSet" not in read(f"{proj}/BuildNet.cs") and "SendNickDel" not in read(f"{proj}/BuildNet.cs")
        spawner = read(f"{proj}/Spawner.cs")
        assert "PrefabNicks.TryGet" in spawner and "PrefabNicks.Set" in spawner and "PrefabNicks.Delete" in spawner
        assert spawner.index("PrefabNicks.TryGet") < spawner.index("partials == 1")  # nick beats substring ambiguity
    # Remote-spawn tracking (0.4.7): the server returns spawned entity
    # Identifiers in the reply; the client adopts those entities as they
    # stream in (else spawned/clear saw nothing and reported '0 spawned');
    # clear asks the server to destroy only the requester's own. 0.5.0 adds
    # the single-piece DespawnOne for the tablet's DELETE button.
    assert "out uint entityId" in read("pchost/ServerCommands.cs") and "DespawnForPlayer" in read("pchost/ServerCommands.cs")
    for proj in ("pchost", "quest"):
        net = read(f"{proj}/BuildNet.cs")
        assert "SendDespawn" in net and "TrackRemote" in net
        spawner = read(f"{proj}/Spawner.cs")
        assert "TickRemoteTracking" in spawner and "_pendingRemote" in spawner
        assert "DespawnOne" in spawner and "SendDespawn(new[] { remoteId })" in spawner
    # 0.4.8: SetMember name-guessing silently no-ops; it tries a direct set
    # first (Convert.To on IL2CPP wrapper types throws IConvertible errors).
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "try { prop.SetValue(target, value, null); }" in hook and "try { field.SetValue(target, value); }" in hook
    # 0.4.0 root causes from live logs:
    # - server spawn needs explicit module registration (ArmorRestored's
    #   CommandService.CommandCollection.Collect in a FirstInitialize seam)
    assert "CommandService.CommandCollection.Collect" in read("pchost/ServerCommands.cs")
    assert "ServerSeam.cs" in read("pchost/ATTBuilding.csproj") and "FirstInitialize" in read("pchost/ServerSeam.cs")
    # - Open is the menu-open lifecycle: suppress-only, never a toggle.
    qb = read("quest/Bubble.cs")
    open_seg = qb[qb.index("EquipmentOpenSuppress"):qb.index("EquipmentPrefix", qb.index("EquipmentOpenSuppress"))]
    assert "ConsoleOpener.Toggle" not in open_seg and "_runPatched" in qb
    assert "if (_runPatched) return;" in qb  # poll stands down when Run patched
    # - the post-command refocus is a delayed off/on cycle, not an instant call.
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "ProcessRefocus" in hook and "_refocusAt" in hook
    # 0.3.0 user requests: dummy suppression patches ONLY the concrete
    # EquipmentModeQuickAccess (never the base class), console spawns a tad
    # up, and the input is refocused/respawned after commands. (The console
    # grab handle from this era is deleted in 0.5.0: no grabbing anywhere.)
    for proj in ("pchost", "quest"):
        bubble = read(f"{proj}/Bubble.cs")
        assert "EquipmentModeQuickAccess" in bubble and "EquipmentPrefix" in bubble
        assert 'AccessTools.Method(typeof(QuickAccessMenuAction), "Open")' not in bubble
        assert "SuppressDummy" in bubble
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "UnityEngine.Vector3.up * 0.12f" in hook and "Vector3.down * 0.25f" not in hook
        assert "RefocusInput" in hook and "respawned after command" in hook
        assert "ADD_Pickup" not in hook and "EnsureMovable" not in hook
    # Crash discipline, round 2 (crash ON menu open): no Harmony patches on
    # QuickAccessMenuAction Run/Open in either build (ArmorUpdate leaves
    # Run/Open/Close untouched) -- activation is polled bool state instead.
    for proj in ("pchost", "quest"):
        bubble = read(f"{proj}/Bubble.cs")
        assert "ActionPrefix" not in bubble, proj
        assert "PollActivation" in bubble and "PriorityBools" in bubble
        assert "BubbleEnabled" in bubble  # pref kill-switch honoured
    assert "DumpActionApi" in read("quest/Bubble.cs")  # real API -> debug.log
    assert "Diag.cs" in read("quest/ATTBuilding.csproj") and "Diag.cs" in read("pchost/ATTBuilding.csproj")
    assert "debug.log" in read("quest/Diag.cs")
    # Crash discipline (Quest hard-close ~90s in): the bubble must clone a
    # ScriptableObject action ASSET (ArmorUpdate's EquipmentMenu), never the
    # first scene entry of quickAccessMenus, and must reuse its one clone.
    bubble = read("quest/Bubble.cs")
    assert "EquipmentModeQuickAccess" in bubble and "FindTemplate" in bubble
    assert "if (_action == null)" in bubble and "Object.Instantiate(template)" in bubble
    assert "template = a; break" not in bubble
    assert "Crumb.cs" in read("quest/ATTBuilding.csproj") and "Crumb.At" in bubble
    # Quest-only compile traps from the IL2CPP surface:
    for bad in ("SERVER_IATTBuildingQuestTALL", "SERVER_IATTBuildingTALL"):
        for f in (ROOT / "quest").glob("*.cs"):
            assert bad not in f.read_text(encoding="utf-8"), f
    assert "_running = true;" in read("quest/Main.cs")
    # Il2CppSystem collections are not C#-foreach-able: AllPrefabs is only
    # ever looped through the NativeSnapshot copy (ArmorUpdate pattern).
    quest_spawner = read("quest/Spawner.cs")
    assert "in PrefabManager.AllPrefabs)" not in quest_spawner
    assert quest_spawner.count("NativeSnapshot.Items(PrefabManager.AllPrefabs)") == 3  # Resolve, nickname-by-hash, ListMatching
    assert "NativeSnapshot.cs" in read("quest/ATTBuilding.csproj")
    # ConvertDelegate<T> wants the IL2CPP delegate type, not System.Action.
    assert "ConvertDelegate<Il2CppSystem.Action<string, string>>" in quest_spawner
    assert "ConvertDelegate<System.Action" not in quest_spawner
    # Shared types live in namespace ATTBuilding; quest code is in the
    # sibling ATTBuildingQuest, so any quest file naming one must import it
    # (the PC files share the namespace and need no using).
    shared_types = ("BuildState", "TrackedRegistry", "VectorSnap", "ParsedCommand", "CommandParser")
    for name in ("Main.cs", "ConsoleHook.cs", "Spawner.cs", "Builder.cs"):
        src = read(f"quest/{name}")
        assert any(s in src for s in shared_types), name
        assert "using ATTBuilding;" in src, name
    assert "namespace ATTBuilding" in read("shared/BuildState.cs")
    # Reflection helpers must be reachable from ConsoleOpener (same file,
    # different class): internal, and the cross-class call is qualified.
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "internal static void SetMember" in hook
        assert "ConsoleHook.SetMember(rc" in hook
    assert "<PlatformTarget>x64</PlatformTarget>" in read("Directory.Build.props")
    assert "ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch" in read("Directory.Build.props")
    # CS0104 cannot come back: the entry class is not called Main anywhere.
    assert "class ATTBuildingMod : MelonMod" in read("pchost/Main.cs")
    assert "class ATTBuildingMod : MelonMod" in read("quest/Main.cs")
    assert "ATTBuilding.ATTBuildingMod" in read("pchost/Properties/AssemblyInfo.cs")
    assert "Debug|Any CPU" in read("pchost/ATTBuilding.csproj")  # VS platform spelling covered
    # CS0246: never name the game's InputField type (its namespace differs
    # between Mono and IL2CPP); the console prefix takes it as object.
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "object field" in hook and "ReadInputText(field)" in hook
        assert "(RuntimeConsole __instance, InputField" not in hook


if __name__ == "__main__":
    for name, fn in sorted(list(globals().items())):
        if name.startswith("test_"): fn(); print("ok", name)
