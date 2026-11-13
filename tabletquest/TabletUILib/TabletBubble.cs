using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000009 RID: 9
	public class TabletBubble
	{
		// Token: 0x0600005B RID: 91 RVA: 0x0000464F File Offset: 0x0000284F
		internal TabletBubble(string name, string icon)
		{
			this.Name = name;
			this.Icon = (string.IsNullOrEmpty(icon) ? "MOD" : icon);
		}

		// Token: 0x0600005C RID: 92 RVA: 0x00004680 File Offset: 0x00002880
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

		// Token: 0x0600005D RID: 93 RVA: 0x00004754 File Offset: 0x00002954
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

		// Token: 0x0600005E RID: 94 RVA: 0x000047B7 File Offset: 0x000029B7
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

		// Token: 0x0600005F RID: 95 RVA: 0x000047F4 File Offset: 0x000029F4
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

		// Token: 0x06000060 RID: 96 RVA: 0x00004834 File Offset: 0x00002A34
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

		// Token: 0x06000061 RID: 97 RVA: 0x000048C4 File Offset: 0x00002AC4
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

		// Token: 0x04000037 RID: 55
		internal string Name;

		// Token: 0x04000038 RID: 56
		internal string Icon;

		// Token: 0x04000039 RID: 57
		internal Texture2D Picture;

		// Token: 0x0400003A RID: 58
		internal List<BubbleEntry> Entries = new List<BubbleEntry>();

		// Token: 0x0400003B RID: 59
		internal TabletBubble Parent;
	}
}
