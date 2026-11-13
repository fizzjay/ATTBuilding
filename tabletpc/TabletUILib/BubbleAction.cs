using System;
using Alta.QuickAccessActions;
using MelonLoader;

namespace TabletUILib
{
	// Token: 0x0200000D RID: 13
	internal class BubbleAction : QuickAccessMenuAction
	{
		// Token: 0x17000009 RID: 9
		// (get) Token: 0x0600008A RID: 138 RVA: 0x000054D8 File Offset: 0x000036D8
		private static bool InGame
		{
			get
			{
				return Player.Current != null && GameModeManager.CurrentMode is JoinServerGameModeBase;
			}
		}

		// Token: 0x1700000A RID: 10
		// (get) Token: 0x0600008B RID: 139 RVA: 0x000054F0 File Offset: 0x000036F0
		public override bool IsValid
		{
			get
			{
				if (!BubbleAction.InGame || this.Entry == null)
				{
					return false;
				}
				string text = this.Entry.Needs ?? this.Entry.Owner.NeedsMod;
				return (text == null || ServerMods.Has(text)) && (this.Entry.Server == null || ServerMods.Can(this.Entry.Id));
			}
		}

		// Token: 0x1700000B RID: 11
		// (get) Token: 0x0600008C RID: 140 RVA: 0x0000555A File Offset: 0x0000375A
		public override bool IsIgnored
		{
			get
			{
				return !this.IsValid || base.IsIgnored;
			}
		}

		// Token: 0x1700000C RID: 12
		// (get) Token: 0x0600008D RID: 141 RVA: 0x0000556C File Offset: 0x0000376C
		public override bool IsActive
		{
			get
			{
				if (this.Entry == null)
				{
					return false;
				}
				if (this.Entry.Home != null)
				{
					return TabletUI.IsShowing(this.Entry.Home);
				}
				if (this.Entry.ServerGet != null)
				{
					return ServerMods.State(this.Entry.Id);
				}
				bool result;
				try
				{
					result = (this.Entry.Active != null && this.Entry.Active());
				}
				catch
				{
					result = false;
				}
				return result;
			}
		}

		// Token: 0x0600008E RID: 142 RVA: 0x000055FC File Offset: 0x000037FC
		public override void Run(Controller controller)
		{
			try
			{
				if (this.Entry.Home != null)
				{
					TabletUI.Toggle(controller, this.Entry.Home);
				}
				else if (this.Entry.Server != null)
				{
					ServerMods.Call(this.Entry.Id);
				}
				else
				{
					this.Entry.Run();
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("wheel button broke: " + ex.Message);
			}
		}

		// Token: 0x04000055 RID: 85
		public BubbleEntry Entry;
	}
}
