
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
                foreach (string verb in new[] { "Run", "Open" })
                {
                    var m = AccessTools.Method(_actionType, verb);
                    if (m != null) harmony.Patch(m, prefix: new HarmonyMethod(typeof(Bubble), nameof(ActionPrefix)));
                }
                var ignored = AccessTools.PropertyGetter(_actionType, "IsIgnored");
                if (ignored != null) harmony.Patch(ignored, postfix: new HarmonyMethod(typeof(Bubble), nameof(IgnoredPostfix)));
                _installed = true;
                return "bubble: quick-access 'ATT Building Console' hook ready (reopen the wrist menu after joining).";
            }
            catch (Exception ex) { return "bubble: hook failed: " + ex.Message; }
        }

        private static bool ActionPrefix(object __instance)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj == null || obj.GetInstanceID() != _ourActionId) return true;
            string msg = ConsoleOpener.Toggle();
            if (msg != null) Mod.Log.Msg(msg);
            return false;
        }

        private static void IgnoredPostfix(object __instance, ref bool __result)
        {
            var obj = __instance as UnityEngine.Object;
            if (obj != null && _ourActionId != 0 && obj.GetInstanceID() == _ourActionId) __result = false;
        }

        internal static void Tick()
        {
            if (!_installed || Time.unscaledTime < _nextScan) return;
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
            var template = menus.GetValue(0) as UnityEngine.Object;
            if (template == null) return;
            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = "ATT Building Console";
            try { SetMember(clone, "DisplayName", "Building"); } catch { }
            var init = clone.GetType().GetMethod("InitializeForPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (init != null) init.Invoke(clone, new object[] { local.NetworkPlayer, false });
            var updated = Array.CreateInstance(_actionType, menus.Length + 1);
            for (int i = 0; i < menus.Length; i++) updated.SetValue(menus.GetValue(i), i);
            updated.SetValue(clone, menus.Length);
            SetMember(menu, "quickAccessMenus", updated);
            _ourActionId = clone.GetInstanceID();
            Mod.Log.Msg("bubble: 'ATT Building Console' added to the wrist menu; reopen it and pick Building.");
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
