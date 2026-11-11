
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
                var type = typeof(QuickAccessMenuAction);
                foreach (string verb in new[] { "Run", "Open" })
                {
                    var m = AccessTools.Method(type, verb);
                    if (m != null) harmony.Patch(m, prefix: new HarmonyMethod(typeof(Bubble), nameof(ActionPrefix)));
                }
                var ignored = AccessTools.PropertyGetter(type, "IsIgnored");
                if (ignored != null) harmony.Patch(ignored, postfix: new HarmonyMethod(typeof(Bubble), nameof(IgnoredPostfix)));
                _installed = true;
                return "bubble: quick-access 'ATT Building Console' hook ready (reopen the wrist menu after joining).";
            }
            catch (Exception ex) { return "bubble: hook failed: " + ex.Message; }
        }

        private static bool ActionPrefix(QuickAccessMenuAction __instance)
        {
            if (__instance == null || __instance.GetInstanceID() != _ourActionId) return true;
            string msg = ConsoleOpener.Toggle();
            if (msg != null) Mod.Log.Msg(msg);
            return false; // never run the cloned native action's own behaviour
        }

        private static void IgnoredPostfix(QuickAccessMenuAction __instance, ref bool __result)
        {
            if (__instance != null && _ourActionId != 0 && __instance.GetInstanceID() == _ourActionId) __result = false;
        }

        internal static void Tick()
        {
            if (!_installed || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            if (_action != null && _menu == null) { Object.Destroy(_action); _action = null; _ourActionId = 0; }
            var local = PlayerController.Current;
            if (local == null || local.NetworkPlayer == null) return;
            foreach (Object value in Resources.FindObjectsOfTypeAll(Il2CppType.Of<QuickAccessMenuController>()))
            {
                var menu = value == null ? null : value.TryCast<QuickAccessMenuController>();
                if (menu == null || !menu.gameObject.activeInHierarchy || menu.Controller == null
                    || menu.Controller.PlayerController == null
                    || menu.Controller.PlayerController.Pointer != local.Pointer) continue;
                try { Ensure(menu, local); }
                catch (Exception ex) { if (!_warned) { Mod.Log.Warning("bubble: " + ex.Message); _warned = true; } }
            }
        }

        private static void Ensure(QuickAccessMenuController menu, PlayerController local)
        {
            var menus = Menus(menu);
            if (menus == null) return;
            foreach (var a in menus) if (a != null && a.GetInstanceID() == _ourActionId) { _menu = menu; return; }
            QuickAccessMenuAction template = null;
            foreach (var a in menus) if (a != null) { template = a; break; }
            if (template == null) return;
            var clone = Object.Instantiate(template);
            clone.name = "ATT Building Console";
            clone.InitializeForPlayer(local.NetworkPlayer, false);
            var updated = new Il2CppReferenceArray<QuickAccessMenuAction>(menus.Length + 1);
            for (int i = 0; i < menus.Length; i++) updated[i] = menus[i];
            updated[menus.Length] = clone;
            SetMenus(menu, updated);
            _menu = menu; _action = clone; _ourActionId = clone.GetInstanceID();
            Mod.Log.Msg("bubble: 'ATT Building Console' added to the wrist menu; reopen it and pick Building.");
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
