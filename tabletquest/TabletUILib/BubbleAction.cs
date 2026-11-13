using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppAlta.QuickAccessActions;
using MelonLoader;

namespace TabletUILib
{
	// Token: 0x0200000A RID: 10
	internal static class BubbleAction
	{
		// Token: 0x17000003 RID: 3
		// (get) Token: 0x06000062 RID: 98 RVA: 0x000048FE File Offset: 0x00002AFE
		private static bool InGame
		{
			get
			{
				if (Player.Current != null)
				{
					GameMode currentMode = GameModeManager.CurrentMode;
					return ((currentMode != null) ? currentMode.TryCast<JoinServerGameModeBase>() : null) != null;
				}
				return false;
			}
		}

		// Token: 0x06000063 RID: 99 RVA: 0x0000491D File Offset: 0x00002B1D
		internal static void Add(QuickAccessMenuAction action, BubbleEntry entry)
		{
			BubbleAction.entries[action.Pointer] = entry;
		}

		// Token: 0x06000064 RID: 100 RVA: 0x00004930 File Offset: 0x00002B30
		private static bool Find(QuickAccessMenuAction action, out BubbleEntry entry)
		{
			entry = null;
			return action != null && BubbleAction.entries.TryGetValue(action.Pointer, out entry);
		}

		// Token: 0x06000065 RID: 101 RVA: 0x00004951 File Offset: 0x00002B51
		private static bool Valid(BubbleEntry entry)
		{
			return BubbleAction.InGame;
		}

		// Token: 0x06000066 RID: 102 RVA: 0x00004958 File Offset: 0x00002B58
		private static bool Active(BubbleEntry entry)
		{
			if (entry.Home != null)
			{
				return TabletUI.IsShowing(entry.Home);
			}
			bool result;
			try
			{
				result = (entry.Active != null && entry.Active());
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x06000067 RID: 103 RVA: 0x000049AC File Offset: 0x00002BAC
		private static void IsValidPostfix(NestingMenuAction __instance, ref bool __result)
		{
			try
			{
				BubbleEntry entry;
				if (BubbleAction.Find(__instance, out entry))
				{
					__result = BubbleAction.Valid(entry);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("wheel button check broke: " + ex.Message);
			}
		}

		// Token: 0x06000068 RID: 104 RVA: 0x000049F8 File Offset: 0x00002BF8
		private static void IsIgnoredPostfix(NestingMenuAction __instance, ref bool __result)
		{
			try
			{
				BubbleEntry entry;
				if (BubbleAction.Find(__instance, out entry))
				{
					__result = !BubbleAction.Valid(entry);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("wheel button check broke: " + ex.Message);
			}
		}

		// Token: 0x06000069 RID: 105 RVA: 0x00004A44 File Offset: 0x00002C44
		private static void IsActivePostfix(NestingMenuAction __instance, ref bool __result)
		{
			try
			{
				BubbleEntry entry;
				if (BubbleAction.Find(__instance, out entry))
				{
					__result = BubbleAction.Active(entry);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("wheel button check broke: " + ex.Message);
			}
		}

		// Token: 0x0600006A RID: 106 RVA: 0x00004A90 File Offset: 0x00002C90
		private static bool BubbleRunPrefix(QuickAccessMenuBubble __instance, Controller controller, ref bool __result)
		{
			BubbleEntry bubbleEntry;
			try
			{
				if (__instance == null || !BubbleAction.Find(__instance.content, out bubbleEntry))
				{
					return true;
				}
			}
			catch
			{
				return true;
			}
			__result = false;
			try
			{
				if (!BubbleAction.Valid(bubbleEntry))
				{
					return false;
				}
				if (bubbleEntry.Home != null)
				{
					TabletUI.Toggle(controller, bubbleEntry.Home);
				}
				else
				{
					bubbleEntry.Run();
				}
				__result = true;
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("wheel button broke: " + ex.Message);
			}
			return false;
		}

		// Token: 0x0600006B RID: 107 RVA: 0x00004B2C File Offset: 0x00002D2C
		internal static void Patch(Harmony harmony)
		{
			Type typeFromHandle = typeof(NestingMenuAction);
			harmony.Patch(AccessTools.PropertyGetter(typeFromHandle, "IsValid"), null, new HarmonyMethod(typeof(BubbleAction), "IsValidPostfix", null), null, null, null);
			harmony.Patch(AccessTools.PropertyGetter(typeFromHandle, "IsIgnored"), null, new HarmonyMethod(typeof(BubbleAction), "IsIgnoredPostfix", null), null, null, null);
			harmony.Patch(AccessTools.PropertyGetter(typeFromHandle, "IsActive"), null, new HarmonyMethod(typeof(BubbleAction), "IsActivePostfix", null), null, null, null);
			harmony.Patch(AccessTools.Method(typeof(QuickAccessMenuBubble), "Run", null, null), new HarmonyMethod(typeof(BubbleAction), "BubbleRunPrefix", null), null, null, null, null);
		}

		// Token: 0x0400003C RID: 60
		private static readonly Dictionary<IntPtr, BubbleEntry> entries = new Dictionary<IntPtr, BubbleEntry>();
	}
}
