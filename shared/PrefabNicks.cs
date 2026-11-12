using System;
using System.Collections.Generic;
using System.IO;

namespace ATTBuilding
{
    // Personal prefab nicknames: short names YOU save for prefabs so that
    //   build nick anvil anv
    // makes
    //   spawn anv
    // spawn an Anvil. Stored on YOUR machine (client-side) in
    // UserData/ATTBuilding/nicks.txt as
    //   nickname=prefabHash|Prefab Name
    // so they survive restarts, can be hand-edited, and work everywhere --
    // spawn already resolves a name to a hash locally before anything is
    // sent to a server, so a nickname is just one more resolution step.
    // nickname(lowercase) -> [hash, prefabName]
    public static class PrefabNicks
    {
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>();
        private static string _filePath;
        private static bool _loaded;

        // Platform code wires this to Diag.Log at startup; may stay null.
        public static Action<string> Log;

        public static void Init(string dataDir)
        {
            try { _filePath = Path.Combine(dataDir, "nicks.txt"); } catch { _filePath = null; }
            Load();
        }

        public static int Count { get { EnsureLoaded(); return Map.Count; } }

        public static bool Validate(string nickname, out string error)
        {
            error = null;
            string nick = (nickname ?? "").Trim();
            if (nick.Length < 2 || nick.Length > 24) { error = "Nickname '" + nickname + "' is invalid: use 2-24 characters, one word (letters, digits, _ - .)."; return false; }
            bool allDigits = true;
            foreach (char c in nick)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.') { error = "Nickname '" + nickname + "' is invalid: one word, letters/digits/_ - . only."; return false; }
                if (!char.IsDigit(c)) allDigits = false;
            }
            if (allDigits) { error = "Nickname '" + nickname + "' is invalid: a pure number would clash with prefab IDs."; return false; }
            return true;
        }

        public static bool TryGet(string nickname, out string hash, out string prefabName)
        {
            hash = null; prefabName = null;
            EnsureLoaded();
            string[] entry;
            if (nickname == null || !Map.TryGetValue(nickname.Trim().ToLowerInvariant(), out entry) || entry == null || entry.Length < 2) return false;
            hash = entry[0]; prefabName = entry[1];
            return true;
        }

        public static void Set(string nickname, string hash, string prefabName)
        {
            EnsureLoaded();
            Map[nickname.Trim().ToLowerInvariant()] = new[] { hash ?? "", prefabName ?? "" };
            Save();
        }

        public static bool Delete(string nickname, out string prefabName)
        {
            prefabName = null;
            EnsureLoaded();
            string key = (nickname ?? "").Trim().ToLowerInvariant();
            string[] entry;
            if (!Map.TryGetValue(key, out entry)) return false;
            prefabName = entry != null && entry.Length > 1 ? entry[1] : "";
            Map.Remove(key);
            Save();
            return true;
        }

        public static List<string> DescribeAll()
        {
            EnsureLoaded();
            var keys = new List<string>(Map.Keys); keys.Sort();
            var lines = new List<string>();
            foreach (string k in keys) { var v = Map[k]; lines.Add(k + " = " + v[1] + " [" + v[0] + "]"); }
            return lines;
        }

        private static void EnsureLoaded() { if (!_loaded) Load(); }

        private static void Load()
        {
            _loaded = true;
            Map.Clear();
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                if (!File.Exists(_filePath)) return;
                foreach (string line in File.ReadAllLines(_filePath))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string nick = s.Substring(0, eq).Trim().ToLowerInvariant();
                    string rest = s.Substring(eq + 1);
                    int bar = rest.IndexOf('|');
                    string hash = (bar >= 0 ? rest.Substring(0, bar) : rest).Trim();
                    string name = bar >= 0 ? rest.Substring(bar + 1).Trim() : "";
                    if (nick.Length > 0 && hash.Length > 0) Map[nick] = new[] { hash, name };
                }
                if (Log != null) Log("nicks: loaded " + Map.Count + " prefab nickname(s) from " + _filePath);
            }
            catch (Exception ex) { if (Log != null) Log("nicks: load failed: " + ex.Message); }
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                var lines = new List<string> { "# ATTBuilding prefab nicknames: nickname=prefabHash|Prefab Name (edit freely; delete a line to remove)" };
                var keys = new List<string>(Map.Keys); keys.Sort();
                foreach (string k in keys) { var v = Map[k]; lines.Add(k + "=" + v[0] + "|" + v[1]); }
                File.WriteAllLines(_filePath, lines.ToArray());
                if (Log != null) Log("nicks: saved " + Map.Count + " prefab nickname(s) to " + _filePath);
            }
            catch (Exception ex) { if (Log != null) Log("nicks: save FAILED: " + ex); }
        }
    }
}
