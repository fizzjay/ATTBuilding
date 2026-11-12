using System;
using Alta.Networking;
using HarmonyLib;

namespace ATTBuilding
{
    // Server-side registration seam, copied from ArmorRestored's ServerSeam:
    // when the network scene first initialises AS THE SERVER, collect this
    // assembly's Alta.Console modules so routed client commands exist.
    // (MelonLoader applies [HarmonyPatch] classes in a melon assembly.)
    [HarmonyPatch(typeof(NetworkScene), "FirstInitialize", new Type[] { typeof(bool) })]
    internal static class ServerSeam
    {
        private static bool _once;

        [HarmonyPostfix]
        private static void Postfix(bool isServer)
        {
            if (!isServer || _once) return;
            _once = true;
            try { ServerCommands.Register(); }
            catch (Exception ex) { Mod.Log.Error("server seam: " + ex.Message); }
        }
    }
}
