using System;
using ATTBuilding; // shared types live in the sibling ATTBuilding namespace
using Il2CppAlta.Global;
using Il2CppAlta.Networking;
using Il2CppAlta.Serialization;

namespace ATTBuildingQuest
{
    // Direct ATTBuilding client->server channel (message type 139), the
    // IL2CPP twin of pchost/BuildNet.cs, modelled on ArmorRestored's Quest
    // Wire (message 137). This REPLACES Alta.Console routing
    // (CommandSync.RouteCommand) as the primary spawn transport: the game's
    // RuntimeConsole source shows RunCommandOnServer is just
    // RouteCommand(command, null) -- the DEV console's channel, which the
    // server only honours for developer/admin-flagged players. Without a
    // server role the routed command dies before reaching any handler (the
    // logs proved it: module registered server-side, handler never fired).
    //
    // Here the client sends a spawn request on its server connection; the
    // server (PC build) reads it, spawns for connection.Player, and replies
    // on the same message. No roles, no name matching.
    internal static class BuildNet
    {
        internal const int Type = 139;

        private const uint KindSpawn = 1;
        private const uint KindReply = 2;
        private const uint KindPing = 3;
        private const uint KindNickSet = 4;
        private const uint KindNickDel = 5;

        internal static Connection ToServer;
        private static int _nextRequest = 1;
        private static int _lastRequest;

        private static SerializeConnectionMethod _handler;
        private static SerializeConnectionMethod _last;
        private static ConnectionChannel _channel;
        private static bool _channelResolved;

        internal static string Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                Prepare();
                var method = HarmonyLib.AccessTools.Method(typeof(JoinServerPipeline), "ConnectionApprovedByServer", null, null);
                if (method == null) return "buildnet: JoinServerPipeline.ConnectionApprovedByServer NOT FOUND; direct spawn channel unavailable (Alta route fallback only).";
                harmony.Patch(method, postfix: new HarmonyLib.HarmonyMethod(typeof(BuildNet), nameof(ApprovedPostfix)));
                return "buildnet: direct spawn channel ready (message 139; reply handler hooks ConnectionApprovedByServer).";
            }
            catch (Exception ex) { return "buildnet: install failed: " + ex.Message; }
        }

        private static void Prepare()
        {
            _handler = new Action<Connection, Stream>(Receive);
        }

        private static void ApprovedPostfix(Connection connection)
        {
            if (connection == null) return;
            try { Listen(connection); }
            catch (Exception ex) { Mod.Log.Error("buildnet listen: " + ex); }
        }

        internal static void Listen(Connection c)
        {
            ToServer = c;
            c.SetHandler((MessageType)Type, _handler);
            Diag.Log("buildnet: client reply handler installed on server connection " + c.Identifier + " (message 139). Direct spawn channel open.");
        }

        internal static bool SendSpawn(uint hash, int count)
        {
            int req = _nextRequest++;
            return Send("spawn #" + req, delegate(Stream s)
            {
                uint kind = KindSpawn; s.SerializeBits(ref kind, 8);
                int r = req; s.SerializeInteger(ref r);
                uint h = hash; s.SerializeBits(ref h, 32);
                int n = count; s.SerializeInteger(ref n);
            }, req);
        }

        internal static bool SendPing()
        {
            int req = _nextRequest++;
            return Send("ping #" + req, delegate(Stream s)
            {
                uint kind = KindPing; s.SerializeBits(ref kind, 8);
                int r = req; s.SerializeInteger(ref r);
            }, req);
        }

        internal static bool SendNickSet(string target, string nickname)
        {
            int req = _nextRequest++;
            return Send("nick set #" + req, delegate(Stream s)
            {
                uint kind = KindNickSet; s.SerializeBits(ref kind, 8);
                int r = req; s.SerializeInteger(ref r);
                string a = target ?? ""; s.SerializeString(ref a, (Stream.StringEncoding)1);
                string b = nickname ?? ""; s.SerializeString(ref b, (Stream.StringEncoding)1);
            }, req);
        }

        internal static bool SendNickDel(string nickname)
        {
            int req = _nextRequest++;
            return Send("nick delete #" + req, delegate(Stream s)
            {
                uint kind = KindNickDel; s.SerializeBits(ref kind, 8);
                int r = req; s.SerializeInteger(ref r);
                string a = nickname ?? ""; s.SerializeString(ref a, (Stream.StringEncoding)1);
            }, req);
        }

        private static bool Send(string what, Action<Stream> body, int req)
        {
            var c = ToServer;
            if (c == null || c.IsDisposed) { Diag.Log("buildnet: cannot send " + what + ": no live server connection captured yet (join a server first)."); return false; }
            try
            {
                ResolveChannel();
                // IL2CPP delegates are wrapper classes: convert explicitly and
                // keep a reference so the converted delegate is not collected.
                _last = new Action<Connection, Stream>((conn, s) => body(s));
                _lastRequest = req;
                bool sent = c.Send(_channel, (MessageType)Type, _last);
                Diag.Log("buildnet: " + what + " sent (Send=" + sent + ").");
                return sent;
            }
            catch (Exception ex) { Diag.Log("buildnet: send " + what + " FAILED: " + ex); return false; }
        }

        private static void Receive(Connection c, Stream s)
        {
            try
            {
                uint kind = 0; s.SerializeBits(ref kind, 8);
                if (kind != KindReply) { Diag.FileOnly("buildnet: client got unknown kind " + kind + "; ignored."); return; }
                int req = 0; s.SerializeInteger(ref req);
                uint ok = 0; s.SerializeBits(ref ok, 8);
                string text = null; s.SerializeString(ref text, (Stream.StringEncoding)1);
                Diag.Log("buildnet: REPLY #" + req + " ok=" + (ok != 0) + ": '" + (text ?? "") + "'");
                ConsoleHook.NoteServerReply((ok != 0 ? "" : "FAILED: ") + (text ?? ""));
            }
            catch (Exception ex) { Diag.Log("buildnet: reading reply failed: " + ex); }
        }

        private static void ResolveChannel()
        {
            if (_channelResolved) return;
            _channelResolved = true;
            try
            {
                ConnectionChannel best = null;
                foreach (ConnectionChannel ch in GlobalSettings<NetworkingSettings>.Instance.Channels)
                {
                    if (ch == null || !ch.IsReliable) continue;
                    if (best == null || ch.MaximumSendInterval < best.MaximumSendInterval) best = ch;
                }
                _channel = best;
                if (best == null) Mod.Log.Warning("buildnet: no reliable channel found; using the connection's default.");
            }
            catch (Exception ex) { Mod.Log.Warning("buildnet: channel lookup failed, using the connection's default: " + ex.Message); }
        }
    }
}
