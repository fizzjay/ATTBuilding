using System;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.Serialization;
using HarmonyLib;

namespace ATTBuilding
{
    // Direct ATTBuilding client<->server channel (message type 139), modelled
    // on ArmorRestored's message-137 wire. This REPLACES Alta.Console routing
    // (CommandSync.RouteCommand) as the primary spawn transport: the game's
    // RuntimeConsole source shows RunCommandOnServer is just
    // RouteCommand(command, null) -- the DEV console's channel, which the
    // server only honours for developer/admin-flagged players. A player with
    // no server roles routes into the void: no handler entry, no reply, only
    // the client timeout (exactly what the logs showed: module registered,
    // 'server spawn: received' never fired).
    //
    // Here both ends are ATTBuilding: the client sends a spawn request on
    // its server connection; the server reads it via a Connection handler,
    // spawns for connection.Player (the server knows who sent it -- no name
    // matching, no roles), and replies on the same message. The Alta route
    // remains as a fallback in Spawner.NetRoute.
    internal static class BuildNet
    {
        internal const MessageType Message = (MessageType)139;

        private const byte KindSpawn = 1;
        private const byte KindReply = 2;
        private const byte KindPing = 3;
        private const byte KindDespawn = 6;

        internal static Connection ToServer;
        private static int _nextRequest = 1;

        // ---- client side -------------------------------------------------
        internal static void Listen(Connection connection)
        {
            ToServer = connection;
            connection.SetHandler(Message, new SerializeConnectionMethod(ClientRead));
            Diag.Log("buildnet: client reply handler installed on server connection " + connection.Identifier + " (message 139). Direct spawn channel open.");
        }

        internal static bool SendSpawn(uint hash, int count)
        {
            var c = ToServer;
            if (c == null || c.IsDisposed) { Diag.Log("buildnet: cannot send spawn: no live server connection captured yet (join a server first)."); return false; }
            int req = _nextRequest++;
            try
            {
                c.Send(null, Message, delegate(Connection conn, Stream s)
                {
                    byte kind = KindSpawn; s.SerializeByte(ref kind);
                    int r = req; s.SerializeInteger(ref r);
                    uint h = hash; s.SerializeBits(ref h, 32);
                    int n = count; s.SerializeInteger(ref n);
                });
                Diag.Log("buildnet: spawn request #" + req + " sent (hash=" + hash + " count=" + count + ").");
                return true;
            }
            catch (Exception ex) { Diag.Log("buildnet: spawn send FAILED: " + ex); return false; }
        }

        internal static bool SendPing()
        {
            var c = ToServer;
            if (c == null || c.IsDisposed) { Diag.Log("buildnet: cannot ping: no live server connection captured yet."); return false; }
            int req = _nextRequest++;
            try
            {
                c.Send(null, Message, delegate(Connection conn, Stream s)
                {
                    byte kind = KindPing; s.SerializeByte(ref kind);
                    int r = req; s.SerializeInteger(ref r);
                });
                Diag.Log("buildnet: ping #" + req + " sent.");
                return true;
            }
            catch (Exception ex) { Diag.Log("buildnet: ping send FAILED: " + ex); return false; }
        }

        internal static bool SendDespawn(uint[] entityIds)
        {
            var c = ToServer;
            if (c == null || c.IsDisposed) { Diag.Log("buildnet: cannot send despawn: no live server connection captured yet."); return false; }
            int req = _nextRequest++;
            try
            {
                c.Send(null, Message, delegate(Connection conn, Stream s)
                {
                    byte kind = KindDespawn; s.SerializeByte(ref kind);
                    int r = req; s.SerializeInteger(ref r);
                    int n = entityIds == null ? 0 : entityIds.Length; s.SerializeInteger(ref n);
                    if (entityIds != null) foreach (uint id in entityIds) { uint u = id; s.SerializeBits(ref u, 32); }
                });
                Diag.Log("buildnet: despawn request #" + req + " sent (" + (entityIds == null ? 0 : entityIds.Length) + " entity id(s)).");
                return true;
            }
            catch (Exception ex) { Diag.Log("buildnet: despawn send FAILED: " + ex); return false; }
        }

        private static void ClientRead(Connection connection, Stream s)
        {
            try
            {
                byte kind = 0; s.SerializeByte(ref kind);
                if (kind != KindReply) { Diag.FileOnly("buildnet: client got unknown kind " + kind + "; ignored."); return; }
                int req = 0; s.SerializeInteger(ref req);
                byte ok = 0; s.SerializeByte(ref ok);
                string text = null; s.SerializeString(ref text, (Stream.StringEncoding)1);
                Diag.Log("buildnet: REPLY #" + req + " ok=" + (ok != 0) + ": '" + (text ?? "") + "'");
                ConsoleHook.NoteServerReply((ok != 0 ? "" : "FAILED: ") + (text ?? ""));
                // Trailing spawned-entity identifiers (0.4.7+ servers): so this
                // client can track/grab/clear what it asked for. Guarded: an
                // older server simply has nothing more in the stream.
                try
                {
                    int n = 0; s.SerializeInteger(ref n);
                    for (int i = 0; i < n && i < 40; i++) { uint id = 0; s.SerializeBits(ref id, 32); if (id != 0) Spawner.TrackRemote(id); }
                    if (n > 0) Diag.Log("buildnet: reply carried " + n + " spawned entity id(s) to track.");
                }
                catch { }
            }
            catch (Exception ex) { Diag.Log("buildnet: reading reply failed: " + ex); }
        }

        // ---- server side -------------------------------------------------
        internal static void InstallServerHandler(Connection connection)
        {
            connection.SetHandler(Message, new SerializeConnectionMethod(ServerRead));
            Diag.Log("buildnet: server request handler installed for connection " + connection.Identifier + " (message 139).");
        }

        private static void ServerRead(Connection connection, Stream s)
        {
            try
            {
                byte kind = 0; s.SerializeByte(ref kind);
                if (kind == KindPing)
                {
                    int req0 = 0; s.SerializeInteger(ref req0);
                    var who0 = connection.Player as Player;
                    Diag.Log("buildnet: PING #" + req0 + " from connection " + connection.Identifier + " player=" + (who0 == null || who0.UserInfo == null ? "?" : who0.UserInfo.Username)
                        + "; ATTBuilding " + Mod.Version + " (direct channel). Alta module registered=" + ServerCommands.Registered);
                    Reply(connection, req0, true, "ATTBuilding server " + Mod.Version + " is HERE on the direct channel (Alta 'build' module registered=" + ServerCommands.Registered + "). Spawn away.");
                    return;
                }
                if (kind == KindDespawn)
                {
                    int reqC = 0; s.SerializeInteger(ref reqC);
                    int nC = 0; s.SerializeInteger(ref nC);
                    var idsC = new uint[Math.Max(0, Math.Min(nC, 200))];
                    for (int i = 0; i < idsC.Length; i++) { uint u = 0; s.SerializeBits(ref u, 32); idsC[i] = u; }
                    var whoC = connection.Player as Player;
                    Diag.Log("buildnet: DESPAWN #" + reqC + " from " + (whoC == null || whoC.UserInfo == null ? "?" : whoC.UserInfo.Username) + ": " + idsC.Length + " id(s)");
                    if (whoC == null) { Reply(connection, reqC, false, "server could not identify your player from your connection yet.", null); return; }
                    int removedC = ServerCommands.DespawnForPlayer(whoC, idsC);
                    Reply(connection, reqC, true, "Removed " + removedC + " of your spawned object(s).", null);
                    return;
                }
                if (kind != KindSpawn) { Diag.FileOnly("buildnet: server got unknown kind " + kind + " from connection " + connection.Identifier + "; ignored."); return; }
                int req = 0; s.SerializeInteger(ref req);
                uint hash = 0; s.SerializeBits(ref hash, 32);
                int count = 1; s.SerializeInteger(ref count);
                if (count < 1) count = 1; if (count > 20) count = 20;
                var target = connection.Player as Player;
                Diag.Log("buildnet: SPAWN request #" + req + " from connection " + connection.Identifier
                    + " player=" + (target == null || target.UserInfo == null ? "(unknown)" : "'" + target.UserInfo.Username + "'#" + target.UserInfo.Identifier)
                    + " hash=" + hash + " count=" + count);
                if (target == null) { Reply(connection, req, false, "server could not identify your player from your connection yet -- try again once fully spawned in."); return; }
                int done = 0; string last = null; var ids = new System.Collections.Generic.List<uint>();
                for (int i = 0; i < count; i++)
                {
                    uint entityId;
                    last = ServerCommands.SpawnForPlayer(target, hash, out entityId);
                    if (last != null && last.StartsWith("Spawned")) { done++; if (entityId != 0) ids.Add(entityId); }
                    else break;
                }
                Reply(connection, req, done > 0, done == count ? last : (done > 0 ? "Spawned " + done + "/" + count + ". " + last : last), ids);
            }
            catch (Exception ex) { Diag.Log("buildnet: server read FAILED: " + ex); }
        }

        private static void Reply(Connection connection, int req, bool ok, string text) { Reply(connection, req, ok, text, null); }

        private static void Reply(Connection connection, int req, bool ok, string text, System.Collections.Generic.List<uint> entityIds)
        {
            try
            {
                connection.Send(null, Message, delegate(Connection conn, Stream s)
                {
                    byte kind = KindReply; s.SerializeByte(ref kind);
                    int r = req; s.SerializeInteger(ref r);
                    byte o = (byte)(ok ? 1 : 0); s.SerializeByte(ref o);
                    string tx = text ?? ""; s.SerializeString(ref tx, (Stream.StringEncoding)1);
                    int n = entityIds == null ? 0 : entityIds.Count; s.SerializeInteger(ref n);
                    if (entityIds != null) foreach (uint id in entityIds) { uint u = id; s.SerializeBits(ref u, 32); }
                });
                Diag.Log("buildnet: reply #" + req + " sent (ok=" + ok + "): '" + (text ?? "") + "'");
            }
            catch (Exception ex) { Diag.Log("buildnet: reply FAILED: " + ex); }
        }
    }

    // Client connection capture (ArmorRestored's ClientConnectionSeam).
    [HarmonyPatch(typeof(JoinServerPipeline), "ConnectionApprovedByServer")]
    internal static class BuildNetClientSeam
    {
        [HarmonyPostfix]
        private static void Postfix(Connection connection)
        {
            if (connection == null) return;
            try { BuildNet.Listen(connection); }
            catch (Exception ex) { Mod.Log.Error("buildnet client seam: " + ex.Message); }
        }
    }

    // Server connection handler (ArmorRestored's ServerConnectionSeam).
    [HarmonyPatch(typeof(ServerHandler), "ConnectionCreated")]
    internal static class BuildNetServerSeam
    {
        [HarmonyPostfix]
        private static void Postfix(Connection connection)
        {
            if (connection == null) return;
            try { BuildNet.InstallServerHandler(connection); }
            catch (Exception ex) { Mod.Log.Error("buildnet server seam: " + ex.Message); }
        }
    }
}
