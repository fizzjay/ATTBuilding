using System;
using System.IO;

namespace ATTBuildingQuest
{
    // Breadcrumb trail for native crashes (ArmorUpdate's Crumb pattern): when
    // the game hard-closes there is no managed exception and the Melon log
    // just stops, so the last line of this file names the step we were on.
    internal static class Crumb
    {
        private static string _path;
        internal static void Init(string dataDir)
        {
            try { _path = Path.Combine(dataDir, "crumb.txt"); At("init"); } catch { }
        }
        internal static void At(string step)
        {
            if (_path == null) return;
            try { File.WriteAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + step); } catch { }
        }
    }
}
