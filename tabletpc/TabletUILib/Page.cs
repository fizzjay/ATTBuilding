using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000007 RID: 7
	public class Page
	{
		// Token: 0x0600000A RID: 10 RVA: 0x00002340 File Offset: 0x00000540
		public Page()
		{
		}

		// Token: 0x0600000B RID: 11 RVA: 0x00002353 File Offset: 0x00000553
		public Page(string title, string status = null)
		{
			this.Title = title;
			this.Status = status;
		}

		// Token: 0x0600000C RID: 12 RVA: 0x00002374 File Offset: 0x00000574
		public Page Add(string label, string detail, Action click)
		{
			this.Rows.Add(new Row
			{
				Label = label,
				Detail = detail,
				Click = click
			});
			return this;
		}

		// Token: 0x0600000D RID: 13 RVA: 0x0000239C File Offset: 0x0000059C
		public Page Add(string label, Action click)
		{
			return this.Add(label, null, click);
		}

		// Token: 0x0600000E RID: 14 RVA: 0x000023A7 File Offset: 0x000005A7
		public Page Button(string label, Action click)
		{
			return this.Add(label, null, click);
		}

		// Token: 0x0600000F RID: 15 RVA: 0x000023B2 File Offset: 0x000005B2
		public Page Button(string label, string detail, Action click)
		{
			return this.Add(label, detail, click);
		}

		// Token: 0x06000010 RID: 16 RVA: 0x000023BD File Offset: 0x000005BD
		public Page Text(string label, string detail = null)
		{
			return this.Add(label, detail, null);
		}

		// Token: 0x06000011 RID: 17 RVA: 0x000023C8 File Offset: 0x000005C8
		public Page Link(string label, Func<Page> page)
		{
			return this.Add(label, null, delegate()
			{
				TabletUI.Go(page);
			});
		}

		// Token: 0x06000012 RID: 18 RVA: 0x000023F8 File Offset: 0x000005F8
		public Page Link(string label, string detail, Func<Page> page)
		{
			return this.Add(label, detail, delegate()
			{
				TabletUI.Go(page);
			});
		}

		// Token: 0x06000013 RID: 19 RVA: 0x00002426 File Offset: 0x00000626
		public Page ServerButton(string label, string id)
		{
			return this.ServerButton(label, null, id);
		}

		// Token: 0x06000014 RID: 20 RVA: 0x00002434 File Offset: 0x00000634
		public Page ServerButton(string label, string detail, string id)
		{
			if (!ServerMods.Can(id))
			{
				return this.Text(label, "server doesnt have this");
			}
			return this.Add(label, detail, delegate()
			{
				ServerMods.Call(id);
			});
		}

		// Token: 0x06000015 RID: 21 RVA: 0x0000247C File Offset: 0x0000067C
		public Page ServerToggle(string label, string id)
		{
			if (!ServerMods.Can(id))
			{
				return this.Text(label, "server doesnt have this");
			}
			return this.Add(label, ServerMods.State(id) ? "on" : "off", delegate()
			{
				ServerMods.Call(id);
			});
		}

		// Token: 0x06000016 RID: 22 RVA: 0x000024DC File Offset: 0x000006DC
		public Page Toggle(string label, Func<bool> get, Action<bool> set)
		{
			return this.Add(label, get() ? "on" : "off", delegate()
			{
				set(!get());
			});
		}

		// Token: 0x06000017 RID: 23 RVA: 0x0000252C File Offset: 0x0000072C
		public Page Toggle(string label, MelonPreferences_Entry<bool> entry)
		{
			return this.Toggle(label, () => entry.Value, delegate(bool v)
			{
				Page.Save<bool>(entry, v);
			});
		}

		// Token: 0x06000018 RID: 24 RVA: 0x00002568 File Offset: 0x00000768
		public Page Choice(string label, string[] options, Func<int> get, Action<int> set)
		{
			if (options == null || options.Length == 0)
			{
				return this.Text(label, null);
			}
			int num = Mathf.Clamp(get(), 0, options.Length - 1);
			return this.Add(label, options[num], delegate()
			{
				set((Mathf.Clamp(get(), 0, options.Length - 1) + 1) % options.Length);
			});
		}

		// Token: 0x06000019 RID: 25 RVA: 0x000025E4 File Offset: 0x000007E4
		public Page Choice<T>(string label, Func<T> get, Action<T> set) where T : struct, Enum
		{
			T[] values = (T[])Enum.GetValues(typeof(T));
			string[] options = Array.ConvertAll<T, string>(values, (T v) => v.ToString().ToLowerInvariant());
			return this.Choice(label, options, () => Array.IndexOf<T>(values, get()), delegate(int i)
			{
				set(values[i]);
			});
		}

		// Token: 0x0600001A RID: 26 RVA: 0x0000266C File Offset: 0x0000086C
		public Page Choice<T>(string label, MelonPreferences_Entry<T> entry) where T : struct, Enum
		{
			return this.Choice<T>(label, () => entry.Value, delegate(T v)
			{
				Page.Save<T>(entry, v);
			});
		}

		// Token: 0x0600001B RID: 27 RVA: 0x000026A8 File Offset: 0x000008A8
		public Page Slider(string label, Func<float> get, Action<float> set, float min, float max, float step)
		{
			Func<Page> <>9__1;
			return this.Add(label, Page.Number(get()), delegate()
			{
				Func<Page> page;
				if ((page = <>9__1) == null)
				{
					page = (<>9__1 = (() => Page.SliderPage(label, get, set, min, max, step)));
				}
				TabletUI.Go(page);
			});
		}

		// Token: 0x0600001C RID: 28 RVA: 0x00002710 File Offset: 0x00000910
		public Page Slider(string label, MelonPreferences_Entry<float> entry, float min, float max, float step)
		{
			return this.Slider(label, () => entry.Value, delegate(float v)
			{
				Page.Save<float>(entry, v);
			}, min, max, step);
		}

		// Token: 0x0600001D RID: 29 RVA: 0x00002750 File Offset: 0x00000950
		public Page Slider(string label, Func<int> get, Action<int> set, int min, int max, int step = 1)
		{
			Func<float> get2 = () => (float)get();
			Action<float> set2 = delegate(float v)
			{
				set(Mathf.RoundToInt(v));
			};
			return this.Slider(label, get2, set2, (float)min, (float)max, (float)step);
		}

		// Token: 0x0600001E RID: 30 RVA: 0x0000279C File Offset: 0x0000099C
		public Page Slider(string label, MelonPreferences_Entry<int> entry, int min, int max, int step = 1)
		{
			return this.Slider(label, () => entry.Value, delegate(int v)
			{
				Page.Save<int>(entry, v);
			}, min, max, step);
		}

		// Token: 0x0600001F RID: 31 RVA: 0x000027DC File Offset: 0x000009DC
		private static Page SliderPage(string label, Func<float> get, Action<float> set, float min, float max, float step)
		{
			Action<float> change = delegate(float v)
			{
				set(Mathf.Clamp(v, min, max));
			};
			return new Page(label, string.Concat(new string[]
			{
				Page.Number(get()),
				"   (",
				Page.Number(min),
				" to ",
				Page.Number(max),
				")"
			})).Button("up", "+" + Page.Number(step), delegate()
			{
				change(get() + step);
			}).Button("down", "-" + Page.Number(step), delegate()
			{
				change(get() - step);
			}).Button("max", Page.Number(max), delegate()
			{
				change(max);
			}).Button("min", Page.Number(min), delegate()
			{
				change(min);
			});
		}

		// Token: 0x06000020 RID: 32 RVA: 0x00002916 File Offset: 0x00000B16
		private static string Number(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		// Token: 0x06000021 RID: 33 RVA: 0x0000292C File Offset: 0x00000B2C
		private static void Save<T>(MelonPreferences_Entry<T> entry, T value)
		{
			entry.Value = value;
			try
			{
				MelonPreferences.Save();
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("couldnt save " + entry.Identifier + ": " + ex.Message);
			}
		}

		// Token: 0x04000009 RID: 9
		public string Title;

		// Token: 0x0400000A RID: 10
		public string Status;

		// Token: 0x0400000B RID: 11
		public string Footer;

		// Token: 0x0400000C RID: 12
		public Action Back;

		// Token: 0x0400000D RID: 13
		public List<Row> Rows = new List<Row>();
	}
}
