
using System;
using ATTBuilding; // shared/ types (BuildState, CommandParser, TrackedRegistry, VectorSnap): sibling namespace, not visible unqualified without this
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppAlta.Meta.UI;
using Il2CppAlta.Networking;
using Il2CppAlta.Networking.Scripts.Player;

namespace ATTBuildingQuest
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
        // reflection (the same seam EquipmentMenu uses for Il2Cpp fields).
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
            try { RefocusInput(field); } catch (Exception ex) { Diag.FileOnly("refocus: " + ex.Message); }
            _refocusAt = UnityEngine.Time.unscaledTime + 0.35f; // processed in TickTimeout
            // If submitting dismissed the panel itself, respawn it straight
            // away (a tad up, in front) so the keyboard comes back with it.
            try { if (!ConsoleOpener.IsVisible) Diag.FileOnly("console: respawned after command: " + ConsoleOpener.Summon()); } catch { }
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
                case CommandKind.ServerPing: Spawner.PingServer(Print); break;
                case CommandKind.Clear: Print(Spawner.ClearSpawned() + " console-spawned object(s) removed."); break;
                case CommandKind.Unknown: Print(cmd.Error); break;
            }
        }

        internal static void Print(string line)
        {
            if (Mod.Log != null) Mod.Log.Msg("[console] " + line);
            try
            {
                if (_console == null) return;
                // outputBuffer / outputField / CopyBufferToOutput are private
                // fields on the Mono RuntimeConsole and interop properties on
                // IL2CPP -- naming them directly only compiles on one build.
                object buffer = GetMember(_console, "outputBuffer");
                if (buffer == null) return;
                int max = 0;
                object outputField = GetMember(_console, "outputField");
                if (outputField != null && GetMember(outputField, "MaxLines") is int ml) max = ml;
                if (max > 0) while (GetMember(buffer, "Count") is int count && count >= max) Call(buffer, "Dequeue");
                Call(buffer, "Enqueue", line);
                Call(_console, "CopyBufferToOutput");
            }
            catch (Exception ex) { Mod.LogOnce("console print: " + ex.Message); }
        }

        internal static object GetMember(object target, string name)
        {
            if (target == null) return null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var type = target.GetType();
            var prop = type.GetProperty(name, flags);
            if (prop != null) return prop.GetValue(target, null);
            var field = type.GetField(name, flags);
            return field == null ? null : field.GetValue(target);
        }

        internal static void SetMember(object target, string name, object value)
        {
            if (target == null) return;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var type = target.GetType();
            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite) { prop.SetValue(target, ConvertTo(prop.PropertyType, value), null); return; }
            var field = type.GetField(name, flags);
            if (field != null) field.SetValue(target, ConvertTo(field.FieldType, value));
        }

        internal static object ConvertTo(System.Type type, object value)
        {
            if (value == null || type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return System.Enum.ToObject(type, value);
            return System.Convert.ChangeType(value, type);
        }

        internal static void Call(object target, string name, params object[] args)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var method in target.GetType().GetMethods(flags))
            {
                if (method.Name != name || method.GetParameters().Length != args.Length) continue;
                method.Invoke(target, args); return;
            }
        }

        // Server replies to routed spawn requests arrive asynchronously
        // (AdminConsole pattern from the reference code). Keep the exact
        // pending command so a timeout can say WHAT was lost and a reply
        // can be matched to it in debug.log on both ends.
        private static string _pendingCommand;
        private static float _pendingStartedAt = -1f;

        internal static void NoteServerReply(string result)
        {
            float waited = _pendingStartedAt >= 0f ? UnityEngine.Time.time - _pendingStartedAt : -1f;
            Diag.FileOnly("route: reply matched pending='" + (_pendingCommand ?? "(none)") + "' waited=" + (waited >= 0f ? waited.ToString("F2") + "s" : "?") + " result='" + (result ?? "(null)") + "'");
            _waitingUntil = -1f;
            _pendingCommand = null;
            _pendingStartedAt = -1f;
            if (!string.IsNullOrEmpty(result)) Print("(server) " + result);
        }

        internal static void BeginWait(Action<string> print) { BeginWait(print, null); }

        internal static void BeginWait(Action<string> print, string command)
        {
            _print = print;
            _pendingCommand = command;
            _pendingStartedAt = UnityEngine.Time.time;
            _waitingUntil = UnityEngine.Time.time + 10f;
            Diag.Log("route: waiting up to 10s for server reply to '" + (command ?? "?") + "'");
        }

        private static object _lastField;

        // Re-focus the console's input field (brings the Quest keyboard back
        // up) right after a command, so you can type the next one without
        // tapping the field again. Reflection: the InputField type/members
        // differ between Mono and IL2CPP (never name them directly).
        internal static void RefocusInput(object field)
        {
            if (field != null) _lastField = field;
            object target = field ?? _lastField;
            if (target == null) return;
            var type = target.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (string name in new[] { "ActivateInputField", "Select", "OnPointerClick" })
            {
                var m = type.GetMethod(name, flags, null, System.Type.EmptyTypes, null);
                if (m == null) continue;
                m.Invoke(target, null);
                Diag.FileOnly("console: input refocused via " + name + " (keyboard should be back).");
                return;
            }
            Diag.FileOnly("console: no refocus method found on input field; tap it to type again.");
        }

        // Let the player pick the console up and move it like an item:
        // consoles have no Pickup of their own, so lend them one (plus a
        // gravity-free Rigidbody, which Pickup requires). Best-effort and
        // logged -- if the game's Pickup rejects a non-networked host,
        // debug.log says so and the console simply stays put.
        internal static void EnsureMovable(UnityEngine.GameObject go)
        {
            // A runtime-added Pickup starts with its [GetComponent]-injected
            // fields EMPTY (that injection happens in the editor): no
            // rigidbody field -> CheckInteractAllowed refuses every grab,
            // and no childrenColliders. Fill them by reflection after adding.
            try
            {
                var existing = go.GetComponent<Pickup>();
                if (existing != null) { Diag.Log("console: console already has a Pickup; fixing its fields and locking it in place."); FixupPickup(go, existing); return; }
                var child = go.GetComponentInChildren<Pickup>();
                if (child != null) Diag.Log("console: note: child Pickup found on '" + child.gameObject.name + "'; adding a root one for the panel.");
                var body = go.GetComponent<UnityEngine.Rigidbody>();
                if (body == null) body = go.AddComponent<UnityEngine.Rigidbody>();
                if (body != null) { body.useGravity = false; body.velocity = UnityEngine.Vector3.zero; body.angularVelocity = UnityEngine.Vector3.zero; body.isKinematic = true; body.constraints = UnityEngine.RigidbodyConstraints.FreezeAll; }
                Diag.FileOnly("console movable: rigidbody " + (body == null ? "MISSING" : "ok"));
                var added = go.AddComponent(Il2CppType.Of<Pickup>());
                if (added == null) { Diag.Log("console: AddComponent<Pickup> returned null."); return; }
                var pickup = go.GetComponent<Pickup>();
                if (pickup == null) { Diag.Log("console: Pickup did NOT stick on the console (see debug.log)."); return; }
                FixupPickup(go, pickup);
                Diag.Log("console: made grabbable -- grab the panel to move it.");
            }
            catch (Exception ex) { Diag.Log("console movable FAILED: " + ex.Message); Diag.FileOnly("console movable: " + ex); }
        }

        private static void FixupPickup(UnityEngine.GameObject go, Pickup pickup)
        {
            try { var body = go.GetComponent<UnityEngine.Rigidbody>(); if (body != null) SetMember(pickup, "rigidbody", body); } catch (Exception ex) { Diag.FileOnly("fixup rigidbody: " + ex.Message); }
            try { SetMember(pickup, "childrenColliders", go.GetComponentsInChildren<UnityEngine.Collider>()); } catch (Exception ex) { Diag.FileOnly("fixup colliders: " + ex.Message); }
            LockConsole(go, true);
            Diag.FileOnly("console movable: field fix-ups applied (rigidbody, childrenColliders); locked until grabbed.");
        }

        private static UnityEngine.GameObject _motionConsole;
        private static UnityEngine.Vector3 _lockedPosition;
        private static UnityEngine.Quaternion _lockedRotation;
        private static bool _hasLockedPose;
        private static bool _consoleGrabbed;

        // The console is a UI panel, not a loose physics prop. Keep every
        // Rigidbody in its hierarchy kinematic and frozen until a player's
        // hand is genuinely interacting with its Pickup. A touch or collision
        // therefore cannot push it, and a zero-gravity body cannot sail away.
        // While grabbed, Pickup owns the body; the first tick after release
        // clears both velocities and locks the panel at the release pose.
        private static void LockConsole(UnityEngine.GameObject go, bool rememberPose)
        {
            if (go == null) return;
            _motionConsole = go;
            if (rememberPose)
            {
                _lockedPosition = go.transform.position;
                _lockedRotation = go.transform.rotation;
                _hasLockedPose = true;
            }
            foreach (var body in go.GetComponentsInChildren<UnityEngine.Rigidbody>())
            {
                if (body == null) continue;
                if (!body.isKinematic)
                {
                    body.velocity = UnityEngine.Vector3.zero;
                    body.angularVelocity = UnityEngine.Vector3.zero;
                    body.isKinematic = true;
                }
                body.useGravity = false;
                body.constraints = UnityEngine.RigidbodyConstraints.FreezeAll;
                body.Sleep();
            }
        }

        private static void UnlockConsole(UnityEngine.GameObject go)
        {
            if (go == null) return;
            _motionConsole = go;
            _hasLockedPose = false;
            foreach (var body in go.GetComponentsInChildren<UnityEngine.Rigidbody>())
            {
                if (body == null) continue;
                body.constraints = UnityEngine.RigidbodyConstraints.None;
                body.useGravity = false;
                if (body.isKinematic) body.isKinematic = false;
                body.WakeUp();
            }
        }

        private static bool IsConsoleGrabbed(UnityEngine.GameObject go)
        {
            var player = PlayerController.Current;
            if (player == null || go == null) return false;
            foreach (var interactor in player.GetComponentsInChildren<Interactor>())
            {
                if (interactor == null || !interactor.IsLocal || !interactor.IsInteracting) continue;
                var pickup = interactor.InteractingWith == null ? null : interactor.InteractingWith.TryCast<Pickup>();
                if (pickup != null && (pickup.transform == go.transform || pickup.transform.IsChildOf(go.transform))) return true;
            }
            return false;
        }

        private static void TickConsoleLock()
        {
            try
            {
                var go = ConsoleOpener.ConsoleObject;
                if (go == null) { _consoleGrabbed = false; _hasLockedPose = false; _motionConsole = null; return; }
                if (_motionConsole != go) { _consoleGrabbed = false; LockConsole(go, true); }
                if (!go.activeInHierarchy) { _consoleGrabbed = false; LockConsole(go, false); return; }

                bool grabbed = IsConsoleGrabbed(go);
                if (grabbed)
                {
                    if (!_consoleGrabbed)
                    {
                        _consoleGrabbed = true;
                        UnlockConsole(go);
                        Diag.Log("console: grabbed -- movement unlocked only while held.");
                    }
                    return;
                }

                if (_consoleGrabbed)
                {
                    _consoleGrabbed = false;
                    LockConsole(go, true);
                    Diag.Log("console: released -- locked in place until grabbed again.");
                    return;
                }

                bool needsLock = false;
                foreach (var body in go.GetComponentsInChildren<UnityEngine.Rigidbody>())
                {
                    if (body != null && (!body.isKinematic || body.constraints != UnityEngine.RigidbodyConstraints.FreezeAll)) { needsLock = true; break; }
                }
                if (needsLock) LockConsole(go, false);
                if (_hasLockedPose && _motionConsole == go)
                {
                    if ((go.transform.position - _lockedPosition).sqrMagnitude > 0.000001f) go.transform.position = _lockedPosition;
                    if (UnityEngine.Quaternion.Angle(go.transform.rotation, _lockedRotation) > 0.01f) go.transform.rotation = _lockedRotation;
                }
            }
            catch (Exception ex) { Diag.FileOnly("console lock: " + ex.Message); }
        }

        private static float _refocusAt = -1f;

        private static void ProcessRefocus()
        {
            if (_refocusAt < 0f || UnityEngine.Time.unscaledTime < _refocusAt) return;
            _refocusAt = -1f;
            try
            {
                if (!ConsoleOpener.IsVisible)
                {
                    Diag.Log("console: respawned after command: " + ConsoleOpener.Summon());
                    return;
                }
                // off/on cycle so the input field re-activates fresh
                var go = ConsoleOpener.ConsoleObject;
                if (go != null) { go.SetActive(false); go.SetActive(true); }
                RefocusInput(null);
                Diag.Log("console: refocused after command (keyboard should be back).");
            }
            catch (Exception ex) { Diag.FileOnly("refocus cycle: " + ex); }
        }

        internal static void TickTimeout()
        {
            TickConsoleLock();
            ProcessRefocus();
            if (_waitingUntil < 0f || UnityEngine.Time.time < _waitingUntil) return;
            _waitingUntil = -1f;
            string pending = _pendingCommand ?? "(unknown command)";
            _pendingCommand = null;
            _pendingStartedAt = -1f;
            Diag.Log("route: TIMEOUT after 10s waiting for '" + pending + "'. No callback reached this client. "
                + "Server checks: is the PC build of ATTBuilding " + Mod.Version + " in the server's Mods folder and loaded "
                + "('ATTBuilding " + Mod.Version + " started (PC Mono)')? Does its log/debug.log show "
                + "'server: registering build module' followed by 'server spawn: received' (or 'server ping: received')? "
                + "If neither line is there, the request never reached a registered ATTBuilding command on the server.");
            if (_print != null) _print("No answer from the server for '" + pending + "'. The server may not run ATTBuilding (PC build " + Mod.Version + "), its 'build' module may not be registered, or you may lack spawn permission there. Type 'build ping' to test, and check the server log for 'server spawn: received'.");
        }
    }


    // Summons the game's RuntimeConsole in front of the player (the
    // DevToolsPatch pattern): reuse the scene copy if one exists, otherwise
    // instantiate the loaded prefab copy. Used by the quick-access bubble.
    internal static class ConsoleOpener
    {
        private static UnityEngine.GameObject _console;
        internal static UnityEngine.GameObject ConsoleObject { get { return _console; } }
        internal static bool IsVisible { get { try { return _console != null && _console.activeInHierarchy; } catch { return false; } } }

        internal static string Toggle()
        {
            if (IsVisible) { try { _console.SetActive(false); return "Console hidden."; } catch (Exception ex) { return "Console hide failed: " + ex.Message; } }
            return Summon();
        }

        internal static string Summon()
        {
            try
            {
                Crumb.At("console: summon");
                var player = PlayerController.Current;
                var head = player == null ? null : player.Head;
                if (head == null) return "Console: player not spawned yet.";
                RuntimeConsole sceneCopy = null, prefabCopy = null;
foreach (UnityEngine.Object value in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<RuntimeConsole>()))
                {
                    var rc2 = value == null ? null : value.TryCast<RuntimeConsole>();
                    if (rc2 == null) continue;
                    if (rc2.gameObject.scene.IsValid()) sceneCopy = rc2; else prefabCopy = rc2;
                }
                if (sceneCopy == null && prefabCopy == null) return "Console: none loaded.";
                UnityEngine.GameObject go; string how;
                if (sceneCopy != null) { go = sceneCopy.gameObject; how = "activated scene copy"; }
                else { go = UnityEngine.Object.Instantiate(prefabCopy.gameObject).TryCast<UnityEngine.GameObject>(); how = "instantiated"; }
                if (go == null) return "Console: could not create.";
                var forward = head.forward; forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = UnityEngine.Vector3.forward;
                forward.Normalize();
                go.transform.SetParent(null);
                go.transform.position = head.position + forward * 1.0f + UnityEngine.Vector3.up * 0.12f; // a tad up: type without bending down
                go.transform.rotation = UnityEngine.Quaternion.LookRotation(forward);
                go.SetActive(true);
                _console = go;
                ConsoleHook.EnsureMovable(go);
                var rc = go.GetComponent<RuntimeConsole>();
                if (rc != null) { try { ConsoleHook.SetMember(rc, "logFilter", 0); } catch { } }
                Crumb.At("console: summoned");
                return "ATT Building console open (" + how + "). Type: build help";
            }
            catch (Exception ex) { return "Console summon failed: " + ex; }
        }
    }
}
