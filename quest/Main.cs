
using System;
using ATTBuilding; // shared/ types (BuildState, CommandParser, TrackedRegistry, VectorSnap): sibling namespace, not visible unqualified without this
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using System.IO;

namespace ATTBuildingQuest
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
                Crumb.Init(Mod.DataDir);
                Settings.Load();
                Diag.Init(Mod.DataDir, "Quest IL2CPP");
                PrefabNicks.Init(Mod.DataDir); PrefabNicks.Log = Diag.Log;
                Diag.Log("init: bubble " + (Mod.BubbleEnabled ? "on" : "OFF (pref)") + ", console " + (Mod.ConsoleEnabled ? "on" : "OFF (pref)"));
                var harmony = new HarmonyLib.Harmony("attbuilding");
                if (Mod.ConsoleEnabled) Mod.Log.Msg(ConsoleHook.Apply(harmony)); else Mod.Log.Msg("console: OFF (pref) -- hooks skipped.");
                if (Mod.BubbleEnabled) Mod.Log.Msg(Bubble.Apply(harmony)); else Mod.Log.Msg("bubble: OFF (pref) -- quick-access bubble skipped. Set BubbleEnabled on and restart to bisect a crash.");
                Mod.Log.Msg(BuildNet.Install(harmony));
                try
                {
                    // Only public TabletUILib API here: Patch/Tick/TabletWheel are
                    // internal to the TabletUILib DLL -- its own MelonMod entrypoint
                    // does that wiring, so the DLL must also sit in the Mods folder.
                    Builder.RegisterTablet();
                    Mod.Log.Msg("tablet: Prefabulizer registered with TabletUILib (wrist wheel -> Prefabulizer summons the build tablet).");
                }
                catch (Exception tex) { Diag.Log("tablet: registration FAILED (is the TabletUILib mod DLL in Mods?): " + tex); }

                _running = true;
                Mod.Log.Msg("ATTBuilding " + Mod.Version + " started (Quest IL2CPP). Wrist wheel: 'ATT Building Console' bubble for the console, 'Prefabulizer' bubble for the build tablet. No grabbing -- point, trigger, build.");
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
            try { Bubble.Tick(); } catch (Exception e) { Mod.LogOnce("bubble: " + e.Message); }
            try { Builder.Tick(); } catch (Exception e) { Mod.LogOnce("builder: " + e.Message); }
            try { Spawner.TickRemoteTracking(); } catch { }
            try { ConsoleHook.TickTimeout(); } catch { }
        }
    }

    internal static class Mod
    {
        internal const string Version = "0.5.0";
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
