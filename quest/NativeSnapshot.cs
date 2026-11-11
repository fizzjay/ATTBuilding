using System;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ATTBuildingQuest
{
    // Snapshot an Il2CppSystem collection into a managed array before looping
    // (ArmorUpdate's NativeEnumerable pattern, quest/NativeEnumerable.cs).
    // Il2CppSystem.List<T>/HashSet<T> enumerators do NOT satisfy C#'s foreach
    // pattern (no accessible MoveNext/Current on the generated bridge), so
    // `foreach (var x in PrefabManager.AllPrefabs)` fails to compile on Quest
    // even though the identical line is fine on Mono. Native collections do
    // expose ICollection<T>.CopyTo -- copy through that, then loop the array.
    internal static class NativeSnapshot
    {
        internal static T[] Items<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> source)
            where T : Il2CppObjectBase
        {
            if (source == null) return new T[0];
            var collection = source.TryCast<Il2CppSystem.Collections.Generic.ICollection<T>>();
            if (collection == null) throw new InvalidOperationException("Native collection does not support snapshot CopyTo");
            var copy = new Il2CppReferenceArray<T>(collection.Count);
            collection.CopyTo(copy, 0);
            var result = new T[copy.Length];
            for (int i = 0; i < result.Length; i++) result[i] = copy[i];
            return result;
        }
    }
}
