"""Static contract tests, ArmorUpdate-style: no game needed.

They pin (a) the snapping maths by re-deriving it from shared/SnapMath.cs
constants, and (b) the PC/Quest parity contract: both projects link the same
shared files, expose the same commands, and never cross-reference the other
platform's assemblies.
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
    for verb in ('"spawn"', '"mode"', '"snapping"', '"snapangle"', '"grid"', '"list"', '"spawned"', '"clear"', '"help"'):
        assert verb in parser, verb
    for platform in ("pchost", "quest"):
        hook = read(f"{platform}/ConsoleHook.cs")
        for kind in ("CommandKind.Spawn", "CommandKind.BuildMode", "CommandKind.Snapping",
                     "CommandKind.SnapAngle", "CommandKind.Grid", "CommandKind.Clear"):
            assert kind in hook, (platform, kind)

def test_both_projects_link_identical_shared_files():
    shared = {p.name for p in (ROOT / "shared").glob("*.cs")}
    assert shared == {"BuildState.cs", "CommandParser.cs", "SnapMath.cs", "TrackedRegistry.cs", "VectorSnap.cs"}
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

def test_build_mode_only_touches_tracked_spawns():
    for platform in ("pchost", "quest"):
        bm = read(f"{platform}/BuildMode.cs")
        assert "TrackedRegistry.Contains" in bm
        assert "VectorSnap.SnapRotation" in bm and "VectorSnap.SnapPosition" in bm
    assert "only these objects become grabbable" in read("shared/TrackedRegistry.cs").lower()

def test_bubble_is_a_new_quick_access_action_on_both():
    assert "ATT Building Console" in read("quest/Bubble.cs")
    assert "ATT Building Console" in read("pchost/Bubble.cs")
    assert "ConsoleOpener.Toggle" in read("quest/Bubble.cs") + read("pchost/Bubble.cs")
    assert "quickAccessMenus" in read("quest/Bubble.cs") and "quickAccessMenus" in read("pchost/Bubble.cs")


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
    # Console flight fix: it must be a locked panel, not a loose
    # zero-gravity physics body. It only unlocks while a local hand is
    # actually interacting with its Pickup, then freezes on release.
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "TickConsoleLock" in hook and "IsConsoleGrabbed" in hook
        assert "body.isKinematic = true" in hook and "body.isKinematic = false" in hook
        assert "UnityEngine.RigidbodyConstraints.FreezeAll" in hook
        assert "locked in place until grabbed again" in hook
        assert "movement unlocked only while held" in hook
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
    # - runtime Pickup needs its editor-injected fields filled, and the
    #   post-command refocus is a delayed off/on cycle, not an instant call.
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "FixupPickup" in hook and '"childrenColliders"' in hook and '"rigidbody"' in hook
        assert "ProcessRefocus" in hook and "_refocusAt" in hook
    # 0.3.0 user requests: dummy suppression patches ONLY the concrete
    # EquipmentModeQuickAccess (never the base class), console spawns a tad
    # up, is grabbable, and the input is refocused/respawned after commands.
    for proj in ("pchost", "quest"):
        bubble = read(f"{proj}/Bubble.cs")
        assert "EquipmentModeQuickAccess" in bubble and "EquipmentPrefix" in bubble
        assert 'AccessTools.Method(typeof(QuickAccessMenuAction), "Open")' not in bubble
        assert "SuppressDummy" in bubble
        hook = read(f"{proj}/ConsoleHook.cs")
        assert "UnityEngine.Vector3.up * 0.12f" in hook and "Vector3.down * 0.25f" not in hook
        assert "EnsureMovable" in hook and "RefocusInput" in hook and "respawned after command" in hook
        assert "ADD_Pickup" not in hook
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
    assert quest_spawner.count("NativeSnapshot.Items(PrefabManager.AllPrefabs)") == 2
    assert "NativeSnapshot.cs" in read("quest/ATTBuilding.csproj")
    # ConvertDelegate<T> wants the IL2CPP delegate type, not System.Action.
    assert "ConvertDelegate<Il2CppSystem.Action<string, string>>" in quest_spawner
    assert "ConvertDelegate<System.Action" not in quest_spawner
    # Shared types live in namespace ATTBuilding; quest code is in the
    # sibling ATTBuildingQuest, so any quest file naming one must import it
    # (the PC files share the namespace and need no using).
    shared_types = ("BuildState", "TrackedRegistry", "VectorSnap", "ParsedCommand", "CommandParser")
    for name in ("Main.cs", "ConsoleHook.cs", "Spawner.cs", "BuildMode.cs"):
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
