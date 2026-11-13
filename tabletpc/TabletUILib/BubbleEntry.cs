using System;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x0200000B RID: 11
	internal class BubbleEntry
	{
		// Token: 0x17000005 RID: 5
		// (get) Token: 0x06000077 RID: 119 RVA: 0x00004FAC File Offset: 0x000031AC
		// (set) Token: 0x06000078 RID: 120 RVA: 0x00004FD3 File Offset: 0x000031D3
		public string Id
		{
			get
			{
				return this.id ?? (this.Owner.Path + "/" + this.Name);
			}
			set
			{
				this.id = value;
			}
		}

		// Token: 0x04000042 RID: 66
		public string Name;

		// Token: 0x04000043 RID: 67
		public string Icon;

		// Token: 0x04000044 RID: 68
		public Texture2D Picture;

		// Token: 0x04000045 RID: 69
		public Func<Page> Home;

		// Token: 0x04000046 RID: 70
		public Action Run;

		// Token: 0x04000047 RID: 71
		public Func<bool> Active;

		// Token: 0x04000048 RID: 72
		public TabletBubble Owner;

		// Token: 0x04000049 RID: 73
		public TabletBubble Sub;

		// Token: 0x0400004A RID: 74
		public Action<Player> Server;

		// Token: 0x0400004B RID: 75
		public Func<Player, bool> ServerGet;

		// Token: 0x0400004C RID: 76
		public string Role;

		// Token: 0x0400004D RID: 77
		public string Needs;

		// Token: 0x0400004E RID: 78
		private string id;
	}
}
