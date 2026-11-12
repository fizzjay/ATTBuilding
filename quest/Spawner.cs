
using System;
using ATTBuilding; // shared/ types (BuildState, CommandParser, TrackedRegistry, VectorSnap): sibling namespace, not visible unqualified without this
using System.Collections.Generic;
using Il2Cpp;
using Il2CppAlta.Chunks;
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
            // Your saved prefab nicknames (build nick anvil anv -> spawn anv).
            // Checked after real names/IDs but before substring matching, so a
            // nickname always wins over ambiguous partial matches.
            string nickHash, nickName;
            if (PrefabNicks.TryGet(wanted, out nickHash, out nickName))
            {
                foreach (NetworkPrefab prefab in NativeSnapshot.Items(PrefabManager.AllPrefabs)) { if (prefab != null && prefab.Hash.ToString() == nickHash) return prefab; }
                error = "Your nickname '" + wanted + "' points at '" + nickName + "' [" + nickHash + "] but that prefab isn't loaded here. Remove it: build del nick " + wanted;
                return null;
            }
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
            if (prefab == null) { Diag.Log("spawn: resolve failed for '" + name + "': " + error); print(error); return; }
            Diag.Log("spawn: resolved '" + name + "' -> '" + Clean(prefab.name) + "' hash=" + prefab.Hash + " count=" + count
                + " authority[" + AuthorityDescription() + "] localPlayer[" + LocalPlayerDescription() + "]");
            if (!HasAuthority())
            {
                // Client on someone else's server: ask that server (if it runs
                // the PC build) to spawn it authoritatively by hash.
                string player = LocalPlayerToken();
                if (player == null) { Diag.Log("spawn: cannot route: local player token unavailable (not spawned in yet)."); print("Not spawned yet."); return; }
                string command = "build spawn " + player + " " + prefab.Hash;
                if (BuildNet.SendSpawn(prefab.Hash, count))
                { print("Asked the server (direct ATTBuilding channel) to spawn " + Clean(prefab.name) + " x" + count + " ..."); ConsoleHook.BeginWait(print, command + " [direct]"); }
                else if (NetRoute.Send(command, print))
                { print("Asked the server (Alta console route) to spawn " + Clean(prefab.name) + " x" + count + " ..."); ConsoleHook.BeginWait(print, command); }
                else print("This client cannot spawn here (not the host/server) and no server channel is open. Play local/host, or the server needs ATTBuilding (PC build).");
                return;
            }
            Vector3 at; Quaternion rot;
            if (!Placement(out at, out rot)) { Diag.Log("spawn: local authority but placement failed (player head missing)."); print("No ground in front of you to spawn on."); return; }
            Diag.Log("spawn: local authority: placement at " + at + " facing " + rot.eulerAngles);
            int done = SpawnAuthoritative(prefab, at, rot, count);
            Diag.Log("spawn: local authority result: " + done + "/" + count + " spawned.");
            print(done > 0
                ? "Spawned " + Clean(prefab.name) + " x" + done + (BuildState.BuildMode ? " (build mode: grab it to move it)" : " (build spawn is tracked; 'build mode on' lets you grab it)")
                : "Spawn failed (server refused or prefab cannot be spawned here).");
        }

        internal static void SetNick(string prefabRef, string nickname, Action<string> print)
        {
            string error;
            if (!PrefabNicks.Validate(nickname, out error)) { print(error); return; }
            // A nickname may not shadow a real prefab name: exact names win in
            // Resolve, so the nickname would never fire.
            NetworkPrefab shadow = Resolve(nickname, out error);
            if (shadow != null && string.Equals(Clean(shadow.name), nickname.Trim(), StringComparison.OrdinalIgnoreCase))
            { print("'" + nickname + "' is already a real prefab name -- no nickname needed (spawn " + nickname + " works as-is)."); return; }
            NetworkPrefab prefab = Resolve(prefabRef, out error);
            if (prefab == null) { print("Can't save nickname: " + error); return; }
            string oldHash, oldName; bool existed = PrefabNicks.TryGet(nickname, out oldHash, out oldName);
            PrefabNicks.Set(nickname, prefab.Hash.ToString(), Clean(prefab.name));
            Diag.Log("nicks: saved '" + nickname + "' -> '" + Clean(prefab.name) + "' hash=" + prefab.Hash + (existed ? " (replaced '" + oldName + "')" : ""));
            print("Nickname saved: spawn " + nickname + " -> " + Clean(prefab.name) + (existed ? " (replaced " + oldName + ")" : "") + ". Saved on this machine in nicks.txt.");
        }

        internal static void DeleteNick(string nickname, Action<string> print)
        {
            string prefabName;
            if (PrefabNicks.Delete(nickname, out prefabName))
            {
                Diag.Log("nicks: deleted '" + nickname + "' (was '" + prefabName + "')");
                print("Nickname deleted: '" + nickname + "' (was " + prefabName + ").");
            }
            else print("No nickname '" + nickname + "' saved. See: build nicks");
        }

        internal static void ListNicks(Action<string> print)
        {
            var lines = PrefabNicks.DescribeAll();
            if (lines.Count == 0) { print("No prefab nicknames saved yet. Save one: build nick <prefab> <nickname>  e.g. build nick anvil anv"); return; }
            print("Your prefab nicknames (" + lines.Count + "):");
            foreach (string line in lines) print("  " + line);
        }

        internal static void PingServer(Action<string> print)
        {
            Diag.Log("ping: requested; localPlayer[" + LocalPlayerDescription() + "] authority[" + AuthorityDescription() + "]");
            if (HasAuthority()) { print("You are the server/local authority on this peer: ATTBuilding " + Mod.Version + " is loaded here, so spawning is local (no route needed)."); return; }
            string player = LocalPlayerToken();
            if (player == null) { print("Not spawned yet."); return; }
            string command = "build ping " + player;
            if (BuildNet.SendPing()) { print("Pinging the server for ATTBuilding on the direct channel (this client is " + Mod.Version + ") ..."); ConsoleHook.BeginWait(print, command + " [direct]"); }
            else if (NetRoute.Send(command, print)) { print("Pinging the server for ATTBuilding via the Alta console route (this client is " + Mod.Version + ") ..."); ConsoleHook.BeginWait(print, command); }
            else print("Could not ping: no server channel is open here (not connected, or the connection was not captured).");
        }

        // Authoritative spawn + build-mode registration. Also used by the PC
        // server command. Returns how many actually spawned.
        internal static int SpawnAuthoritative(NetworkPrefab prefab, Vector3 at, Quaternion rot, int count)
        {
            int done = 0;
            Diag.Log("spawn: SpawnAuthoritative prefab='" + Clean(prefab.name) + "' hash=" + prefab.Hash + " count=" + count + " at=" + at + " authority[" + AuthorityDescription() + "]");
            for (int i = 0; i < count; i++)
            {
                try
                {
                    Vector3 thisAt = at + Vector3.up * (0.05f * i);
                    Diag.FileOnly("spawn: attempt " + (i + 1) + "/" + count + " at " + thisAt);
                    NetworkEntity entity = SpawnHelper.Spawn(prefab, SpawnData.Default, null, thisAt, rot, null);
                    if (entity == null) { Diag.Log("spawn: SpawnHelper.Spawn returned NULL for '" + Clean(prefab.name) + "' (prefab refused here)."); continue; }
                    done++;
                    try { var chunk = LocationChunkHelper.GetBestContaining(thisAt, null); if (chunk != null) entity.Chunk = chunk; } catch (Exception cex) { Diag.FileOnly("spawn: chunk assign failed: " + cex.Message); }
                    Diag.Log("spawn: SpawnHelper created '" + entity.gameObject.name + "' instance=" + entity.gameObject.GetInstanceID());
                    Track(entity);
                }
                catch (Exception ex) { Diag.Log("spawn: SpawnHelper.Spawn EXCEPTION: " + ex); Mod.LogOnce("spawn: " + ex.Message); }
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

        // What we put in the routed command for "who wants this": the numeric
        // Identifier when there is one (usernames can contain spaces, which
        // the server console parser would split into extra arguments),
        // else the username. The server accepts either (ArmorRestored's
        // Players.Find does the same).
        internal static string LocalPlayerToken()
        {
            try
            {
                var p = Player.Current == null ? null : Player.Current.TryCast<Player>();
                if (p == null || p.UserInfo == null) return null;
                return p.UserInfo.Identifier != 0 ? p.UserInfo.Identifier.ToString() : p.UserInfo.Username;
            }
            catch { return LocalPlayerName(); }
        }

        internal static string LocalPlayerDescription()
        {
            try
            {
                var p = Player.Current == null ? null : Player.Current.TryCast<Player>();
                if (p == null) return "Player.Current=null";
                if (p.UserInfo == null) return "UserInfo=null";
                return "username='" + p.UserInfo.Username + "' identifier=" + p.UserInfo.Identifier + " token='" + LocalPlayerToken() + "'";
            }
            catch (Exception ex) { return "unavailable: " + ex.Message; }
        }

        internal static string AuthorityDescription()
        {
            bool isServer = false, isLocal = false; string serverError = null, localError = null;
            try { isServer = NetworkSceneManager.IsServer; } catch (Exception ex) { serverError = ex.Message; }
            try { isLocal = NetworkSceneManager.IsLocalTest; } catch (Exception ex) { localError = ex.Message; }
            return "IsServer=" + isServer + (serverError != null ? "(err:" + serverError + ")" : "")
                + " IsLocalTest=" + isLocal + (localError != null ? "(err:" + localError + ")" : "");
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
                Diag.Log("route: CommandSync.Instance=" + (sync == null ? "NULL (no command channel to a server)" : sync.GetType().FullName) + "; sending '" + command + "'");
                if (sync == null) { Diag.Log("route: SEND ABORTED: CommandSync.Instance is null -- this client has no server command channel (not connected to a server?)."); return false; }
                sync.RouteCommand(command, Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string, string>>(new System.Action<string, string>(OnReply)));
                Diag.Log("route: RouteCommand('" + command + "') returned without throwing; callback pending.");
                return true;
            }
            catch (Exception ex) { Diag.Log("route: SEND FAILED for '" + command + "': " + ex); Mod.LogOnce("route: " + ex.Message); return false; }
        }
        private static void OnReply(string name, string result)
        {
            Diag.Log("route: CALLBACK from server (name='" + (name ?? "(null)") + "'): '" + (string.IsNullOrEmpty(result) ? "(empty)" : result) + "'");
            ConsoleHook.NoteServerReply(result);
        }
    }
}
