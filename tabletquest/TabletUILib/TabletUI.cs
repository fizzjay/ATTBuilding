using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2Cpp;
using Il2CppAlta.Meta.UI;
using Il2CppAlta.QuickAccessActions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000006 RID: 6
	public static class TabletUI
	{
		// Token: 0x17000001 RID: 1
		// (get) Token: 0x0600001E RID: 30 RVA: 0x00002A00 File Offset: 0x00000C00
		public static bool IsOpen
		{
			get
			{
				return TabletUI.view != null || TabletUI.waitUntil > 0f;
			}
		}

		// Token: 0x0600001F RID: 31 RVA: 0x00002A17 File Offset: 0x00000C17
		public static void AddApp(string name, string icon, Func<Page> homePage)
		{
			TabletWheel.Apps.App(name, string.IsNullOrEmpty(icon) ? "APP" : icon, homePage);
		}

		// Token: 0x06000020 RID: 32 RVA: 0x00002A36 File Offset: 0x00000C36
		public static TabletBubble AddBubble(string name, string icon)
		{
			return TabletWheel.Add(name, icon);
		}

		// Token: 0x17000002 RID: 2
		// (get) Token: 0x06000021 RID: 33 RVA: 0x00002A3F File Offset: 0x00000C3F
		public static TabletBubble Wheel
		{
			get
			{
				return TabletWheel.Wheel;
			}
		}

		// Token: 0x06000022 RID: 34 RVA: 0x00002A46 File Offset: 0x00000C46
		private static bool IsLeaving(SocialTablet t)
		{
			return TabletUI.leaving.ContainsKey(t.Pointer);
		}

		// Token: 0x06000023 RID: 35 RVA: 0x00002A58 File Offset: 0x00000C58
		private static SocialTablet FindTablet()
		{
			foreach (IntPtr key in (from p in TabletUI.leaving
			where p.Value == null
			select p.Key).ToList<IntPtr>())
			{
				TabletUI.leaving.Remove(key);
			}
			foreach (SocialTablet socialTablet in Object.FindObjectsOfType<SocialTablet>())
			{
				if (socialTablet != null && socialTablet.IsLocalPlayer && socialTablet.gameObject.activeInHierarchy && !TabletUI.IsLeaving(socialTablet))
				{
					return socialTablet;
				}
			}
			return null;
		}

		// Token: 0x06000024 RID: 36 RVA: 0x00002B60 File Offset: 0x00000D60
		private static SummonActionItemQuickAccess SocialTabletAction()
		{
			ActionItemsManager instance = ActionItemsManager.Instance;
			if (instance == null)
			{
				return null;
			}
			Il2CppReferenceArray<ActionItemData> actionItems = instance.actionItems;
			if (actionItems == null)
			{
				return null;
			}
			foreach (ActionItemData actionItemData in actionItems)
			{
				if (actionItemData != null && actionItemData.Action != null && actionItemData.Action.DisplayName == "Social Tablet")
				{
					return actionItemData.Action;
				}
			}
			return null;
		}

		// Token: 0x06000025 RID: 37 RVA: 0x00002BFC File Offset: 0x00000DFC
		private static bool Summon(Controller controller)
		{
			SummonActionItemQuickAccess summonActionItemQuickAccess = TabletUI.SocialTabletAction();
			if (summonActionItemQuickAccess == null)
			{
				MelonLogger.Warning("couldnt find the social tablet on the wheel");
				return false;
			}
			TabletUI.summoning = true;
			try
			{
				summonActionItemQuickAccess.Run(controller);
			}
			finally
			{
				TabletUI.summoning = false;
			}
			return true;
		}

		// Token: 0x06000026 RID: 38 RVA: 0x00002C4C File Offset: 0x00000E4C
		private static Page MakePage()
		{
			Page page;
			try
			{
				page = (TabletUI.current() ?? new Page("EMPTY", "this page returned nothing"));
			}
			catch (Exception ex)
			{
				string str = "page broke: ";
				Exception ex2 = ex;
				MelonLogger.Warning(str + ((ex2 != null) ? ex2.ToString() : null));
				page = new Page("ERROR", ex.Message);
			}
			if (page.Back == null && TabletUI.history.Count > 0)
			{
				Page page2 = page;
				Action back;
				if ((back = TabletUI.<>O.<0>__Back) == null)
				{
					back = (TabletUI.<>O.<0>__Back = new Action(TabletUI.Back));
				}
				page2.Back = back;
			}
			return page;
		}

		// Token: 0x06000027 RID: 39 RVA: 0x00002CF0 File Offset: 0x00000EF0
		private static void Show(bool keepPage)
		{
			if (TabletUI.view != null && TabletUI.current != null)
			{
				TabletUI.view.Show(TabletUI.MakePage(), keepPage);
			}
		}

		// Token: 0x06000028 RID: 40 RVA: 0x00002D10 File Offset: 0x00000F10
		public static void Go(Func<Page> page)
		{
			if (page == null)
			{
				return;
			}
			if (TabletUI.current != null)
			{
				TabletUI.history.Add(TabletUI.current);
			}
			TabletUI.current = page;
			TabletUI.Show(false);
		}

		// Token: 0x06000029 RID: 41 RVA: 0x00002D38 File Offset: 0x00000F38
		public static void Back()
		{
			if (TabletUI.history.Count == 0)
			{
				TabletUI.GoHome();
				return;
			}
			TabletUI.current = TabletUI.history[TabletUI.history.Count - 1];
			TabletUI.history.RemoveAt(TabletUI.history.Count - 1);
			TabletUI.Show(false);
		}

		// Token: 0x0600002A RID: 42 RVA: 0x00002D8E File Offset: 0x00000F8E
		public static void GoHome()
		{
			TabletUI.history.Clear();
			TabletUI.current = TabletUI.home;
			TabletUI.Show(false);
		}

		// Token: 0x0600002B RID: 43 RVA: 0x00002DAA File Offset: 0x00000FAA
		public static void Refresh()
		{
			TabletUI.Show(true);
		}

		// Token: 0x0600002C RID: 44 RVA: 0x00002DB2 File Offset: 0x00000FB2
		public static bool IsShowing(Func<Page> homePage)
		{
			return TabletUI.IsOpen && TabletUI.home == homePage;
		}

		// Token: 0x0600002D RID: 45 RVA: 0x00002DC8 File Offset: 0x00000FC8
		private static void Detach()
		{
			TabletUI.waitUntil = -1f;
			TabletView tabletView = TabletUI.view;
			TabletUI.view = null;
			TabletUI.tablet = null;
			TabletUI.current = null;
			TabletUI.history.Clear();
			if (tabletView != null)
			{
				tabletView.Dispose();
			}
		}

		// Token: 0x0600002E RID: 46 RVA: 0x00002E0A File Offset: 0x0000100A
		public static void Close()
		{
			bool flag = TabletUI.view != null;
			TabletUI.Detach();
			if (flag && TabletUI.lastController != null && TabletUI.FindTablet() != null)
			{
				TabletUI.Summon(TabletUI.lastController);
			}
		}

		// Token: 0x0600002F RID: 47 RVA: 0x00002E40 File Offset: 0x00001040
		internal static void Toggle(Controller controller, Func<Page> homePage)
		{
			TabletUI.lastController = controller;
			if (TabletUI.IsOpen)
			{
				if (TabletUI.home != homePage)
				{
					TabletUI.home = homePage;
					TabletUI.GoHome();
					return;
				}
				TabletUI.Close();
				return;
			}
			else
			{
				TabletUI.home = homePage;
				if (TabletUI.FindTablet() == null && !TabletUI.Summon(controller))
				{
					return;
				}
				TabletUI.waitUntil = Time.unscaledTime + 6f;
				return;
			}
		}

		// Token: 0x06000030 RID: 48 RVA: 0x00002EA8 File Offset: 0x000010A8
		private static void Open(SocialTablet t)
		{
			TabletUI.waitUntil = -1f;
			TabletUI.tablet = t;
			TabletUI.view = TabletView.Create(t);
			if (TabletUI.view == null)
			{
				TabletUI.tablet = null;
				MelonLogger.Warning("couldnt open the page on the tablet");
				return;
			}
			try
			{
				TabletView tabletView = TabletUI.view;
				Action action;
				if ((action = TabletUI.<>O.<1>__GoHome) == null)
				{
					action = (TabletUI.<>O.<1>__GoHome = new Action(TabletUI.GoHome));
				}
				tabletView.Home = action;
				TabletView tabletView2 = TabletUI.view;
				Action afterClick;
				if ((afterClick = TabletUI.<>O.<2>__Refresh) == null)
				{
					afterClick = (TabletUI.<>O.<2>__Refresh = new Action(TabletUI.Refresh));
				}
				tabletView2.AfterClick = afterClick;
				TouchScreenMenuBase componentInChildren = t.GetComponentInChildren<TouchScreenMenuBase>(true);
				if (componentInChildren != null && !componentInChildren.isSetupLocal)
				{
					componentInChildren.Setup(true);
				}
				TabletUI.GoHome();
				TabletUI.view.Window.SwitchToPanel(TabletUI.view.Panel);
			}
			catch (Exception ex)
			{
				TabletUI.Detach();
				string str = "couldnt open the page on the tablet ";
				Exception ex2 = ex;
				MelonLogger.Warning(str + ((ex2 != null) ? ex2.ToString() : null));
			}
		}

		// Token: 0x06000031 RID: 49 RVA: 0x00002FA8 File Offset: 0x000011A8
		internal static void Tick()
		{
			if (TabletUI.view != null)
			{
				if (TabletUI.tablet == null || !TabletUI.tablet.gameObject.activeInHierarchy || TabletUI.IsLeaving(TabletUI.tablet))
				{
					TabletUI.Detach();
					return;
				}
				if (Time.unscaledTime >= TabletUI.nextRefresh)
				{
					TabletUI.nextRefresh = Time.unscaledTime + 0.25f;
					TabletUI.Refresh();
				}
				return;
			}
			else
			{
				if (TabletUI.waitUntil < 0f)
				{
					return;
				}
				if (Time.unscaledTime > TabletUI.waitUntil)
				{
					TabletUI.waitUntil = -1f;
					MelonLogger.Warning("the social tablet didnt come out");
					return;
				}
				SocialTablet socialTablet = TabletUI.FindTablet();
				if (socialTablet != null)
				{
					TabletUI.Open(socialTablet);
				}
				return;
			}
		}

		// Token: 0x06000032 RID: 50 RVA: 0x00003051 File Offset: 0x00001251
		private static void AfterFade(SocialTablet __instance, bool isFadingIn)
		{
			if (__instance == null)
			{
				return;
			}
			if (isFadingIn)
			{
				TabletUI.leaving.Remove(__instance.Pointer);
				return;
			}
			TabletUI.leaving[__instance.Pointer] = __instance;
		}

		// Token: 0x06000033 RID: 51 RVA: 0x00003084 File Offset: 0x00001284
		private static bool KeepOurPanel(Window window, ref Panel panel, bool finishing)
		{
			if (TabletUI.view == null || TabletUI.view.Window != window || TabletUI.view.Panel == null)
			{
				return true;
			}
			if (panel == TabletUI.view.Panel)
			{
				return true;
			}
			panel = TabletUI.view.Panel;
			return finishing || window.CurrentPanel != panel;
		}

		// Token: 0x06000034 RID: 52 RVA: 0x000030F1 File Offset: 0x000012F1
		private static bool BeforeSwitch(Window __instance, ref Panel panel)
		{
			return TabletUI.KeepOurPanel(__instance, ref panel, false);
		}

		// Token: 0x06000035 RID: 53 RVA: 0x000030FB File Offset: 0x000012FB
		private static bool BeforeFinish(Window __instance, ref Panel panel)
		{
			return TabletUI.KeepOurPanel(__instance, ref panel, true);
		}

		// Token: 0x06000036 RID: 54 RVA: 0x00003105 File Offset: 0x00001305
		private static void BeforeSummon(SummonActionItemQuickAccess __instance)
		{
			if (!TabletUI.summoning && TabletUI.view != null && __instance != null && __instance.DisplayName == "Social Tablet")
			{
				TabletUI.Detach();
			}
		}

		// Token: 0x06000037 RID: 55 RVA: 0x00003138 File Offset: 0x00001338
		internal static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(Window), "SwitchToPanel", new Type[]
			{
				typeof(Panel)
			}, null), new HarmonyMethod(typeof(TabletUI), "BeforeSwitch", null), null, null, null, null);
			harmony.Patch(AccessTools.Method(typeof(Window), "FinishSwitch", null, null), new HarmonyMethod(typeof(TabletUI), "BeforeFinish", null), null, null, null, null);
			harmony.Patch(AccessTools.Method(typeof(SummonActionItemQuickAccess), "Run", null, null), new HarmonyMethod(typeof(TabletUI), "BeforeSummon", null), null, null, null, null);
			harmony.Patch(AccessTools.Method(typeof(SocialTablet), "StartFade", null, null), null, new HarmonyMethod(typeof(TabletUI), "AfterFade", null), null, null, null);
		}

		// Token: 0x0400000D RID: 13
		private static TabletView view;

		// Token: 0x0400000E RID: 14
		private static SocialTablet tablet;

		// Token: 0x0400000F RID: 15
		private static Func<Page> home;

		// Token: 0x04000010 RID: 16
		private static Func<Page> current;

		// Token: 0x04000011 RID: 17
		private static Controller lastController;

		// Token: 0x04000012 RID: 18
		private static List<Func<Page>> history = new List<Func<Page>>();

		// Token: 0x04000013 RID: 19
		private static float waitUntil = -1f;

		// Token: 0x04000014 RID: 20
		private static float nextRefresh;

		// Token: 0x04000015 RID: 21
		private static bool summoning;

		// Token: 0x04000016 RID: 22
		private static Dictionary<IntPtr, SocialTablet> leaving = new Dictionary<IntPtr, SocialTablet>();

		// Token: 0x0200001B RID: 27
		[CompilerGenerated]
		private static class <>O
		{
			// Token: 0x04000066 RID: 102
			public static Action <0>__Back;

			// Token: 0x04000067 RID: 103
			public static Action <1>__GoHome;

			// Token: 0x04000068 RID: 104
			public static Action <2>__Refresh;
		}
	}
}
