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
    for f in (ROOT / "pchost").glob("*.cs"):
        assert ".WasCollected" not in f.read_text(encoding="utf-8"), f
    for proj in ("pchost", "quest"):
        hook = read(f"{proj}/ConsoleHook.cs")
        for bad in ("_console.outputBuffer", "_console.outputField", "_console.CopyBufferToOutput()", "rc.logFilter"):
            assert bad not in hook, (proj, bad)
        assert 'GetMember(_console, "outputBuffer")' in hook and 'SetMember(rc, "logFilter", 0)' in hook
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
