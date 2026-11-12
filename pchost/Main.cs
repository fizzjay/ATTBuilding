
using System;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using System.IO;

namespace ATTBuilding
{
    public class ATTBuildingMod : MelonMod
    {
        private bool _running;
        public override void OnInitializeMelon()
        {
            Mod.Log = LoggerInstance;
            try
            {
                Mod.DataDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ATTBuilding");
                Directory.CreateDirectory(Mod.DataDir);
                Settings.Load();
                Diag.Init(Mod.DataDir, "PC Mono");
                Diag.Log("init: bubble " + (Mod.BubbleEnabled ? "on" : "OFF (pref)") + ", console " + (Mod.ConsoleEnabled ? "on" : "OFF (pref)"));
                var harmony = new HarmonyLib.Harmony("attbuilding");
                if (Mod.ConsoleEnabled) Mod.Log.Msg(ConsoleHook.Apply(harmony)); else Mod.Log.Msg("console: OFF (pref) -- hooks skipped.");
                if (Mod.BubbleEnabled) Mod.Log.Msg(Bubble.Apply(harmony)); else Mod.Log.Msg("bubble: OFF (pref) -- quick-access bubble skipped. Set BubbleEnabled on and restart to bisect a crash.");

Diag.FileOnly("server: 'build' module registers when this process becomes a server (ServerSeam FirstInitialize, else the server-state poll).");                _running = true;
                Mod.Log.Msg("ATTBuilding " + Mod.Version + " started (PC Mono). Open the wrist menu and pick the 'ATT Building Console' bubble, then: build help");
            }
            catch (Exception ex) { Mod.Log.Error("failed to start, staying off: " + ex); }
        }
        private float _nextHeartbeat;
        public override void OnUpdate()
        {
            if (!_running) return;
            if (UnityEngine.Time.unscaledTime >= _nextHeartbeat)
            {
                _nextHeartbeat = UnityEngine.Time.unscaledTime + 20f;
                Diag.FileOnly("heartbeat: bubbleAdded=" + Bubble.Added + ", buildMode=" + BuildState.BuildMode + ", tracked=" + TrackedRegistry.Count);
            }
            try { ServerCommands.TryRegisterWhenServer(); } catch { }
            try { Bubble.Tick(); } catch (Exception e) { Mod.LogOnce("bubble: " + e.Message); }
            try { BuildMode.Tick(); } catch (Exception e) { Mod.LogOnce("buildmode: " + e.Message); }
            try { ConsoleHook.TickTimeout(); } catch { }
        }
    }

    internal static class Mod
    {
        internal const string Version = "0.4.4";
        internal static bool BubbleEnabled = true;
        internal static bool ConsoleEnabled = true;
        internal static bool SuppressDummy = true;
        internal static MelonLogger.Instance Log;
        internal static string DataDir;
        private static string _onceKey;
        internal static void LogOnce(string message)
        {
            if (_onceKey == message) return; _onceKey = message;
            if (Log != null) Log.Warning(message);
        }
    }

    internal static class Settings
    {
        internal static void Load()
        {
            var cat = MelonPreferences.CreateCategory("ATTBuilding");
            BuildState.AngleStepDegrees = cat.CreateEntry("SnapAngleDegrees", 15f).Value;
            BuildState.GridMetres = cat.CreateEntry("GridMetres", 0.25f).Value;
            BuildState.Snapping = cat.CreateEntry("SnappingDefault", false).Value;
            Mod.BubbleEnabled = cat.CreateEntry("BubbleEnabled", true).Value;
            Mod.ConsoleEnabled = cat.CreateEntry("ConsoleEnabled", true).Value;
            Mod.SuppressDummy = cat.CreateEntry("SuppressDummy", true).Value;
        }
        internal static void Save()
        {
            var cat = MelonPreferences.GetCategory("ATTBuilding");
            if (cat == null) return;
            foreach (var e in cat.Entries)
            {
                if (e.Identifier == "SnapAngleDegrees") e.BoxedValue = BuildState.AngleStepDegrees;
                if (e.Identifier == "GridMetres") e.BoxedValue = BuildState.GridMetres;
                if (e.Identifier == "SnappingDefault") e.BoxedValue = BuildState.Snapping;
            }
            MelonPreferences.Save();
        }
    }
}
