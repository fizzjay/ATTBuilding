using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ATTBuilding
{
    public sealed class SpawnRecord
    {
        public uint EntityId;
        public string Hash = "";
        public string Name = "";
        public float Px, Py, Pz;
        public float Rx, Ry, Rz, Rw = 1f;
        public float Sx = 1f, Sy = 1f, Sz = 1f;
    }

    // The build memory: every prefab this mod spawned, written to
    // spawned-ledger.txt in the mod data dir, keyed by network entity id.
    // After leaving and rejoining a server, the platform Spawner recognises
    // (re-adopts) pieces from previous sessions when they stream in, so the
    // Prefabulizer can edit them again. Pure data + file IO (the
    // PrefabNicks pattern), identical on PC and Quest.
    public static class SpawnLedger
    {
        private const int MaxRecords = 400;
        private static readonly List<SpawnRecord> _records = new List<SpawnRecord>();
        private static string _path;
        private static bool _dirty;

        public static int Count { get { return _records.Count; } }
        public static Action<string> Log;

        public static void Init(string dataDir)
        {
            _records.Clear(); _dirty = false;
            try
            {
                _path = Path.Combine(dataDir, "spawned-ledger.txt");
                if (!File.Exists(_path)) return;
                foreach (string line in File.ReadAllLines(_path))
                {
                    var r = Parse(line);
                    if (r != null && Find(r.EntityId) == null) _records.Add(r);
                }
                if (Log != null && _records.Count > 0) Log("ledger: remembered " + _records.Count + " piece(s) from previous sessions (they become editable again when you get near them).");
            }
            catch (Exception ex) { if (Log != null) Log("ledger: load failed: " + ex.Message); }
        }

        public static SpawnRecord Find(uint entityId)
        {
            foreach (var r in _records) if (r.EntityId == entityId) return r;
            return null;
        }

        public static SpawnRecord[] Snapshot() { return _records.ToArray(); }

        public static void Upsert(SpawnRecord record)
        {
            if (record == null || record.EntityId == 0) return;
            var old = Find(record.EntityId);
            if (old != null) _records.Remove(old);
            _records.Add(record);
            while (_records.Count > MaxRecords) _records.RemoveAt(0);
            _dirty = true;
        }

        public static bool Remove(uint entityId)
        {
            var old = Find(entityId);
            if (old == null) return false;
            _records.Remove(old); _dirty = true; return true;
        }

        public static void Flush()
        {
            if (!_dirty || _path == null) return;
            try
            {
                var lines = new List<string>();
                foreach (var r in _records) lines.Add(Format(r));
                File.WriteAllLines(_path, lines.ToArray());
                _dirty = false;
            }
            catch (Exception ex) { if (Log != null) Log("ledger: save failed: " + ex.Message); }
        }

        private static string Format(SpawnRecord r)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|", new[]
            {
                "1", r.EntityId.ToString(c), r.Hash ?? "", Sanitize(r.Name),
                r.Px.ToString("R", c), r.Py.ToString("R", c), r.Pz.ToString("R", c),
                r.Rx.ToString("R", c), r.Ry.ToString("R", c), r.Rz.ToString("R", c), r.Rw.ToString("R", c),
                r.Sx.ToString("R", c), r.Sy.ToString("R", c), r.Sz.ToString("R", c),
            });
        }

        private static SpawnRecord Parse(string line)
        {
            try
            {
                if (string.IsNullOrEmpty(line)) return null;
                var p = line.Split('|');
                if (p.Length < 14 || p[0] != "1") return null;
                var c = CultureInfo.InvariantCulture;
                var r = new SpawnRecord();
                r.EntityId = uint.Parse(p[1], c);
                if (r.EntityId == 0) return null;
                r.Hash = p[2]; r.Name = p[3];
                r.Px = float.Parse(p[4], c); r.Py = float.Parse(p[5], c); r.Pz = float.Parse(p[6], c);
                r.Rx = float.Parse(p[7], c); r.Ry = float.Parse(p[8], c); r.Rz = float.Parse(p[9], c); r.Rw = float.Parse(p[10], c);
                r.Sx = float.Parse(p[11], c); r.Sy = float.Parse(p[12], c); r.Sz = float.Parse(p[13], c);
                return r;
            }
            catch { return null; }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
