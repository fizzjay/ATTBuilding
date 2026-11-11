using System.Collections.Generic;

namespace ATTBuilding
{
    // Everything THIS console spawned. Only these objects become grabbable in
    // build mode -- pre-existing world items are never touched.
    // Keys are Unity gameObject instance ids (stable per live object).
    public static class TrackedRegistry
    {
        private static readonly HashSet<int> _ids = new HashSet<int>();
        public static int Count { get { return _ids.Count; } }
        public static void Add(int gameObjectInstanceId) { if (gameObjectInstanceId != 0) _ids.Add(gameObjectInstanceId); }
        public static bool Contains(int gameObjectInstanceId) { return gameObjectInstanceId != 0 && _ids.Contains(gameObjectInstanceId); }
        public static void Remove(int gameObjectInstanceId) { _ids.Remove(gameObjectInstanceId); }
        public static void Clear() { _ids.Clear(); }
        public static int[] Snapshot() { var copy = new int[_ids.Count]; _ids.CopyTo(copy); return copy; }
    }
}
