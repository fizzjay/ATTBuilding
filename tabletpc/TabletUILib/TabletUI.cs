using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Alta.Meta.UI;
using Alta.QuickAccessActions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000009 RID: 9
	public static class TabletUI
	{
		// Token: 0x17000003 RID: 3
		// (get) Token: 0x0600003D RID: 61 RVA: 0x00003456 File Offset: 0x00001656
		public static bool IsOpen
		{
			get
			{
				return TabletUI.view != null || TabletUI.waitUntil > 0f;
			}
		}

		// Token: 0x0600003E RID: 62 RVA: 0x0000346D File Offset: 0x0000166D
		public static void AddApp(string name, string icon, Func<Page> homePage)
		{
			TabletWheel.Apps.App(name, string.IsNullOrEmpty(icon) ? "APP" : icon, homePage);
		}

		// Token: 0x0600003F RID: 63 RVA: 0x0000348C File Offset: 0x0000168C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void AddServerApp(string name, string icon, Func<Page> homePage, string mod = null)
		{
			TabletWheel.Apps.ServerApp(name, string.IsNullOrEmpty(icon) ? "APP" : icon, homePage, mod ?? TabletBubble.ModOf(Assembly.GetCallingAssembly()));
		}

		// Token: 0x06000040 RID: 64 RVA: 0x000034BA File Offset: 0x000016BA
		public static TabletBubble AddBubble(string name, string icon)
		{
			return TabletWheel.Add(name, icon);
		}

		// Token: 0x17000004 RID: 4
		// (get) Token: 0x06000041 RID: 65 RVA: 0x000034C3 File Offset: 0x000016C3
		public static TabletBubble Wheel
		{
			get
			{
				return TabletWheel.Wheel;
			}
		}

		// Token: 0x06000042 RID: 66 RVA: 0x000034CC File Offset: 0x000016CC
		private static SocialTablet FindTablet()
		{
			TabletUI.leaving.RemoveWhere((SocialTablet x) => x == null);
			foreach (SocialTablet socialTablet in Object.FindObjectsOfType<SocialTablet>())
			{
				if (socialTablet != null && socialTablet.IsLocalPlayer && socialTablet.gameObject.activeInHierarchy && !TabletUI.leaving.Contains(socialTablet))
				{
					return socialTablet;
				}
			}
			return null;
		}

		// Token: 0x06000043 RID: 67 RVA: 0x0000354C File Offset: 0x0000174C
		private static SummonActionItemQuickAccess SocialTabletAction()
		{
			if (ActionItemsManager.Instance == null)
			{
				return null;
			}
			ActionItemData[] array = TabletUI.actionItemsField.GetValue(ActionItemsManager.Instance) as ActionItemData[];
			if (array == null)
			{
				return null;
			}
			foreach (ActionItemData actionItemData in array)
			{
				if (actionItemData != null && actionItemData.Action != null && actionItemData.Action.DisplayName == "Social Tablet")
				{
					return actionItemData.Action;
				}
			}
			return null;
		}

		// Token: 0x06000044 RID: 68 RVA: 0x000035CC File Offset: 0x000017CC
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

		// Token: 0x06000045 RID: 69 RVA: 0x0000361C File Offset: 0x0000181C
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

		// Token: 0x06000046 RID: 70 RVA: 0x000036C0 File Offset: 0x000018C0
		private static void Show(bool keepPage)
		{
			if (TabletUI.view != null && TabletUI.current != null)
			{
				TabletUI.view.Show(TabletUI.MakePage(), keepPage);
			}
		}

		// Token: 0x06000047 RID: 71 RVA: 0x000036E0 File Offset: 0x000018E0
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

		// Token: 0x06000048 RID: 72 RVA: 0x00003708 File Offset: 0x00001908
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

		// Token: 0x06000049 RID: 73 RVA: 0x0000375E File Offset: 0x0000195E
		public static void GoHome()
		{
			TabletUI.history.Clear();
			TabletUI.current = TabletUI.home;
			TabletUI.Show(false);
		}

		// Token: 0x0600004A RID: 74 RVA: 0x0000377A File Offset: 0x0000197A
		public static void Refresh()
		{
			TabletUI.Show(true);
		}

		// Token: 0x0600004B RID: 75 RVA: 0x00003782 File Offset: 0x00001982
		public static bool IsShowing(Func<Page> homePage)
		{
			return TabletUI.IsOpen && TabletUI.home == homePage;
		}

		// Token: 0x0600004C RID: 76 RVA: 0x00003798 File Offset: 0x00001998
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

		// Token: 0x0600004D RID: 77 RVA: 0x000037DA File Offset: 0x000019DA
		public static void Close()
		{
			bool flag = TabletUI.view != null;
			TabletUI.Detach();
			if (flag && TabletUI.lastController != null && TabletUI.FindTablet() != null)
			{
				TabletUI.Summon(TabletUI.lastController);
			}
		}

		// Token: 0x0600004E RID: 78 RVA: 0x00003810 File Offset: 0x00001A10
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

		// Token: 0x0600004F RID: 79 RVA: 0x00003878 File Offset: 0x00001A78
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
				if (componentInChildren != null && !(bool)TabletUI.setupField.GetValue(componentInChildren))
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

		// Token: 0x06000050 RID: 80 RVA: 0x00003984 File Offset: 0x00001B84
		internal static void Tick()
		{
			if (TabletUI.view != null)
			{
				if (TabletUI.tablet == null || !TabletUI.tablet.gameObject.activeInHierarchy || TabletUI.leaving.Contains(TabletUI.tablet))
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

		// Token: 0x06000051 RID: 81 RVA: 0x00003A32 File Offset: 0x00001C32
		private static void AfterFade(SocialTablet __instance, bool isFadingIn)
		{
			if (__instance == null)
			{
				return;
			}
			if (isFadingIn)
			{
				TabletUI.leaving.Remove(__instance);
				return;
			}
			TabletUI.leaving.Add(__instance);
		}

		// Token: 0x06000052 RID: 82 RVA: 0x00003A5C File Offset: 0x00001C5C
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

		// Token: 0x06000053 RID: 83 RVA: 0x00003AC9 File Offset: 0x00001CC9
		private static bool BeforeSwitch(Window __instance, ref Panel panel)
		{
			return TabletUI.KeepOurPanel(__instance, ref panel, false);
		}

		// Token: 0x06000054 RID: 84 RVA: 0x00003AD3 File Offset: 0x00001CD3
		private static bool BeforeFinish(Window __instance, ref Panel panel)
		{
			return TabletUI.KeepOurPanel(__instance, ref panel, true);
		}

		// Token: 0x06000055 RID: 85 RVA: 0x00003ADD File Offset: 0x00001CDD
		private static void BeforeSummon(SummonActionItemQuickAccess __instance)
		{
			if (!TabletUI.summoning && TabletUI.view != null && __instance != null && __instance.DisplayName == "Social Tablet")
			{
				TabletUI.Detach();
			}
		}

		// Token: 0x06000056 RID: 86 RVA: 0x00003B10 File Offset: 0x00001D10
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

		// Token: 0x0400001D RID: 29
		private static FieldInfo actionItemsField = AccessTools.Field(typeof(ActionItemsManager), "actionItems");

		// Token: 0x0400001E RID: 30
		private static FieldInfo setupField = AccessTools.Field(typeof(TouchScreenMenuBase), "isSetupLocal");

		// Token: 0x0400001F RID: 31
		private static TabletView view;

		// Token: 0x04000020 RID: 32
		private static SocialTablet tablet;

		// Token: 0x04000021 RID: 33
		private static Func<Page> home;

		// Token: 0x04000022 RID: 34
		private static Func<Page> current;

		// Token: 0x04000023 RID: 35
		private static Controller lastController;

		// Token: 0x04000024 RID: 36
		private static List<Func<Page>> history = new List<Func<Page>>();

		// Token: 0x04000025 RID: 37
		private static float waitUntil = -1f;

		// Token: 0x04000026 RID: 38
		private static float nextRefresh;

		// Token: 0x04000027 RID: 39
		private static bool summoning;

		// Token: 0x04000028 RID: 40
		private static HashSet<SocialTablet> leaving = new HashSet<SocialTablet>();

		// Token: 0x02000029 RID: 41
		[CompilerGenerated]
		private static class <>O
		{
			// Token: 0x04000098 RID: 152
			public static Action <0>__Back;

			// Token: 0x04000099 RID: 153
			public static Action <1>__GoHome;

			// Token: 0x0400009A RID: 154
			public static Action <2>__Refresh;
		}
	}
}
