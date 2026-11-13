using System;
using MelonLoader;

namespace TabletUILib
{
	// Token: 0x02000003 RID: 3
	public class TabletUIMod : MelonMod
	{
		// Token: 0x06000005 RID: 5 RVA: 0x00002419 File Offset: 0x00000619
		public override void OnInitializeMelon()
		{
			TabletUI.Patch(base.HarmonyInstance);
			TabletWheel.Patch(base.HarmonyInstance);
			MelonLogger.Msg("tabletuilib loaded");
		}

		// Token: 0x06000006 RID: 6 RVA: 0x0000243C File Offset: 0x0000063C
		public override void OnUpdate()
		{
			try
			{
				TabletUI.Tick();
			}
			catch (Exception ex)
			{
				MelonLogger.Error(ex);
			}
		}
	}
}
