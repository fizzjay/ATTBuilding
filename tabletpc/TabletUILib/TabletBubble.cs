using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x0200000C RID: 12
	public class TabletBubble
	{
		// Token: 0x0600007A RID: 122 RVA: 0x00004FE4 File Offset: 0x000031E4
		internal TabletBubble(string name, string icon)
		{
			this.Name = name;
			this.Icon = (string.IsNullOrEmpty(icon) ? "MOD" : icon);
		}

		// Token: 0x17000006 RID: 6
		// (get) Token: 0x0600007B RID: 123 RVA: 0x00005014 File Offset: 0x00003214
		internal string Path
		{
			get
			{
				if (this.Parent != null)
				{
					return this.Parent.Path + "/" + this.Name;
				}
				return this.Name;
			}
		}

		// Token: 0x17000007 RID: 7
		// (get) Token: 0x0600007C RID: 124 RVA: 0x00005040 File Offset: 0x00003240
		internal string NeedsMod
		{
			get
			{
				string result;
				if ((result = this.Needs) == null)
				{
					TabletBubble parent = this.Parent;
					if (parent == null)
					{
						return null;
					}
					result = parent.NeedsMod;
				}
				return result;
			}
		}

		// Token: 0x17000008 RID: 8
		// (get) Token: 0x0600007D RID: 125 RVA: 0x0000505D File Offset: 0x0000325D
		internal IEnumerable<BubbleEntry> All
		{
			get
			{
				return this.Entries.SelectMany(delegate(BubbleEntry e)
				{
					if (e.Sub != null)
					{
						return e.Sub.All;
					}
					return new BubbleEntry[]
					{
						e
					};
				});
			}
		}

		// Token: 0x0600007E RID: 126 RVA: 0x0000508C File Offset: 0x0000328C
		internal static string ModOf(Assembly asm)
		{
			MelonAssembly melonAssembly = MelonAssembly.LoadedAssemblies.FirstOrDefault((MelonAssembly a) => a.Assembly == asm);
			MelonBase melonBase = (melonAssembly != null) ? melonAssembly.LoadedMelons.FirstOrDefault<MelonBase>() : null;
			if (melonBase == null)
			{
				throw new InvalidOperationException("couldnt tell which mod this is, pass the mod name in");
			}
			return melonBase.Info.Name;
		}

		// Token: 0x0600007F RID: 127 RVA: 0x000050E5 File Offset: 0x000032E5
		public TabletBubble NeedsServer(string mod)
		{
			if (string.IsNullOrEmpty(mod))
			{
				throw new ArgumentNullException("mod");
			}
			this.Needs = mod;
			return this;
		}

		// Token: 0x06000080 RID: 128 RVA: 0x00005102 File Offset: 0x00003302
		[MethodImpl(MethodImplOptions.NoInlining)]
		public TabletBubble NeedsServer()
		{
			return this.NeedsServer(TabletBubble.ModOf(Assembly.GetCallingAssembly()));
		}

		// Token: 0x06000081 RID: 129 RVA: 0x00005114 File Offset: 0x00003314
		[MethodImpl(MethodImplOptions.NoInlining)]
		public TabletBubble Image(string resource, string entry = null)
		{
			if (string.IsNullOrEmpty(resource))
			{
				throw new ArgumentNullException("resource");
			}
			BubbleEntry bubbleEntry = null;
			if (entry != null)
			{
				bubbleEntry = this.Entries.FirstOrDefault((BubbleEntry e) => e.Name == entry);
				if (bubbleEntry == null)
				{
					throw new ArgumentException(string.Concat(new string[]
					{
						"no ",
						entry,
						" in ",
						this.Name,
						" yet, add it before giving it an image"
					}), "entry");
				}
			}
			Texture2D texture2D = Icons.Load(Assembly.GetCallingAssembly(), resource);
			if (texture2D == null)
			{
				return this;
			}
			if (bubbleEntry == null)
			{
				this.Picture = texture2D;
			}
			else if (bubbleEntry.Sub != null)
			{
				bubbleEntry.Sub.Picture = texture2D;
			}
			else
			{
				bubbleEntry.Picture = texture2D;
			}
			return this;
		}

		// Token: 0x06000082 RID: 130 RVA: 0x000051E8 File Offset: 0x000033E8
		public TabletBubble Bubble(string name, string icon, Action<TabletBubble> fill)
		{
			if (fill == null)
			{
				throw new ArgumentNullException("fill");
			}
			TabletBubble tabletBubble = new TabletBubble(name, icon)
			{
				Parent = this
			};
			fill(tabletBubble);
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = tabletBubble.Icon,
				Sub = tabletBubble,
				Owner = this
			});
			return this;
		}

		// Token: 0x06000083 RID: 131 RVA: 0x0000524B File Offset: 0x0000344B
		public TabletBubble App(string name, string icon, Func<Page> home)
		{
			if (home == null)
			{
				throw new ArgumentNullException("home");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Home = home,
				Owner = this
			});
			return this;
		}

		// Token: 0x06000084 RID: 132 RVA: 0x00005288 File Offset: 0x00003488
		[MethodImpl(MethodImplOptions.NoInlining)]
		public TabletBubble ServerApp(string name, string icon, Func<Page> home, string mod = null)
		{
			if (home == null)
			{
				throw new ArgumentNullException("home");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Home = home,
				Needs = (mod ?? TabletBubble.ModOf(Assembly.GetCallingAssembly())),
				Owner = this
			});
			return this;
		}

		// Token: 0x06000085 RID: 133 RVA: 0x000052E6 File Offset: 0x000034E6
		public TabletBubble Action(string name, string icon, Action run)
		{
			if (run == null)
			{
				throw new ArgumentNullException("run");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Run = run,
				Owner = this
			});
			return this;
		}

		// Token: 0x06000086 RID: 134 RVA: 0x00005324 File Offset: 0x00003524
		public TabletBubble ServerAction(string name, string icon, Action<Player> run, string role = null)
		{
			if (run == null)
			{
				throw new ArgumentNullException("run");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Server = run,
				Role = role,
				Owner = this
			});
			return this;
		}

		// Token: 0x06000087 RID: 135 RVA: 0x00005374 File Offset: 0x00003574
		public TabletBubble Toggle(string name, string icon, Func<bool> get, Action<bool> set)
		{
			if (get == null || set == null)
			{
				throw new ArgumentNullException((get == null) ? "get" : "set");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Run = delegate()
				{
					set(!get());
				},
				Active = get,
				Owner = this
			});
			return this;
		}

		// Token: 0x06000088 RID: 136 RVA: 0x00005404 File Offset: 0x00003604
		public TabletBubble Toggle(string name, string icon, MelonPreferences_Entry<bool> entry)
		{
			return this.Toggle(name, icon, () => entry.Value, delegate(bool v)
			{
				entry.Value = v;
				try
				{
					MelonPreferences.Save();
				}
				catch (Exception ex)
				{
					MelonLogger.Warning("couldnt save " + entry.Identifier + ": " + ex.Message);
				}
			});
		}

		// Token: 0x06000089 RID: 137 RVA: 0x00005440 File Offset: 0x00003640
		public TabletBubble ServerToggle(string name, string icon, Func<Player, bool> get, Action<Player, bool> set, string role = null)
		{
			if (get == null || set == null)
			{
				throw new ArgumentNullException((get == null) ? "get" : "set");
			}
			this.Entries.Add(new BubbleEntry
			{
				Name = name,
				Icon = icon,
				Server = delegate(Player p)
				{
					set(p, !get(p));
				},
				ServerGet = get,
				Role = role,
				Owner = this
			});
			return this;
		}

		// Token: 0x0400004F RID: 79
		internal string Name;

		// Token: 0x04000050 RID: 80
		internal string Icon;

		// Token: 0x04000051 RID: 81
		internal Texture2D Picture;

		// Token: 0x04000052 RID: 82
		internal List<BubbleEntry> Entries = new List<BubbleEntry>();

		// Token: 0x04000053 RID: 83
		internal string Needs;

		// Token: 0x04000054 RID: 84
		internal TabletBubble Parent;
	}
}
