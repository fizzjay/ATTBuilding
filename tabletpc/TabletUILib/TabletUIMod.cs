using System;
using MelonLoader;
using TavernLib.ModdingApi;

namespace TabletUILib
{
	// Token: 0x02000005 RID: 5
	[TavernMod(3)]
	public class TabletUIMod : MelonMod
	{
		// Token: 0x06000006 RID: 6 RVA: 0x000022D9 File Offset: 0x000004D9
		public override void OnInitializeMelon()
		{
			TabletUI.Patch(base.HarmonyInstance);
			TabletWheel.Patch(base.HarmonyInstance);
			ServerMods.Start();
			MelonLogger.Msg("tabletuilib loaded");
		}

		// Token: 0x06000007 RID: 7 RVA: 0x00002300 File Offset: 0x00000500
		public override void OnUpdate()
		{
			try
			{
				TabletUI.Tick();
				ServerMods.Tick();
			}
			catch (Exception ex)
			{
				MelonLogger.Error(ex);
			}
		}
	}
}
