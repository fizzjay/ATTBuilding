
using System;
using Alta.Console;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using UnityEngine;

namespace ATTBuilding
{
    // Server side (PC build installed on the server/host): Quest and PC
    // clients route "build spawn <player> <prefabHash>" here through
    // CommandSync, and the server -- the only peer that may authoritatively
    // SpawnHelper.Spawn for everyone -- does the actual spawn in front of
    // that player. Same [Module]/[Command] seam as ArmorRestored's
    // ArmorCommands. Without this on the server, client spawn requests get
    // the polite "no answer" timeout from the client console.
    [Module("build", "ATTBuilding: spawn and place prefabs for building")]
    public static class ServerCommands
    {
        [Command("spawn", "Spawn a prefab in front of a player: build spawn <player> <prefabHash>")]
        public static string Spawn(string player, string prefabHash)
        {
            uint hash;
            if (!uint.TryParse(prefabHash, out hash)) return "bad prefab hash '" + prefabHash + "'";
            var prefab = PrefabManager.GetPrefab(hash);
            if (prefab == null) return "no prefab with hash " + hash;
            Player target = null; string why = null;
            foreach (IPlayer p in Player.AllPlayers)
            {
                var candidate = p as Player;
                if (candidate != null && candidate.UserInfo != null
                    && string.Equals(candidate.UserInfo.Username, player, StringComparison.OrdinalIgnoreCase)) { target = candidate; break; }
            }
            if (target == null) { why = "no connected player called " + player; return why; }
            var controller = target.PlayerController;
            if (controller == null || controller.Head == null) return player + " is not spawned in";
            Vector3 forward = controller.Head.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 origin = controller.Head.position + forward * 1.2f;
            Vector3 at = origin;
            RaycastHit[] hits = Physics.RaycastAll(origin + Vector3.up, Vector3.down, 12f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (controller.gameObject != null && hit.collider.transform.IsChildOf(controller.transform)) continue;
                at = hit.point; break;
            }
            var entity = SpawnHelper.Spawn(prefab, SpawnData.Default, null, at, Quaternion.LookRotation(forward, Vector3.up), null);
            return entity == null ? "spawn refused for " + prefab.name : "Spawned " + prefab.name + " for " + player;
        }
    }
}
