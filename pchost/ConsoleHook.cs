
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Alta.Networking.Scripts.Player;

namespace ATTBuilding
{
    // Hooks the game's own RuntimeConsole (the ShardClient pattern): lines
    // starting with "build"/"attb", plus the bare verbs "spawn", "buildmode"
    // and "snapping", are ours; everything else goes to the game untouched.
    internal static class ConsoleHook
    {
        private static RuntimeConsole _console;
        private static float _waitingUntil = -1f;
        private static Action<string> _print;

        internal static string Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(RuntimeConsole), "OnInputSubmitted"),
                    prefix: new HarmonyMethod(typeof(ConsoleHook), nameof(Prefix)));
                return "console: patched RuntimeConsole.OnInputSubmitted for 'build' commands.";
            }
            catch (Exception ex) { return "console: patch failed: " + ex.Message; }
        }

        // `field` is the game's InputField, declared as object on purpose: the
        // type's namespace is not the same in the Mono and IL2CPP assemblies,
        // and naming it wrong is a compile error. Harmony injects arguments by
        // parameter name, so object works; InputText/SetText are read by
        // reflection (the same reflection seam the reference mods use for game fields).
        private static bool Prefix(RuntimeConsole __instance, object field)
        {
            string text;
            try { text = field == null ? null : ReadInputText(field); } catch { return true; }
            if (string.IsNullOrWhiteSpace(text)) return true;
            string[] words = text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string first = words[0].ToLowerInvariant();
            if (first != "build" && first != "attb" && first != "spawn" && first != "buildmode" && first != "snapping")
                return true; // not ours: game's own console handles it
            _console = __instance;
            Print("> " + text.Trim());
            try { Run(CommandParser.Parse(text)); }
            catch (Exception ex) { Print("Error: " + ex.Message); Mod.LogOnce("build command: " + ex); }
            try { ClearInput(field); } catch { }
            return false;
        }


        private static string ReadInputText(object field)
        {
            var type = field.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var prop = type.GetProperty("InputText", flags);
            if (prop != null) return prop.GetValue(field, null) as string;
            var member = type.GetField("InputText", flags);
            return member == null ? null : member.GetValue(field) as string;
        }

        private static void ClearInput(object field)
        {
            if (field == null) return;
            var type = field.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var setText = type.GetMethod("SetText", flags, null, new[] { typeof(string) }, null);
            if (setText != null) { setText.Invoke(field, new object[] { string.Empty }); return; }
            var prop = type.GetProperty("InputText", flags);
            if (prop != null && prop.CanWrite) prop.SetValue(field, string.Empty, null);
        }

        private static void Run(ParsedCommand cmd)
        {
            switch (cmd.Kind)
            {
                case CommandKind.Help:
                    foreach (string line in CommandParser.HelpLines) Print(line);
                    Print("state: build mode " + (BuildState.BuildMode ? "on" : "off") + ", snapping " + (BuildState.Snapping ? "true" : "false")
                        + ", snapangle " + BuildState.AngleStepDegrees + ", grid " + BuildState.GridMetres + " m");
                    break;
                case CommandKind.Spawn: Spawner.SpawnByName(cmd.Text, (int)cmd.Number, Print); break;
                case CommandKind.BuildMode:
                    BuildState.BuildMode = cmd.Flag;
                    if (cmd.Flag) BuildMode.EnsureAllGrabbable();
                    Print("Build mode " + (cmd.Flag ? "ON: grab anything this console spawned and move it like an item. Release to place it." : "off."));
                    break;
                case CommandKind.Snapping:
                    BuildState.Snapping = cmd.Flag; Settings.Save();
                    Print("Snapping " + cmd.Flag + ": held spawned objects snap to " + BuildState.AngleStepDegrees + "-degree angles.");
                    break;
                case CommandKind.SnapAngle:
                    BuildState.AngleStepDegrees = cmd.Number; Settings.Save();
                    Print("Snap angle = " + cmd.Number + " degrees" + (cmd.Number == 0 ? " (rotation snapping off)" : "") + ".");
                    break;
                case CommandKind.Grid:
                    BuildState.GridMetres = cmd.Number; Settings.Save();
                    Print("Position grid = " + cmd.Number + " m" + (cmd.Number == 0 ? " (position snapping off)" : "") + " (applied on release).");
                    break;
                case CommandKind.LivePositionSnap:
                    BuildState.LivePositionSnap = cmd.Flag;
                    Print("Live position snapping " + cmd.Flag + ".");
                    break;
                case CommandKind.List: Spawner.ListMatching(cmd.Text, Print); break;
                case CommandKind.Spawned: Print(TrackedRegistry.Count + " console-spawned object(s) tracked for build mode."); break;
                case CommandKind.Clear: Print(Spawner.ClearSpawned() + " console-spawned object(s) removed."); break;
                case CommandKind.Unknown: Print(cmd.Error); break;
            }
        }

        internal static void Print(string line)
        {
            if (Mod.Log != null) Mod.Log.Msg("[console] " + line);
            try
            {
                if (_console == null || _console.WasCollected) return;
                var buffer = _console.outputBuffer;
                int max = _console.outputField == null ? 0 : _console.outputField.MaxLines;
                while (max > 0 && buffer.Count >= max) buffer.Dequeue();
                buffer.Enqueue(line);
                _console.CopyBufferToOutput();
            }
            catch (Exception ex) { Mod.LogOnce("console print: " + ex.Message); }
        }

        // Server replies to routed spawn requests arrive asynchronously
        // (AdminConsole pattern from the reference code).
        internal static void NoteServerReply(string result) { if (!string.IsNullOrEmpty(result)) Print("(server) " + result); }
        internal static void BeginWait(Action<string> print) { _print = print; _waitingUntil = UnityEngine.Time.time + 10f; }
        internal static void TickTimeout()
        {
            if (_waitingUntil < 0f || UnityEngine.Time.time < _waitingUntil) return;
            _waitingUntil = -1f;
            if (_print != null) _print("No answer from the server. It may not run ATTBuilding, or you may lack spawn permission there.");
        }
    }

    // Summons the game's RuntimeConsole in front of the player (the
    // DevToolsPatch pattern): reuse the scene copy if one exists, otherwise
    // instantiate the loaded prefab copy. Used by the quick-access bubble.
    internal static class ConsoleOpener
    {
        private static UnityEngine.GameObject _console;
        internal static bool IsVisible { get { return _console != null && !_console.WasCollected && _console.activeInHierarchy; } }

        internal static string Toggle()
        {
            if (IsVisible) { try { _console.SetActive(false); return "Console hidden."; } catch (Exception ex) { return "Console hide failed: " + ex.Message; } }
            return Summon();
        }

        internal static string Summon()
        {
            try
            {
                var player = PlayerController.Current;
                var head = player == null ? null : player.Head;
                if (head == null) return "Console: player not spawned yet.";
                RuntimeConsole sceneCopy = null, prefabCopy = null;
foreach (var value in UnityEngine.Resources.FindObjectsOfTypeAll(typeof(RuntimeConsole)))
                {
                    var rc2 = value as RuntimeConsole;
                    if (rc2 == null) continue;
                    if (rc2.gameObject.scene.IsValid()) sceneCopy = rc2; else prefabCopy = rc2;
                }
                if (sceneCopy == null && prefabCopy == null) return "Console: none loaded.";
                UnityEngine.GameObject go; string how;
                if (sceneCopy != null) { go = sceneCopy.gameObject; how = "activated scene copy"; }
                else { go = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(prefabCopy.gameObject); how = "instantiated"; }
                if (go == null) return "Console: could not create.";
                var forward = head.forward; forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = UnityEngine.Vector3.forward;
                forward.Normalize();
                go.transform.SetParent(null);
                go.transform.position = head.position + forward * 0.9f + UnityEngine.Vector3.down * 0.25f;
                go.transform.rotation = UnityEngine.Quaternion.LookRotation(forward);
                go.SetActive(true);
                _console = go;
                var rc = go.GetComponent<RuntimeConsole>();
                if (rc != null) rc.logFilter = 0;
                return "ATT Building console open (" + how + "). Type: build help";
            }
            catch (Exception ex) { return "Console summon failed: " + ex; }
        }
    }
}
