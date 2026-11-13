using System;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000008 RID: 8
	internal class BubbleEntry
	{
		// Token: 0x0400002F RID: 47
		public string Name;

		// Token: 0x04000030 RID: 48
		public string Icon;

		// Token: 0x04000031 RID: 49
		public Texture2D Picture;

		// Token: 0x04000032 RID: 50
		public Func<Page> Home;

		// Token: 0x04000033 RID: 51
		public Action Run;

		// Token: 0x04000034 RID: 52
		public Func<bool> Active;

		// Token: 0x04000035 RID: 53
		public TabletBubble Owner;

		// Token: 0x04000036 RID: 54
		public TabletBubble Sub;
	}
}
