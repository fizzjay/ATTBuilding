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
                int done = 0; string last = null;
                for (int i = 0; i < count; i++)
                {
                    last = ServerCommands.SpawnForPlayer(target, hash);
                    if (last != null && last.StartsWith("Spawned")) done++;
                    else break;
                }
                Reply(connection, req, done > 0, done == count ? last : (done > 0 ? "Spawned " + done + "/" + count + ". " + last : last));
            }
            catch (Exception ex) { Diag.Log("buildnet: server read FAILED: " + ex); }
        }

        private static void Reply(Connection connection, int req, bool ok, string text)
        {
            try
            {
                connection.Send(null, Message, delegate(Connection conn, Stream s)
                {
                    byte kind = KindReply; s.SerializeByte(ref kind);
                    int r = req; s.SerializeInteger(ref r);
                    byte o = (byte)(ok ? 1 : 0); s.SerializeByte(ref o);
                    string tx = text ?? ""; s.SerializeString(ref tx, (Stream.StringEncoding)1);
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
