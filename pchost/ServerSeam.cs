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

        [HarmonyPriority(800)]
        [HarmonyPostfix]
        private static void Postfix(bool isServer)
        {
            Diag.FileOnly("server seam: NetworkScene.FirstInitialize(isServer=" + isServer + ") once=" + _once + " registered=" + ServerCommands.Registered);
            if (!isServer || _once) return;
            _once = true;
            Diag.Log("server seam: FirstInitialize(isServer=true) -- registering 'build' module now.");
            try { ServerCommands.Register("NetworkScene.FirstInitialize"); }
            catch (Exception ex) { Mod.Log.Error("server seam: " + ex); Diag.FileOnly("server seam exception: " + ex); }
        }
    }
}
