using System;
using System.IO;

namespace ATTBuilding
{
    // Diagnostic log: Melon log + an append-only debug.log in the mod's data
    // folder, flushed per line. Native crashes leave no managed exception,
    // so the last lines of debug.log (plus crumb.txt) are the only evidence;
    // that is deliberate, and why milestones -- not per-frame work -- log.
    internal static class Diag
    {
        private static string _path;
        internal static bool Verbose = true;

        internal static void Init(string dataDir, string platform)
        {
            try
            {
                _path = Path.Combine(dataDir, "debug.log");
                File.AppendAllText(_path, Environment.NewLine + "=== ATTBuilding session (" + platform + ") " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine);
            }
            catch { }
        }

        // Milestone: Melon log + file.
        internal static void Log(string message)
        {
            try { if (Mod.Log != null) Mod.Log.Msg("[diag] " + message); } catch { }
            FileOnly(message);
        }

        // File only (heartbeats, API dumps, state changes we may need later).
        internal static void FileOnly(string message)
        {
            if (_path == null) return;
            try { File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine); } catch { }
        }
    }
}
