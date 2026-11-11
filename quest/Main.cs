
using System;
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
                Settings.Load();
                var harmony = new HarmonyLib.Harmony("attbuilding");
                Mod.Log.Msg(ConsoleHook.Apply(harmony));
                Mod.Log.Msg(Bubble.Apply(harmony));
SERVER_IATTBuildingQuestTALL                _running = true;
                Mod.Log.Msg("ATTBuilding " + Mod.Version + " started (Quest IL2CPP). Open the wrist menu and pick the 'ATT Building Console' bubble, then: build help");
            }
            catch (Exception ex) { Mod.Log.Error("failed to start, staying off: " + ex); }
        }
        public override void OnUpdate()
        {
            if (!_running) return;
            try { Bubble.Tick(); } catch (Exception e) { Mod.LogOnce("bubble: " + e.Message); }
            try { BuildMode.Tick(); } catch (Exception e) { Mod.LogOnce("buildmode: " + e.Message); }
            try { ConsoleHook.TickTimeout(); } catch { }
        }
    }

    internal static class Mod
    {
        internal const string Version = "0.1.0";
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
