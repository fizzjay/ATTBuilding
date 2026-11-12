
using System;
using ATTBuilding; // shared/ types (BuildState, CommandParser, TrackedRegistry, VectorSnap): sibling namespace, not visible unqualified without this
using System.Collections.Generic;
using Il2Cpp;
using Il2CppAlta.Networking;
using Il2CppAlta.Networking.Scripts.Player;
using Il2CppATT.Character.QuickAccessMenu;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ATTBuildingQuest
{
    // Prefab lookup by name against PrefabManager.AllPrefabs (ArmorRestored's
    // Loot/Registry pattern), spawning in front of the player on the ground
    // (ArmorRestored's armor-stand raycast), and -- when this client has no
    // spawn authority -- routing the request to a server running the PC build
    // (the ShardClient AdminConsole / CommandSync pattern). The server side
    // lives in pchost/ServerCommands.cs:  build spawn <player> <prefabHash>
    internal static class Spawner
    {
        internal static NetworkPrefab Resolve(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) { error = "Name a prefab, e.g. build spawn wooden plank"; return null; }
            string wanted = name.Trim();
            NetworkPrefab exact = null, partial = null; int partials = 0;
            foreach (NetworkPrefab prefab in NativeSnapshot.Items(PrefabManager.AllPrefabs)) // Il2Cpp collections have no C#-foreach-able enumerator; snapshot first
            {
                if (prefab == null) continue;
                string candidate = Clean(prefab.name);
                if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase)
                    || prefab.Hash.ToString() == wanted) { exact = prefab; break; }
                if (candidate.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) { partial = prefab; partials++; }
            }
            if (exact != null) return exact;
            if (partials == 1) return partial;
            if (partials > 1) { error = "'" + wanted + "' matches " + partials + " prefabs. Be more specific (see: build list " + wanted + ")."; return null; }
            error = "No prefab matching '" + wanted + "'. See: build list";
            return null;
        }

        internal static void ListMatching(string filter, Action<string> print)
        {
            var names = new List<string>();
            foreach (NetworkPrefab prefab in NativeSnapshot.Items(PrefabManager.AllPrefabs)) // Il2Cpp collections have no C#-foreach-able enumerator; snapshot first
            {
                if (prefab == null) continue;
                string n = Clean(prefab.name);
                if (string.IsNullOrEmpty(filter) || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || prefab.Hash.ToString().IndexOf(filter) >= 0) names.Add(n + "  [" + prefab.Hash + "]");
            }
            names.Sort();
            if (names.Count == 0) { print("No prefabs matching '" + filter + "'."); return; }
            for (int i = 0; i < names.Count && i < 12; i++) print(names[i]);
            if (names.Count > 12) print("... and " + (names.Count - 12) + " more (narrow it: build list <text>).");
        }

        internal static void SpawnByName(string name, int count, Action<string> print)
        {
            string error;
            NetworkPrefab prefab = Resolve(name, out error);
            if (prefab == null) { print(error); return; }
            if (!HasAuthority())
            {
                // Client on someone else's server: ask that server (if it runs
                // the PC build) to spawn it authoritatively by hash.
                string player = LocalPlayerName();
                if (player == null) { print("Not spawned yet."); return; }
                if (NetRoute.Send("build spawn " + player + " " + prefab.Hash, print))
                { print("Asked the server to spawn " + Clean(prefab.name) + " x" + count + " ..."); ConsoleHook.BeginWait(print); }
                else print("This client cannot spawn here (not the host/server) and no server command channel is open. Play local/host, or the server needs ATTBuilding (PC build).");
                return;
            }
            Vector3 at; Quaternion rot;
            if (!Placement(out at, out rot)) { print("No ground in front of you to spawn on."); return; }
            int done = SpawnAuthoritative(prefab, at, rot, count);
            print(done > 0
                ? "Spawned " + Clean(prefab.name) + " x" + done + (BuildState.BuildMode ? " (build mode: grab it to move it)" : " (build spawn is tracked; 'build mode on' lets you grab it)")
                : "Spawn failed (server refused or prefab cannot be spawned here).");
        }

        // Authoritative spawn + build-mode registration. Also used by the PC
        // server command. Returns how many actually spawned.
        internal static int SpawnAuthoritative(NetworkPrefab prefab, Vector3 at, Quaternion rot, int count)
        {
            int done = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    NetworkEntity entity = SpawnHelper.Spawn(prefab, SpawnData.Default, null,
                        at + Vector3.up * (0.05f * i), rot, null);
                    if (entity == null) continue;
                    done++;
                    Track(entity);
                }
                catch (Exception ex) { Mod.LogOnce("spawn: " + ex.Message); }
            }
            return done;
        }

        internal static void Track(NetworkEntity entity)
        {
            if (entity == null) return;
            TrackedRegistry.Add(entity.gameObject.GetInstanceID());
            var pickup = entity.gameObject.GetComponentInChildren<Pickup>();
            if (pickup != null) TrackedRegistry.Add(pickup.gameObject.GetInstanceID());
            if (BuildState.BuildMode) BuildMode.EnsureGrabbable(entity.gameObject);
        }

        internal static int ClearSpawned()
        {
            int removed = 0;
            foreach (int id in TrackedRegistry.Snapshot())
            {
                var go = FindByInstanceId(id);
                if (go == null) continue;
                try
                {
                    var prefab = go.GetComponent<NetworkPrefab>();
                    var entity = prefab == null ? null : prefab.Entity;
                    if (HasAuthority() && entity != null && entity.Spawner != null) entity.Spawner.Destroy(entity, false);
                    else UnityEngine.Object.Destroy(go);
                    removed++;
                }
                catch (Exception ex) { Mod.LogOnce("clear: " + ex.Message); }
            }
            TrackedRegistry.Clear();
            return removed;
        }

        internal static bool HasAuthority()
        {
            try { return NetworkSceneManager.IsServer || NetworkSceneManager.IsLocalTest; }
            catch { return false; }
        }

        internal static GameObject FindByInstanceId(int instanceId)
        {
            foreach (var prefab in UnityEngine.Object.FindObjectsOfType<NetworkPrefab>())
            {
                if (prefab == null) continue;
                if (prefab.gameObject.GetInstanceID() == instanceId) return prefab.gameObject;
                var pickup = prefab.gameObject.GetComponentInChildren<Pickup>();
                if (pickup != null && pickup.gameObject.GetInstanceID() == instanceId) return prefab.gameObject;
            }
            return null;
        }

        // Ground placement in front of the local player: raycast down onto the
        // nearest solid, non-player surface (ArmorRestored armor-stand code).
        internal static bool Placement(out Vector3 at, out Quaternion rot)
        {
            at = Vector3.zero; rot = Quaternion.identity;
            var player = PlayerController.Current;
            var head = player == null ? null : player.Head;
            if (head == null) return false;
            Vector3 forward = head.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            rot = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 origin = head.position + forward * 1.2f;
            RaycastHit[] hits = Physics.RaycastAll(origin + Vector3.up, Vector3.down, 12f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (player.gameObject != null && hit.collider.transform.IsChildOf(player.transform)) continue;
                at = hit.point; return true;
            }
            at = origin; return true; // mid-air spawn beats no spawn (caves/void)
        }

        internal static string LocalPlayerName()
        {
            try { var p = Player.Current == null ? null : Player.Current.TryCast<Player>(); return p == null || p.UserInfo == null ? null : p.UserInfo.Username; }
            catch { return null; }
        }

        internal static string Clean(string name)
        {
            if (name == null) return "";
            int i = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return (i >= 0 ? name.Substring(0, i) : name).Trim();
        }
    }

    // Routes a line to the server console channel (CommandSync), exactly the
    // ShardClient AdminConsole seam. Isolated here: if a game update moves
    // CommandSync, this is the only file to fix.
    internal static class NetRoute
    {
        internal static bool Send(string command, Action<string> print)
        {
            try
            {
                var sync = CommandSync.Instance;
                if (sync == null) return false;
                Diag.Log("route: sending to server: " + command);
                sync.RouteCommand(command, Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string, string>>(new System.Action<string, string>(OnReply)));
                return true;
            }
            catch (Exception ex) { Mod.LogOnce("route: " + ex.Message); return false; }
        }
        private static void OnReply(string name, string result)
        {
            Diag.Log("route: server replied (" + (name ?? "?") + "): " + (string.IsNullOrEmpty(result) ? "(empty)" : result));
            ConsoleHook.NoteServerReply(result);
        }
    }
}
