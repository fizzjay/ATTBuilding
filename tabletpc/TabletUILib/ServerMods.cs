using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Alta.Networking;
using Alta.Serialization;
using MelonLoader;
using TavernLib.ModdingApi;
using TavernLib.Utils;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000008 RID: 8
	public static class ServerMods
	{
		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000022 RID: 34 RVA: 0x0000297C File Offset: 0x00000B7C
		// (set) Token: 0x06000023 RID: 35 RVA: 0x00002983 File Offset: 0x00000B83
		public static bool Known { get; private set; }

		// Token: 0x06000024 RID: 36 RVA: 0x0000298B File Offset: 0x00000B8B
		public static bool Has(string mod)
		{
			return ServerMods.Known && mod != null && ServerMods.mods.Contains(mod);
		}

		// Token: 0x06000025 RID: 37 RVA: 0x000029A4 File Offset: 0x00000BA4
		public static bool Can(string id)
		{
			return ServerMods.Known && id != null && ServerMods.actions.Contains(id);
		}

		// Token: 0x06000026 RID: 38 RVA: 0x000029C0 File Offset: 0x00000BC0
		public static bool State(string id)
		{
			Dictionary<string, bool> obj = ServerMods.states;
			bool result;
			lock (obj)
			{
				bool flag2;
				result = (id != null && ServerMods.states.TryGetValue(id, out flag2) && flag2);
			}
			return result;
		}

		// Token: 0x06000027 RID: 39 RVA: 0x00002A10 File Offset: 0x00000C10
		public static void On(string id, Action<Player> run, string role = null)
		{
			if (string.IsNullOrEmpty(id))
			{
				throw new ArgumentNullException("id");
			}
			if (run == null)
			{
				throw new ArgumentNullException("run");
			}
			ServerMods.calls[id] = new BubbleEntry
			{
				Id = id,
				Server = run,
				Role = role
			};
		}

		// Token: 0x06000028 RID: 40 RVA: 0x00002A64 File Offset: 0x00000C64
		public static void Toggle(string id, Func<Player, bool> get, Action<Player, bool> set, string role = null)
		{
			if (string.IsNullOrEmpty(id))
			{
				throw new ArgumentNullException("id");
			}
			if (get == null || set == null)
			{
				throw new ArgumentNullException((get == null) ? "get" : "set");
			}
			ServerMods.calls[id] = new BubbleEntry
			{
				Id = id,
				Server = delegate(Player p)
				{
					set(p, !get(p));
				},
				ServerGet = get,
				Role = role
			};
		}

		// Token: 0x06000029 RID: 41 RVA: 0x00002B00 File Offset: 0x00000D00
		public static void Call(string id)
		{
			if (ServerMods.server == null || ServerMods.server.IsDisposed || !ServerMods.Can(id))
			{
				return;
			}
			ServerMods.server.Send(null, (MessageType)182, delegate(Connection c, Stream s)
			{
				s.SerializeString(ref id, 1);
			});
		}

		// Token: 0x17000002 RID: 2
		// (get) Token: 0x0600002A RID: 42 RVA: 0x00002B58 File Offset: 0x00000D58
		private static IEnumerable<BubbleEntry> Handlers
		{
			get
			{
				return TabletWheel.ServerEntries.Concat(ServerMods.calls.Values);
			}
		}

		// Token: 0x0600002B RID: 43 RVA: 0x00002B6E File Offset: 0x00000D6E
		internal static void Start()
		{
			TavernEvents.TavernEvent<ISocket> socketCreated = TavernEvents.SocketCreated;
			Action<ISocket> action;
			if ((action = ServerMods.<>O.<0>__OnSocket) == null)
			{
				action = (ServerMods.<>O.<0>__OnSocket = new Action<ISocket>(ServerMods.OnSocket));
			}
			socketCreated.Subscribe(action);
		}

		// Token: 0x0600002C RID: 44 RVA: 0x00002B98 File Offset: 0x00000D98
		internal static void Tick()
		{
			ServerMods.RunWork();
			ServerMods.SyncAll();
			if (ServerMods.Known || ServerMods.tries >= 5 || ServerMods.server == null || ServerMods.server.IsDisposed || !ServerMods.server.IsApproved || Player.Current == null)
			{
				return;
			}
			if (Time.unscaledTime < ServerMods.nextAsk)
			{
				return;
			}
			ServerMods.tries++;
			ServerMods.nextAsk = Time.unscaledTime + 3f;
			ServerMods.server.Send(null, (MessageType)180, delegate(Connection c, Stream s)
			{
			});
		}

		// Token: 0x0600002D RID: 45 RVA: 0x00002C40 File Offset: 0x00000E40
		private static void OnSocket(ISocket socket)
		{
			socket.ConnectionCreated += delegate(Connection c)
			{
				ServerMods.OnConnection(socket, c);
			};
			socket.ConnectionDestroyed += delegate(Connection c)
			{
				if (c == ServerMods.server)
				{
					ServerMods.Forget();
				}
			};
		}

		// Token: 0x0600002E RID: 46 RVA: 0x00002C9C File Offset: 0x00000E9C
		private static void OnConnection(ISocket socket, Connection connection)
		{
			if (socket.IsServer)
			{
				MessageType message = (MessageType)180;
				SerializeConnectionMethod handler;
				if ((handler = ServerMods.<>O.<1>__OnAsk) == null)
				{
					handler = (ServerMods.<>O.<1>__OnAsk = new SerializeConnectionMethod(ServerMods.OnAsk));
				}
				connection.SetHandler(message, handler);
				MessageType message2 = (MessageType)182;
				SerializeConnectionMethod handler2;
				if ((handler2 = ServerMods.<>O.<2>__OnPress) == null)
				{
					handler2 = (ServerMods.<>O.<2>__OnPress = new SerializeConnectionMethod(ServerMods.OnPress));
				}
				connection.SetHandler(message2, handler2);
				return;
			}
			ServerMods.Forget();
			ServerMods.server = connection;
			MessageType message3 = (MessageType)181;
			SerializeConnectionMethod handler3;
			if ((handler3 = ServerMods.<>O.<3>__OnReply) == null)
			{
				handler3 = (ServerMods.<>O.<3>__OnReply = new SerializeConnectionMethod(ServerMods.OnReply));
			}
			connection.SetHandler(message3, handler3);
			MessageType message4 = (MessageType)183;
			SerializeConnectionMethod handler4;
			if ((handler4 = ServerMods.<>O.<4>__OnStates) == null)
			{
				handler4 = (ServerMods.<>O.<4>__OnStates = new SerializeConnectionMethod(ServerMods.OnStates));
			}
			connection.SetHandler(message4, handler4);
		}

		// Token: 0x0600002F RID: 47 RVA: 0x00002D58 File Offset: 0x00000F58
		private static void Forget()
		{
			ServerMods.server = null;
			ServerMods.tries = 0;
			ServerMods.nextAsk = 0f;
			ServerMods.Known = false;
			ServerMods.mods.Clear();
			ServerMods.actions.Clear();
			Dictionary<string, bool> obj = ServerMods.states;
			lock (obj)
			{
				ServerMods.states.Clear();
			}
		}

		// Token: 0x06000030 RID: 48 RVA: 0x00002DCC File Offset: 0x00000FCC
		private static void Queue(Action a)
		{
			Queue<Action> obj = ServerMods.work;
			lock (obj)
			{
				ServerMods.work.Enqueue(a);
			}
		}

		// Token: 0x06000031 RID: 49 RVA: 0x00002E10 File Offset: 0x00001010
		private static void RunWork()
		{
			for (;;)
			{
				Queue<Action> obj = ServerMods.work;
				Action action;
				lock (obj)
				{
					if (ServerMods.work.Count == 0)
					{
						break;
					}
					action = ServerMods.work.Dequeue();
				}
				try
				{
					action();
					continue;
				}
				catch (Exception ex)
				{
					string str = "server stuff broke: ";
					Exception ex2 = ex;
					MelonLogger.Warning(str + ((ex2 != null) ? ex2.ToString() : null));
					continue;
				}
				break;
			}
		}

		// Token: 0x06000032 RID: 50 RVA: 0x00002E98 File Offset: 0x00001098
		private static void OnAsk(Connection connection, Stream stream)
		{
			ServerMods.Queue(delegate
			{
				ServerMods.Answer(connection);
			});
		}

		// Token: 0x06000033 RID: 51 RVA: 0x00002EB8 File Offset: 0x000010B8
		private static void Answer(Connection connection)
		{
			if (connection.IsDisposed)
			{
				return;
			}
			List<string> names = (from m in MelonBase.RegisteredMelons
			select m.Info.Name).Distinct<string>().ToList<string>();
			List<string> ids = (from e in ServerMods.Handlers
			select e.Id).Distinct<string>().ToList<string>();
			connection.Send(null, (MessageType)181, delegate(Connection c, Stream s)
			{
				ServerMods.Write(s, names);
				ServerMods.Write(s, ids);
			});
			ServerMods.sent[connection] = new Dictionary<string, bool>();
			ServerMods.Sync(connection);
		}

		// Token: 0x06000034 RID: 52 RVA: 0x00002F78 File Offset: 0x00001178
		private static void OnPress(Connection connection, Stream stream)
		{
			string id = "";
			stream.SerializeString(ref id, 1);
			ServerMods.Queue(delegate
			{
				ServerMods.RunPress(connection, id);
			});
		}

		// Token: 0x06000035 RID: 53 RVA: 0x00002FBC File Offset: 0x000011BC
		private static void RunPress(Connection connection, string id)
		{
			if (connection.IsDisposed)
			{
				return;
			}
			BubbleEntry bubbleEntry = ServerMods.Handlers.FirstOrDefault((BubbleEntry e) => e.Id == id);
			Player player = connection.Player as Player;
			if (bubbleEntry == null || player == null)
			{
				return;
			}
			if (bubbleEntry.Role != null && !Roles.HasRole(connection, bubbleEntry.Role))
			{
				string[] array = new string[6];
				int num = 0;
				UserInfoAndRole userInfo = connection.UserInfo;
				array[num] = ((userInfo != null) ? userInfo.Username : null);
				array[1] = " pressed ";
				array[2] = id;
				array[3] = " without the ";
				array[4] = bubbleEntry.Role;
				array[5] = " role";
				MelonLogger.Msg(string.Concat(array));
				return;
			}
			try
			{
				bubbleEntry.Server(player);
			}
			catch (Exception ex)
			{
				string str = "server button ";
				string id2 = id;
				string str2 = " broke: ";
				Exception ex2 = ex;
				MelonLogger.Warning(str + id2 + str2 + ((ex2 != null) ? ex2.ToString() : null));
			}
			if (bubbleEntry.ServerGet != null && ServerMods.sent.ContainsKey(connection))
			{
				ServerMods.Sync(connection);
			}
		}

		// Token: 0x06000036 RID: 54 RVA: 0x000030DC File Offset: 0x000012DC
		private static void SyncAll()
		{
			if (ServerMods.sent.Count == 0 || Time.unscaledTime < ServerMods.nextSync)
			{
				return;
			}
			ServerMods.nextSync = Time.unscaledTime + 0.5f;
			foreach (Connection connection in ServerMods.sent.Keys.ToList<Connection>())
			{
				if (connection.IsDisposed)
				{
					ServerMods.sent.Remove(connection);
				}
				else
				{
					ServerMods.Sync(connection);
				}
			}
		}

		// Token: 0x06000037 RID: 55 RVA: 0x00003178 File Offset: 0x00001378
		private static void Sync(Connection connection)
		{
			Player player = connection.Player as Player;
			if (player == null)
			{
				return;
			}
			Dictionary<string, bool> dictionary = ServerMods.sent[connection];
			List<KeyValuePair<string, bool>> changed = new List<KeyValuePair<string, bool>>();
			foreach (BubbleEntry bubbleEntry in ServerMods.Handlers)
			{
				if (bubbleEntry.ServerGet != null)
				{
					bool flag;
					try
					{
						flag = bubbleEntry.ServerGet(player);
					}
					catch
					{
						continue;
					}
					bool flag2;
					if (!dictionary.TryGetValue(bubbleEntry.Id, out flag2) || flag2 != flag)
					{
						dictionary[bubbleEntry.Id] = flag;
						changed.Add(new KeyValuePair<string, bool>(bubbleEntry.Id, flag));
					}
				}
			}
			if (changed.Count == 0)
			{
				return;
			}
			connection.Send(null, (MessageType)183, delegate(Connection c, Stream s)
			{
				int count = changed.Count;
				s.SerializeInteger(ref count);
				foreach (KeyValuePair<string, bool> keyValuePair in changed)
				{
					string key = keyValuePair.Key;
					int num = (keyValuePair.Value > false) ? 1 : 0;
					s.SerializeString(ref key, 1);
					s.SerializeInteger(ref num);
				}
			});
		}

		// Token: 0x06000038 RID: 56 RVA: 0x00003284 File Offset: 0x00001484
		private static void OnStates(Connection connection, Stream stream)
		{
			int num = 0;
			stream.SerializeInteger(ref num);
			for (int i = 0; i < num; i++)
			{
				string key = "";
				int num2 = 0;
				stream.SerializeString(ref key, 1);
				stream.SerializeInteger(ref num2);
				Dictionary<string, bool> obj = ServerMods.states;
				lock (obj)
				{
					ServerMods.states[key] = (num2 != 0);
				}
			}
		}

		// Token: 0x06000039 RID: 57 RVA: 0x00003300 File Offset: 0x00001500
		private static void Write(Stream s, List<string> list)
		{
			int count = list.Count;
			s.SerializeInteger(ref count);
			for (int i = 0; i < count; i++)
			{
				string text = list[i];
				s.SerializeString(ref text, 1);
			}
		}

		// Token: 0x0600003A RID: 58 RVA: 0x0000333C File Offset: 0x0000153C
		private static List<string> Read(Stream s)
		{
			int num = 0;
			s.SerializeInteger(ref num);
			List<string> list = new List<string>();
			for (int i = 0; i < num; i++)
			{
				string item = "";
				s.SerializeString(ref item, 1);
				list.Add(item);
			}
			return list;
		}

		// Token: 0x0600003B RID: 59 RVA: 0x0000337C File Offset: 0x0000157C
		private static void OnReply(Connection connection, Stream stream)
		{
			ServerMods.mods = new HashSet<string>(ServerMods.Read(stream), StringComparer.OrdinalIgnoreCase);
			ServerMods.actions = new HashSet<string>(ServerMods.Read(stream));
			ServerMods.Known = true;
			MelonLogger.Msg(string.Concat(new string[]
			{
				"server has ",
				ServerMods.mods.Count.ToString(),
				" mods and ",
				ServerMods.actions.Count.ToString(),
				" server buttons"
			}));
		}

		// Token: 0x0400000E RID: 14
		private const MessageType Ask = (MessageType)180;

		// Token: 0x0400000F RID: 15
		private const MessageType Reply = (MessageType)181;

		// Token: 0x04000010 RID: 16
		private const MessageType Press = (MessageType)182;

		// Token: 0x04000011 RID: 17
		private const MessageType States = (MessageType)183;

		// Token: 0x04000012 RID: 18
		private static HashSet<string> mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Token: 0x04000013 RID: 19
		private static HashSet<string> actions = new HashSet<string>();

		// Token: 0x04000014 RID: 20
		private static Dictionary<string, bool> states = new Dictionary<string, bool>();

		// Token: 0x04000015 RID: 21
		private static Dictionary<string, BubbleEntry> calls = new Dictionary<string, BubbleEntry>();

		// Token: 0x04000016 RID: 22
		private static Dictionary<Connection, Dictionary<string, bool>> sent = new Dictionary<Connection, Dictionary<string, bool>>();

		// Token: 0x04000017 RID: 23
		private static Queue<Action> work = new Queue<Action>();

		// Token: 0x04000018 RID: 24
		private static Connection server;

		// Token: 0x04000019 RID: 25
		private static int tries;

		// Token: 0x0400001A RID: 26
		private static float nextAsk;

		// Token: 0x0400001B RID: 27
		private static float nextSync;

		// Token: 0x0200001F RID: 31
		[CompilerGenerated]
		private static class <>O
		{
			// Token: 0x04000083 RID: 131
			public static Action<ISocket> <0>__OnSocket;

			// Token: 0x04000084 RID: 132
			public static SerializeConnectionMethod <1>__OnAsk;

			// Token: 0x04000085 RID: 133
			public static SerializeConnectionMethod <2>__OnPress;

			// Token: 0x04000086 RID: 134
			public static SerializeConnectionMethod <3>__OnReply;

			// Token: 0x04000087 RID: 135
			public static SerializeConnectionMethod <4>__OnStates;
		}
	}
}
