using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Alta.Chunks;
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
    //
    // Every hop logs: registration (Register + DumpCommandCollection), the
    // moment a routed command lands (Spawn/Ping entry), player resolution,
    // placement, and the SpawnHelper result. If a client times out, the
    // server log now says which hop never happened.
    [Module("build", "ATTBuilding: spawn and place prefabs for building")]
    public static class ServerCommands
    {
        // [Module] classes are NOT auto-discovered. ArmorRestored calls
        // CommandService.CommandCollection.Collect(assembly) once the server
        // scene initialises (its ServerSeam); without this, clients route
        // "build spawn ..." into the void and only ever see the timeout.
        // NOTE: do NOT Collect at melon-init time and latch the flag: if the
        // command collection is (re)created when the server scene loads, an
        // early Collect is silently discarded and the latch would then block
        // the real registration. Register only once we KNOW this process is
        // acting as a server (ServerSeam FirstInitialize, or the server-state
        // poll in Main.OnUpdate).
        private static bool _registered;
        private static float _nextServerTry;

        internal static bool Registered { get { return _registered; } }

        internal static void Register() { Register("unspecified"); }

        internal static void Register(string source)
        {
            if (_registered) { Diag.FileOnly("server: registration skipped (source=" + source + "): already registered."); return; }
            Diag.Log("server: registering 'build' module (source=" + source + ", assembly=" + typeof(ServerCommands).Assembly.FullName
                + ", authority[" + Spawner.AuthorityDescription() + "]). If this line never appears on the server, ATTBuilding's PC build is not loaded there.");
            try
            {
                CommandService.CommandCollection.Collect(typeof(ServerCommands).Assembly);
                _registered = true;
                Mod.Log.Msg("console commands registered: build (source " + source + "; clients can now 'build spawn' through this server).");
                Diag.Log("server: 'build' console module registered OK (source=" + source + ").");
                DumpCommandCollection();
            }
            catch (Exception ex)
            {
                _registered = false;
                Mod.Log.Error("console command registration failed (source " + source + "): " + ex);
                Diag.FileOnly("server: registration exception: " + ex);
            }
        }

        // Fallback if the FirstInitialize seam ever misses: Main.OnUpdate
        // calls this; it registers as soon as this process reports itself
        // server/local-test. No-ops (cheaply) once registered.
        internal static void TryRegisterWhenServer()
        {
            if (_registered) return;
            float now = Time.unscaledTime;
            if (now < _nextServerTry) return;
            _nextServerTry = now + 5f;
            bool isServer = false, isLocal = false;
            try { isServer = NetworkSceneManager.IsServer; isLocal = NetworkSceneManager.IsLocalTest; }
            catch (Exception ex) { Diag.FileOnly("server: register poll could not read NetworkSceneManager: " + ex.Message); return; }
            if (isServer || isLocal) Diag.FileOnly("server: register poll IsServer=" + isServer + " IsLocalTest=" + isLocal + " (not registered yet)");
            if (isServer || isLocal) Register("server-state poll (IsServer=" + isServer + ", IsLocalTest=" + isLocal + ")");
        }

        // One-time evidence of what Collect actually produced: the collection
        // type, its methods, and whether anything in it mentions build/spawn.
        private static void DumpCommandCollection()
        {
            try
            {
                object collection = CommandService.CommandCollection;
                if (collection == null) { Diag.FileOnly("server: CommandCollection is NULL after Collect -- registration cannot have worked."); return; }
                var type = collection.GetType();
                var methodNames = new List<string>();
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    if (!methodNames.Contains(m.Name)) methodNames.Add(m.Name);
                methodNames.Sort();
                Diag.FileOnly("server: CommandCollection type=" + type.FullName + " methods=[" + string.Join(", ", methodNames.ToArray()) + "]");
                var enumerable = collection as IEnumerable;
                if (enumerable == null) { Diag.FileOnly("server: CommandCollection is not IEnumerable; cannot list its commands."); return; }
                int total = 0, hits = 0;
                foreach (object item in enumerable)
                {
                    if (total >= 500) break;
                    total++;
                    string s = DescribeCollectionItem(item);
                    if (s.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0)
                    { hits++; Diag.FileOnly("server: collection[" + total + "] BUILD/SPAWN? " + s); }
                    else if (total <= 10) Diag.FileOnly("server: collection[" + total + "] " + s);
                }
                Diag.FileOnly("server: CommandCollection listed " + total + " item(s), " + hits + " mentioning build/spawn."
                    + (hits == 0 ? " ZERO build commands after Collect -- registration did not take; send us this dump." : ""));
            }
            catch (Exception ex) { Diag.FileOnly("server: CommandCollection dump failed: " + ex); }
        }

        private static string DescribeCollectionItem(object item)
        {
            if (item == null) return "(null)";
            string text;
            try { text = item.ToString(); } catch { text = "(ToString threw)"; }
            string result = item.GetType().FullName + " => " + text;
            try
            {
                foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    string n = prop.Name;
                    if (n.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("command", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("module", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("key", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("value", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("id", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try { object v = prop.GetValue(item, null); result += " | " + n + "=" + (v == null ? "null" : v.ToString()); } catch { }
                }
            }
            catch { }
            if (result.Length > 400) result = result.Substring(0, 400);
            return result.Replace('\n', ' ').Replace('\r', ' ');
        }

        // ---- Nicknames -------------------------------------------------
        // Saved in UserData/ATTBuilding/nicks.txt as
        //   nickname=identifier|username
        // so they survive restarts and can be hand-edited. Anywhere a
        // player token is accepted (spawn routing, 'build nick'), a saved
        // nickname resolves to that player.
        private static Dictionary<string, string[]> _nicks; // nickname(lower) -> [identifier, username]

        private static string NickFilePath { get { return Path.Combine(Mod.DataDir, "nicks.txt"); } }

        private static Dictionary<string, string[]> LoadNicks()
        {
            if (_nicks != null) return _nicks;
            _nicks = new Dictionary<string, string[]>();
            try
            {
                if (File.Exists(NickFilePath))
                {
                    foreach (string line in File.ReadAllLines(NickFilePath))
                    {
                        string s = line.Trim();
                        if (s.Length == 0 || s.StartsWith("#")) continue;
                        int eq = s.IndexOf('=');
                        if (eq <= 0) continue;
                        string nick = s.Substring(0, eq).Trim().ToLowerInvariant();
                        string[] parts = s.Substring(eq + 1).Split('|');
                        if (nick.Length > 0 && parts.Length >= 1) _nicks[nick] = new[] { parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "" };
                    }
                }
                Diag.FileOnly("nicks: loaded " + _nicks.Count + " nickname(s) from " + NickFilePath);
            }
            catch (Exception ex) { Diag.FileOnly("nicks: load failed: " + ex.Message); }
            return _nicks;
        }

        private static void SaveNicks()
        {
            try
            {
                var lines = new List<string> { "# ATTBuilding nicknames: nickname=identifier|username (edit freely; delete a line to remove)" };
                var keys = new List<string>(LoadNicks().Keys); keys.Sort();
                foreach (string k in keys) { var v = _nicks[k]; lines.Add(k + "=" + v[0] + "|" + v[1]); }
                File.WriteAllLines(NickFilePath, lines.ToArray());
                Diag.FileOnly("nicks: saved " + _nicks.Count + " nickname(s) to " + NickFilePath);
            }
            catch (Exception ex) { Diag.Log("nicks: save FAILED: " + ex); }
        }

        private static bool ValidNickname(string nick)
        {
            if (string.IsNullOrEmpty(nick) || nick.Length < 2 || nick.Length > 24) return false;
            foreach (char c in nick) if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.') return false;
            return true;
        }

        internal static string SetNickByToken(Player requester, string targetToken, string nickname, out bool ok)
        {
            ok = false;
            string nick = (nickname ?? "").Trim();
            if (!ValidNickname(nick)) return "FAILED: nickname '" + nickname + "' is invalid (2-24 chars: letters, digits, _ - .)";
            string why;
            var target = FindPlayer(targetToken, out why);
            if (target == null) return "FAILED: " + why;
            LoadNicks()[nick.ToLowerInvariant()] = new[] { target.UserInfo.Identifier.ToString(), target.UserInfo.Username };
            SaveNicks();
            ok = true;
            string msg = "Saved nickname '" + nick + "' for '" + target.UserInfo.Username + "'#" + target.UserInfo.Identifier
                + (requester != null && requester.UserInfo != null ? " (set by '" + requester.UserInfo.Username + "')" : "") + ".";
            Diag.Log("nicks: " + msg);
            return msg;
        }

        internal static string DeleteNickByName(Player requester, string nickname, out bool ok)
        {
            ok = false;
            string nick = (nickname ?? "").Trim().ToLowerInvariant();
            var map = LoadNicks();
            if (!map.ContainsKey(nick)) return "FAILED: no saved nickname '" + nickname + "'.";
            map.Remove(nick);
            SaveNicks();
            ok = true;
            string msg = "Deleted nickname '" + nickname + "'"
                + (requester != null && requester.UserInfo != null ? " (deleted by '" + requester.UserInfo.Username + "')" : "") + ".";
            Diag.Log("nicks: " + msg);
            return msg;
        }

        // Local/server-console entry (also used by PC 'build nick' when this
        // process IS the server): the requester is whoever runs it locally.
        internal static string NickLocal(string targetToken, string nickname)
        {
            bool ok; return SetNickByToken(null, targetToken, nickname, out ok);
        }

        internal static string DelNickLocal(string nickname)
        {
            bool ok; return DeleteNickByName(null, nickname, out ok);
        }

        [Command("nick", "Save a nickname for a player: build nick <player name|id|nickname> <nickname>")]
        public static string Nick(string player, string nickname)
        {
            try { bool ok; return SetNickByToken(null, player, nickname, out ok); }
            catch (Exception ex) { Diag.Log("server nick: EXCEPTION: " + ex); return "ATTBuilding nick failed on the server: " + ex.Message; }
        }

        [Command("delnick", "Delete a saved nickname: build delnick <nickname> (in-game: build del nick <nickname>)")]
        public static string DelNick(string nickname)
        {
            try { bool ok; return DeleteNickByName(null, nickname, out ok); }
            catch (Exception ex) { Diag.Log("server delnick: EXCEPTION: " + ex); return "ATTBuilding delnick failed on the server: " + ex.Message; }
        }

        [Command("ping", "Check the server runs ATTBuilding: build ping <player>")]
        public static string Ping(string player)
        {
            try
            {
                string why;
                var target = FindPlayer(player, out why);
                Diag.Log("server ping: received token='" + player + "' -> " + (target == null ? "NO MATCH (" + why + ")" : "matched '" + target.UserInfo.Username + "'#" + target.UserInfo.Identifier)
                    + "; ATTBuilding " + Mod.Version + " registered=" + _registered + " authority[" + Spawner.AuthorityDescription() + "] players[" + DescribePlayers() + "]");
                return "ATTBuilding server " + Mod.Version + " is HERE: 'build' module registered=" + _registered
                    + ", player " + (target == null ? "NOT matched (" + why + ")" : "'" + target.UserInfo.Username + "' matched")
                    + ". Now try: build spawn <prefab>";
            }
            catch (Exception ex) { Diag.Log("server ping: EXCEPTION: " + ex); return "ATTBuilding ping failed on the server: " + ex.Message; }
        }

        [Command("spawn", "Spawn a prefab in front of a player: build spawn <player> <prefabHash>")]
        public static string Spawn(string player, string prefabHash)
        {
            // Wrapped end-to-end (ArmorRestored's ArmorCommands.Run pattern):
            // an unhandled exception inside a routed command is swallowed by
            // the console layer and the client only ever sees its timeout.
            try { return SpawnInternal(player, prefabHash); }
            catch (Exception ex) { Diag.Log("server spawn: UNHANDLED EXCEPTION: " + ex); return "ATTBuilding spawn failed on the server: " + ex.Message; }
        }

        private static string SpawnInternal(string player, string prefabHash)
        {
            Diag.Log("server spawn: received token='" + player + "' hash='" + prefabHash + "' registered=" + _registered
                + " authority[" + Spawner.AuthorityDescription() + "] players[" + DescribePlayers() + "]");
            uint hash;
            if (!uint.TryParse(prefabHash, out hash)) { Diag.Log("server spawn: bad prefab hash '" + prefabHash + "'"); return "bad prefab hash '" + prefabHash + "'"; }
            string why;
            var target = FindPlayer(player, out why);
            if (target == null) { Diag.Log("server spawn: player NOT matched: " + why); return why; }
            Diag.Log("server spawn: player matched '" + target.UserInfo.Username + "'#" + target.UserInfo.Identifier);
            return SpawnForPlayer(target, hash);
        }

        // Spawn one prefab in front of an already-identified player. Shared
        // by the Alta.Console command above and the direct BuildNet channel
        // (which identifies the player from their connection instead).
        internal static string SpawnForPlayer(Player target, uint hash)
        {
            var prefab = PrefabManager.GetPrefab(hash);
            if (prefab == null) { Diag.Log("server spawn: PrefabManager.GetPrefab(" + hash + ") returned null on the server."); return "no prefab with hash " + hash + " on the server"; }
            Diag.Log("server spawn: prefab hash " + hash + " -> '" + prefab.name + "' for '" + target.UserInfo.Username + "'");
            var controller = target.PlayerController;
            if (controller == null || controller.Head == null) { Diag.Log("server spawn: player '" + target.UserInfo.Username + "' has no PlayerController/Head yet (not spawned in)."); return target.UserInfo.Username + " is not spawned in"; }
            Vector3 forward = controller.Head.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 origin = controller.Head.position + forward * 1.2f;
            Vector3 at = origin;
            RaycastHit[] hits = Physics.RaycastAll(origin + Vector3.up, Vector3.down, 12f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Diag.FileOnly("server spawn: placement origin=" + origin + " ground hits=" + hits.Length);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (controller.gameObject != null && hit.collider.transform.IsChildOf(controller.transform)) continue;
                at = hit.point; break;
            }
            // Lift a touch off the surface: prefabs whose pivot is not at
            // their base otherwise spawn half-buried (invisible in game
            // even though the server created them).
            at += Vector3.up * 0.1f;
            Diag.Log("server spawn: spawning '" + prefab.name + "' at " + at + " for '" + target.UserInfo.Username + "' via SpawnHelper.Spawn ...");
            var entity = SpawnHelper.Spawn(prefab, SpawnData.Default, null, at, Quaternion.LookRotation(forward, Vector3.up), null);
            if (entity == null) { Diag.Log("server spawn: SpawnHelper.Spawn returned NULL for '" + prefab.name + "' at " + at); return "spawn refused for " + prefab.name + " (SpawnHelper returned null; see server log)"; }
            // The game's own spawn command (SpawnCommandModule.Spawn) does
            // this right after SpawnHelper.Spawn; without a Chunk the
            // entity can exist on the server yet never stream to clients.
            string chunkInfo;
            try
            {
                var chunk = LocationChunkHelper.GetBestContaining(at, null);
                if (chunk != null) { entity.Chunk = chunk; chunkInfo = "assigned"; }
                else chunkInfo = "GetBestContaining returned null (left as SpawnHelper set it)";
            }
            catch (Exception ex) { chunkInfo = "assign failed: " + ex.Message; }
            Diag.Log("server spawn: SUCCESS '" + prefab.name + "' -> entity '" + entity.gameObject.name + "' instance=" + entity.gameObject.GetInstanceID()
                + " at " + entity.transform.position + " chunk=" + chunkInfo + " active=" + entity.gameObject.activeInHierarchy);
            return "Spawned " + prefab.name + " for " + target.UserInfo.Username + " at " + at;
        }

        // ArmorRestored's Players.Find: accept a username OR the numeric
        // UserInfo.Identifier, and say exactly who the server can see when
        // neither matches.
        private static Player FindPlayer(string token, out string why)
        {
            why = null;
            string text = (token ?? "").Trim();
            if (text.Length == 0) { why = "name a player"; return null; }
            uint num;
            bool isId = uint.TryParse(text, out num);
            foreach (IPlayer p in Player.AllPlayers)
            {
                var candidate = p as Player;
                if (candidate == null || candidate.UserInfo == null) continue;
                if (isId && candidate.UserInfo.Identifier == (int)num) return candidate;
                if (string.Equals(candidate.UserInfo.Username, text, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            // Saved nicknames resolve too ('build nick <player> <nickname>').
            string[] saved;
            if (LoadNicks().TryGetValue(text.ToLowerInvariant(), out saved) && saved != null && saved.Length >= 1)
            {
                foreach (IPlayer p in Player.AllPlayers)
                {
                    var candidate = p as Player;
                    if (candidate == null || candidate.UserInfo == null) continue;
                    int savedId;
                    if (int.TryParse(saved[0], out savedId) && candidate.UserInfo.Identifier == savedId) return candidate;
                    if (!string.IsNullOrEmpty(saved[1]) && string.Equals(candidate.UserInfo.Username, saved[1], StringComparison.OrdinalIgnoreCase)) return candidate;
                }
                why = "nickname '" + text + "' is saved for '" + (saved.Length > 1 ? saved[1] : saved[0]) + "' but that player is not connected. Server sees: " + DescribePlayers();
                return null;
            }
            why = "no connected player matching '" + text + "'. Server sees: " + DescribePlayers();
            return null;
        }

        private static string DescribePlayers()
        {
            try
            {
                var parts = new List<string>();
                foreach (IPlayer p in Player.AllPlayers)
                {
                    var pl = p as Player;
                    if (pl == null) { parts.Add("(non-Player " + (p == null ? "null" : p.GetType().Name) + ")"); continue; }
                    parts.Add(pl.UserInfo == null ? "(no UserInfo)" : "'" + pl.UserInfo.Username + "'#" + pl.UserInfo.Identifier);
                }
                return parts.Count == 0 ? "(none)" : string.Join(", ", parts.ToArray());
            }
            catch (Exception ex) { return "(unavailable: " + ex.Message + ")"; }
        }
    }
}
