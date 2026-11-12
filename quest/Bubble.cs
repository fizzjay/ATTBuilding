
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppAlta.QuickAccessActions;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ATTBuildingQuest
{
    // The new wrist-menu bubble. Mirrors ArmorUpdate's quest EquipmentMenu:
    // scan the LOCAL player's QuickAccessMenuController, clone a native
    // QuickAccessMenuAction, append it to quickAccessMenus, un-ignore it --
    // and leave every native action alone. When OUR clone is opened/run we
    // summon the building console instead (patching Run/Open only diverts
    // instances whose id we registered).
    internal static class Bubble
    {
        private static int _ourActionId;
        private static QuickAccessMenuController _menu;
        private static QuickAccessMenuAction _action;
        private static float _nextScan;
        private static bool _installed, _warned;

        internal static string Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var ignored = AccessTools.PropertyGetter(typeof(QuickAccessMenuAction), "IsIgnored");
                if (ignored != null) harmony.Patch(ignored, postfix: new HarmonyMethod(typeof(Bubble), nameof(IgnoredPostfix)));
                if (Mod.SuppressDummy)
                {
                    // Concrete type only (NOT the QuickAccessMenuAction base
                    // class that crashed on menu-open when patched), and
                    // only methods EquipmentModeQuickAccess itself declares.
                    // Open is the menu-open lifecycle: suppress-only (no
                    // dummy, NO console). Run is the actual pick: toggle.
                    PatchDeclared(harmony, "Open", nameof(EquipmentOpenSuppress));
                    if (PatchDeclared(harmony, "Run", nameof(EquipmentPrefix))) _runPatched = true;
                    else Diag.FileOnly("bubble: EquipmentModeQuickAccess.Run not patchable; polled activation remains.");
                    // THE RED BUBBLE: QuickAccessMenuBubble.Run() refuses to
                    // run an action whose IsValid is false (red/invalid
                    // tint, no activation). Our clone's native validity is
                    // tied to the equipment-mode session we suppress, so
                    // force IsValid=true for OUR INSTANCE ONLY. (In 0.2.0
                    // the poll bypassed this gate; the Run patch cannot.)
                    var valid = AccessTools.PropertyGetter(typeof(EquipmentModeQuickAccess), "IsValid");
                    if (valid != null) { harmony.Patch(valid, postfix: new HarmonyMethod(typeof(Bubble), nameof(ValidPostfix))); Diag.Log("bubble: IsValid forced true for our clone (fixes red/unopenable bubble)."); }
                    else Diag.FileOnly("bubble: IsValid getter not found on EquipmentModeQuickAccess.");
                }
                _installed = true;
                DumpActionApi();
                return "bubble: quick-access 'ATT Building Console' ready (activation is polled, Run/Open are NOT patched).";
            }
            catch (Exception ex) { return "bubble: hook failed: " + ex.Message; }
        }

        internal static bool Added { get { return _action != null; } }

        // One-time dump of the real QuickAccessMenuAction surface on THIS
        // game build (methods + bool members) into debug.log.
        private static void DumpActionApi()
        {
            try
            {
                var type = typeof(QuickAccessMenuAction);
                Diag.FileOnly("QuickAccessMenuAction methods:");
                foreach (var m in type.GetMethods(Members))
                    Diag.FileOnly("  m " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", Array.ConvertAll(m.GetParameters(), x => x.ParameterType.Name + " " + x.Name)) + ")");
                Diag.FileOnly("QuickAccessMenuAction bool members:");
                foreach (var m in BoolMembers(type)) Diag.FileOnly("  b " + m.Name);
            }
            catch (Exception ex) { Diag.FileOnly("api dump failed: " + ex.Message); }
        }

        private sealed class BoolMember
        {
            internal string Name; internal PropertyInfo Prop; internal FieldInfo Field;
            internal bool Get(object target)
            {
                try { return Prop != null ? (bool)Prop.GetValue(target, null) : (bool)Field.GetValue(target); }
                catch { return false; }
            }
        }

        private static List<BoolMember> BoolMembers(System.Type type)
        {
            var list = new List<BoolMember>();
            foreach (var prop in type.GetProperties(Members))
                if (prop.PropertyType == typeof(bool) && prop.CanRead) list.Add(new BoolMember { Name = prop.Name, Prop = prop });
            foreach (var field in type.GetFields(Members))
                if (field.FieldType == typeof(bool)) list.Add(new BoolMember { Name = field.Name, Field = field });
            return list;
        }

        // Activation WITHOUT patching Run/Open: poll our clone's bool state.
        // Selecting a bubble flips an open/active-style bool on the action;
        // we edge-trigger on the priority names below, summon the console,
        // then call the action's own Close() (a plain method call, exactly
        // what the game itself does) so it resets. Every state change is
        // logged, so if no priority name flips, debug.log still shows which
        // member DID -- tell us and we add it.
        private static readonly string[] PriorityBools = { "IsOpen", "isOpen", "IsOpened", "Opened", "IsActive", "isActive", "Active", "IsSelected", "isSelected", "Selected", "IsOn", "IsToggled" };
        private static List<BoolMember> _boolMembers;
        private static readonly Dictionary<string, bool> _lastState = new Dictionary<string, bool>();
        private static float _nextPoll;

        private static void PollActivation()
        {
            if (_runPatched) return; // Run() patch is the activation path; polling would double-toggle
            if (_action == null || UnityEngine.Time.unscaledTime < _nextPoll) return;
            _nextPoll = UnityEngine.Time.unscaledTime + 0.15f;
            if (_boolMembers == null) { _boolMembers = BoolMembers(typeof(QuickAccessMenuAction)); Snapshot("initial"); return; }
            foreach (var member in _boolMembers)
            {
                bool now = member.Get(_action);
                bool before;
                if (!_lastState.TryGetValue(member.Name, out before)) { _lastState[member.Name] = now; continue; }
                if (now == before) continue;
                _lastState[member.Name] = now;
                Diag.Log("bubble: action state " + member.Name + ": " + before + " -> " + now);
                if (now && Array.IndexOf(PriorityBools, member.Name) >= 0)
                {
                    Diag.Log("bubble: activated via " + member.Name + " -- toggling console.");
                    string msg = ConsoleOpener.Toggle();
                    if (msg != null) Mod.Log.Msg(msg);
                    TryClose();
                    Snapshot("after-close");
                }
            }
        }

        private static void Snapshot(string why)
        {
            _lastState.Clear();
            if (_boolMembers == null || _action == null) return;
            foreach (var member in _boolMembers) _lastState[member.Name] = member.Get(_action);
            var parts = new List<string>();
            foreach (var kv in _lastState) parts.Add(kv.Key + "=" + kv.Value);
            Diag.FileOnly("bubble: state snapshot (" + why + "): " + string.Join(", ", parts.ToArray()));
        }

        private static void TryClose()
        {
            try
            {
                var close = AccessTools.Method(typeof(QuickAccessMenuAction), "Close");
                if (close != null) { close.Invoke(_action, null); Diag.FileOnly("bubble: called action Close()."); }
            }
            catch (Exception ex) { Diag.FileOnly("bubble: Close() failed: " + ex.Message); }
        }

        private static bool _runPatched;

        private static bool PatchDeclared(HarmonyLib.Harmony harmony, string name, string prefix)
        {
            var m = AccessTools.Method(typeof(EquipmentModeQuickAccess), name);
            if (m == null) { Diag.FileOnly("bubble: EquipmentModeQuickAccess." + name + " not found."); return false; }
            if (m.DeclaringType != typeof(EquipmentModeQuickAccess)) { Diag.FileOnly("bubble: " + name + " is inherited, not declared on EquipmentModeQuickAccess; not patching."); return false; }
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(Bubble), prefix));
            Diag.FileOnly("bubble: patched EquipmentModeQuickAccess." + name);
            return true;
        }

        private static void ValidPostfix(EquipmentModeQuickAccess __instance, ref bool __result)
        {
            if (__instance != null && _ourActionId != 0 && __instance.GetInstanceID() == _ourActionId) __result = true;
        }

        private static bool EquipmentOpenSuppress(EquipmentModeQuickAccess __instance)
        {
            if (__instance == null || _ourActionId == 0 || __instance.GetInstanceID() != _ourActionId) return true; // not ours: native behaviour (your real equipment dummy) untouched
            Diag.FileOnly("bubble: our clone Open() suppressed (menu-open lifecycle; no dummy, no console).");
            return false;
        }

        private static bool EquipmentPrefix(EquipmentModeQuickAccess __instance)
        {
            if (__instance == null || _ourActionId == 0 || __instance.GetInstanceID() != _ourActionId) return true; // not ours: native behaviour (your real equipment dummy) untouched
            Diag.Log("bubble: our clone Run() -- toggling console, native equipment dummy suppressed.");
            string msg = ConsoleOpener.Toggle();
            if (msg != null) Mod.Log.Msg(msg);
            return false; // skip the native Open/Run that spawns the dummy
        }

        private static void IgnoredPostfix(QuickAccessMenuAction __instance, ref bool __result)
        {
            if (__instance != null && _ourActionId != 0 && __instance.GetInstanceID() == _ourActionId) __result = false;
        }

        internal static void Tick()
        {
            if (!_installed || !Mod.BubbleEnabled) return;
            try { PollActivation(); } catch (Exception ex) { Diag.FileOnly("poll: " + ex.Message); }
            if (UnityEngine.Time.unscaledTime < _nextScan) return;
            _nextScan = UnityEngine.Time.unscaledTime + 1f;
            Crumb.At("bubble: scan");
            if (_action != null && _menu == null) { Object.Destroy(_action); _action = null; _ourActionId = 0; _boolMembers = null; }
            var local = PlayerController.Current;
            if (local == null || local.NetworkPlayer == null) return;
            foreach (Object value in Resources.FindObjectsOfTypeAll(Il2CppType.Of<QuickAccessMenuController>()))
            {
                var menu = value == null ? null : value.TryCast<QuickAccessMenuController>();
                if (menu == null || !menu.gameObject.activeInHierarchy || menu.Controller == null
                    || menu.Controller.PlayerController == null
                    || menu.Controller.PlayerController.Pointer != local.Pointer) continue;
                try { Ensure(menu, local); }
                catch (Exception ex) { if (!_warned) { Mod.Log.Warning("bubble: " + ex.Message); _warned = true; } Diag.FileOnly("ensure: " + ex); }
            }
        }

        private static void Ensure(QuickAccessMenuController menu, PlayerController local)
        {
            var menus = Menus(menu);
            if (menus == null) return;
            foreach (var a in menus) if (a != null && a.GetInstanceID() == _ourActionId) { _menu = menu; return; }

            // Reuse the one clone if the controller rebuilt its menu list;
            // never Instantiate per rebuild.
            if (_action == null)
            {
                // Clone ONLY a ScriptableObject action asset (ArmorUpdate's
                // EquipmentMenu does exactly this with the native
                // EquipmentModeQuickAccess). The previous version cloned the
                // first entry of quickAccessMenus: if that entry is a scene
                // Component, Object.Instantiate clones its entire GameObject
                // (the wrist-menu rig), every menu rebuild -- native-crash
                // territory. If no asset template is loaded, add nothing.
                QuickAccessMenuAction template = FindTemplate();
                if (template == null)
                {
                    if (!_warned) { Mod.Log.Warning("bubble: no cloneable quick-access action asset loaded yet; bubble not added."); _warned = true; }
                    return;
                }
                Crumb.At("bubble: instantiate template " + template.name);
                _action = Object.Instantiate(template);
                _action.name = "ATT Building Console";
                try { ConsoleHook.SetMember(_action, "displayName", "Building"); } catch { } // DisplayName is get-only over this field
                _ourActionId = _action.GetInstanceID();
                Crumb.At("bubble: cloned " + _ourActionId);
            }
            Crumb.At("bubble: InitializeForPlayer");
            _action.InitializeForPlayer(local.NetworkPlayer, false);
            var updated = new Il2CppReferenceArray<QuickAccessMenuAction>(menus.Length + 1);
            for (int i = 0; i < menus.Length; i++) updated[i] = menus[i];
            updated[menus.Length] = _action;
            Crumb.At("bubble: append to quickAccessMenus");
            SetMenus(menu, updated);
            _menu = menu;
            Crumb.At("bubble: added");
            Diag.Log("bubble: 'ATT Building Console' added to the wrist menu (id " + _ourActionId + "); reopen it and pick Building.");
            _boolMembers = null; Snapshot("added");
        }

        private static QuickAccessMenuAction FindTemplate()
        {
            // Preferred: the native Equipment Mode action asset (proven
            // cloneable by ArmorUpdate's EquipmentMenu).
            foreach (Object value in Resources.FindObjectsOfTypeAll(Il2CppType.Of<EquipmentModeQuickAccess>()))
            {
                var native = value == null ? null : value.TryCast<EquipmentModeQuickAccess>();
                if (native != null) return native.TryCast<QuickAccessMenuAction>();
            }
            // Fallback: any loaded action asset that is NOT a scene Component.
            foreach (Object value in Resources.FindObjectsOfTypeAll(Il2CppType.Of<QuickAccessMenuAction>()))
            {
                var action = value == null ? null : value.TryCast<QuickAccessMenuAction>();
                if (action == null) continue;
                try { if (action.TryCast<Component>() != null) continue; } catch { continue; }
                return action;
            }
            return null;
        }

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Il2CppReferenceArray<QuickAccessMenuAction> Menus(QuickAccessMenuController menu)
        {
            var p = typeof(QuickAccessMenuController).GetProperty("quickAccessMenus", Members);
            if (p != null) return p.GetValue(menu, null) as Il2CppReferenceArray<QuickAccessMenuAction>;
            var f = typeof(QuickAccessMenuController).GetField("quickAccessMenus", Members);
            if (f == null) throw new MissingMemberException("QuickAccessMenuController.quickAccessMenus");
            return f.GetValue(menu) as Il2CppReferenceArray<QuickAccessMenuAction>;
        }
        private static void SetMenus(QuickAccessMenuController menu, Il2CppReferenceArray<QuickAccessMenuAction> value)
        {
            var p = typeof(QuickAccessMenuController).GetProperty("quickAccessMenus", Members);
            if (p != null) { p.SetValue(menu, value, null); return; }
            var f = typeof(QuickAccessMenuController).GetField("quickAccessMenus", Members);
            if (f == null) throw new MissingMemberException("QuickAccessMenuController.quickAccessMenus");
            f.SetValue(menu, value);
        }
    }
}
