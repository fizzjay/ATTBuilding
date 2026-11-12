
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Alta.Networking.Scripts.Player;

namespace ATTBuilding
{
    // PC (Mono) twin of quest/Bubble.cs, reflection-driven so it binds to the
    // Mono ATT.Character.QuickAccessMenu types by name at runtime. Same user
    // result: a new "ATT Building Console" bubble on the wrist menu that opens
    // the building console; native bubbles are untouched.
    internal static class Bubble
    {
        private static int _ourActionId;
        private static object _clone;
        private static float _nextScan;
        private static bool _installed, _warned;
        private static Type _menuType, _actionType;

        internal static string Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                _menuType = AccessTools.TypeByName("QuickAccessMenuController");
                _actionType = AccessTools.TypeByName("QuickAccessMenuAction");
                if (_menuType == null || _actionType == null) return "bubble: QuickAccess types not found in this build.";
                var ignored = AccessTools.PropertyGetter(_actionType, "IsIgnored");
                if (ignored != null) harmony.Patch(ignored, postfix: new HarmonyMethod(typeof(Bubble), nameof(IgnoredPostfix)));
                if (Mod.SuppressDummy)
                {
                    var concrete = AccessTools.TypeByName("EquipmentModeQuickAccess");
                    if (concrete != null)
                    {
                        PatchDeclared(harmony, concrete, "Open", nameof(EquipmentOpenSuppress));
                        if (PatchDeclared(harmony, concrete, "Run", nameof(EquipmentPrefix))) _runPatched = true;
                        // Red/unopenable bubble fix: force IsValid for ours.
                        var valid = AccessTools.PropertyGetter(concrete, "IsValid");
                        if (valid != null) harmony.Patch(valid, postfix: new HarmonyMethod(typeof(Bubble), nameof(ValidPostfix)));
                    }
                }
                _installed = true;
                return "bubble: quick-access 'ATT Building Console' hook ready (reopen the wrist menu after joining).";
            }
            catch (Exception ex) { return "bubble: hook failed: " + ex.Message; }
        }

        private static bool _runPatched;

        private static bool PatchDeclared(HarmonyLib.Harmony harmony, System.Type concrete, string name, string prefix)
        {
            var m = AccessTools.Method(concrete, name);
            if (m == null || m.DeclaringType != concrete) { Diag.FileOnly("bubble: " + name + " not declared on " + concrete.Name + "; not patching."); return false; }
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(Bubble), prefix));
            return true;
        }

        private static void ValidPostfix(object __instance, ref bool __result)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj != null && _ourActionId != 0 && obj.GetInstanceID() == _ourActionId) __result = true;
        }

        private static bool EquipmentOpenSuppress(object __instance)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj == null || _ourActionId == 0 || obj.GetInstanceID() != _ourActionId) return true;
            Diag.FileOnly("bubble: our clone Open() suppressed (menu-open lifecycle; no dummy, no console).");
            return false;
        }

        private static bool EquipmentPrefix(object __instance)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj == null || _ourActionId == 0 || obj.GetInstanceID() != _ourActionId) return true;
            Diag.Log("bubble: our clone Run() -- toggling console, native equipment dummy suppressed.");
            string msg = ConsoleOpener.Toggle();
            if (msg != null) Mod.Log.Msg(msg);
            return false;
        }

        private static void IgnoredPostfix(object __instance, ref bool __result)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj != null && _ourActionId != 0 && obj.GetInstanceID() == _ourActionId) __result = false;
        }


        internal static bool Added { get { return _clone != null; } }

        // Polled activation (no Run/Open patches): edge-trigger on an
        // open/active-style bool flip of our clone, then call its Close().
        private static readonly string[] PriorityBools = { "IsOpen", "isOpen", "IsOpened", "Opened", "IsActive", "isActive", "Active", "IsSelected", "isSelected", "Selected", "IsOn", "IsToggled" };
        private static readonly System.Collections.Generic.Dictionary<string, bool> _lastState = new System.Collections.Generic.Dictionary<string, bool>();
        private static float _nextPoll;

        private static void PollActivation()
        {
            if (_runPatched) return; // Run() patch is the activation path
            var action = _clone as UnityEngine.Object;
            if (action == null || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.15f;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var prop in action.GetType().GetProperties(f))
            {
                if (prop.PropertyType != typeof(bool) || !prop.CanRead) continue;
                bool now; try { now = (bool)prop.GetValue(action, null); } catch { continue; }
                bool before;
                if (!_lastState.TryGetValue(prop.Name, out before)) { _lastState[prop.Name] = now; continue; }
                if (now == before) continue;
                _lastState[prop.Name] = now;
                Diag.Log("bubble: action state " + prop.Name + ": " + before + " -> " + now);
                if (now && System.Array.IndexOf(PriorityBools, prop.Name) >= 0)
                {
                    Diag.Log("bubble: activated via " + prop.Name + " -- toggling console.");
                    string msg = ConsoleOpener.Toggle();
                    if (msg != null) Mod.Log.Msg(msg);
                    try { var close = action.GetType().GetMethod("Close", f); if (close != null) close.Invoke(action, null); } catch { }
                    _lastState.Clear();
                }
            }
        }

        internal static void Tick()
        {
            if (!_installed || !Mod.BubbleEnabled) return;
            try { PollActivation(); } catch (Exception ex) { Diag.FileOnly("poll: " + ex.Message); }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            var local = PlayerController.Current;
            if (local == null || local.NetworkPlayer == null) return;
            foreach (var value in Resources.FindObjectsOfTypeAll(_menuType))
            {
                var menu = value as Component;
                if (menu == null || !menu.gameObject.activeInHierarchy) continue;
                var controller = GetMember(menu, "Controller") as Component;
                var pc = controller == null ? null : GetMember(controller, "PlayerController") as PlayerController;
                if (pc == null || pc != local) continue;
                try { Ensure(menu, local); }
                catch (Exception ex) { if (!_warned) { Mod.Log.Warning("bubble: " + ex.Message); _warned = true; } }
            }
        }

        private static void Ensure(Component menu, PlayerController local)
        {
            var menus = GetMember(menu, "quickAccessMenus") as Array;
            if (menus == null || menus.Length == 0) return;
            foreach (var a in menus)
            { var o = a as UnityEngine.Object; if (o != null && o.GetInstanceID() == _ourActionId) return; }
            // Reuse the one clone if the controller rebuilt its menu list.
            if (_clone != null)
            {
                var existing = _clone as UnityEngine.Object;
                SetMember(existing, "DisplayName", "Building");
                AppendTo(menu, menus, existing);
                return;
            }
            var template = menus.GetValue(0) as UnityEngine.Object;
            if (template == null) return;
            var clone = UnityEngine.Object.Instantiate(template);
            _clone = clone;
            clone.name = "ATT Building Console";
            try { SetMember(clone, "displayName", "Building"); } catch { } // DisplayName is get-only over this field
            var init = clone.GetType().GetMethod("InitializeForPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (init != null) init.Invoke(clone, new object[] { local.NetworkPlayer, false });
            AppendTo(menu, menus, clone);
            _ourActionId = clone.GetInstanceID();
            Mod.Log.Msg("bubble: 'ATT Building Console' added to the wrist menu; reopen it and pick Building.");
        }


        private static void AppendTo(Component menu, Array menus, UnityEngine.Object action)
        {
            var updated = Array.CreateInstance(_actionType, menus.Length + 1);
            for (int i = 0; i < menus.Length; i++) updated.SetValue(menus.GetValue(i), i);
            updated.SetValue(action, menus.Length);
            SetMember(menu, "quickAccessMenus", updated);
        }

        private static object GetMember(object target, string name)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = target.GetType().GetProperty(name, f); if (p != null) return p.GetValue(target, null);
            var field = target.GetType().GetField(name, f); return field == null ? null : field.GetValue(target);
        }
        private static void SetMember(object target, string name, object value)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = target.GetType().GetProperty(name, f); if (p != null && p.CanWrite) { p.SetValue(target, value, null); return; }
            var field = target.GetType().GetField(name, f); if (field != null) field.SetValue(target, value);
        }
    }
}
