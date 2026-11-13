using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppAlta.QuickAccessActions;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x0200000B RID: 11
	internal static class TabletWheel
	{
		// Token: 0x17000004 RID: 4
		// (get) Token: 0x0600006D RID: 109 RVA: 0x00004C07 File Offset: 0x00002E07
		internal static TabletBubble Apps
		{
			get
			{
				if (TabletWheel.apps == null)
				{
					TabletWheel.apps = TabletWheel.Add("TabletUILib", "APPS");
				}
				return TabletWheel.apps;
			}
		}

		// Token: 0x0600006E RID: 110 RVA: 0x00004C2C File Offset: 0x00002E2C
		internal static TabletBubble Add(string name, string icon)
		{
			TabletBubble tabletBubble = new TabletBubble(name, icon);
			TabletWheel.bubbles.Add(tabletBubble);
			return tabletBubble;
		}

		// Token: 0x0600006F RID: 111 RVA: 0x00004C4D File Offset: 0x00002E4D
		private static Transform Holder()
		{
			if (TabletWheel.holder == null)
			{
				GameObject gameObject = new GameObject("TabletUILib wheel icons");
				gameObject.SetActive(false);
				Object.DontDestroyOnLoad(gameObject);
				TabletWheel.holder = gameObject.transform;
			}
			return TabletWheel.holder;
		}

		// Token: 0x06000070 RID: 112 RVA: 0x00004C84 File Offset: 0x00002E84
		private static bool GameLabel(Transform parent, string label)
		{
			if (TabletWheel.labelSource == null)
			{
				return false;
			}
			bool result;
			try
			{
				GameObject gameObject = Object.Instantiate<GameObject>(TabletWheel.labelSource.gameObject, parent, false);
				gameObject.name = "TabletUILib label";
				gameObject.SetActive(true);
				gameObject.transform.localPosition = Vector3.zero;
				gameObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
				TextRenderer component = gameObject.GetComponent<TextRenderer>();
				component.Text = label;
				component.SetColor(Color.white);
				foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
				{
					renderer.enabled = true;
				}
				result = true;
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("couldnt copy the wheel title for a label: " + ex.Message);
				result = false;
			}
			return result;
		}

		// Token: 0x06000071 RID: 113 RVA: 0x00004D74 File Offset: 0x00002F74
		private static GameObject MakeIcon(GameObject template, string label, Texture2D image)
		{
			if (template == null)
			{
				return null;
			}
			GameObject gameObject = Object.Instantiate<GameObject>(template);
			gameObject.transform.SetParent(TabletWheel.Holder(), false);
			foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
			{
				renderer.enabled = false;
			}
			if (image != null && Icons.Add(gameObject.transform, image))
			{
				return gameObject;
			}
			GameObject gameObject2 = new GameObject("TabletUILib icon");
			gameObject2.transform.SetParent(gameObject.transform, false);
			gameObject2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
			TextMesh textMesh = null;
			try
			{
				if (TabletWheel.font == null)
				{
					Object builtinResource = Resources.GetBuiltinResource(Il2CppType.Of<Font>(), "Arial.ttf");
					TabletWheel.font = ((builtinResource != null) ? builtinResource.TryCast<Font>() : null);
				}
				textMesh = gameObject2.AddComponent<TextMesh>();
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("no TextMesh on this build: " + ex.Message);
			}
			if (textMesh == null)
			{
				Object.Destroy(gameObject2);
				if (!TabletWheel.GameLabel(gameObject.transform, label))
				{
					foreach (Renderer renderer2 in gameObject.GetComponentsInChildren<Renderer>(true))
					{
						renderer2.enabled = true;
					}
				}
				return gameObject;
			}
			if (TabletWheel.font != null)
			{
				textMesh.font = TabletWheel.font;
				textMesh.GetComponent<MeshRenderer>().sharedMaterial = TabletWheel.font.material;
			}
			textMesh.text = label;
			textMesh.anchor = 4;
			textMesh.alignment = 1;
			textMesh.fontStyle = 1;
			textMesh.fontSize = 64;
			textMesh.characterSize = ((label.Length > 3) ? 0.01f : 0.0125f);
			textMesh.color = Color.white;
			return gameObject;
		}

		// Token: 0x06000072 RID: 114 RVA: 0x00004F68 File Offset: 0x00003168
		private static void Setup(QuickAccessMenuAction action, QuickAccessMenuAction template, string name, string label, Texture2D image)
		{
			GameObject prefab = template.prefab;
			action.prefab = (TabletWheel.MakeIcon(prefab, string.IsNullOrEmpty(label) ? "?" : label, image) ?? prefab);
			action.displayName = name;
		}

		// Token: 0x06000073 RID: 115 RVA: 0x00004FA8 File Offset: 0x000031A8
		private static QuickAccessMenuAction MakeButton(NestingMenuAction nest, QuickAccessMenuAction template, BubbleEntry entry)
		{
			NestingMenuAction nestingMenuAction = Object.Instantiate<NestingMenuAction>(nest);
			nestingMenuAction.nestedActions = new Il2CppReferenceArray<QuickAccessMenuAction>(Array.Empty<QuickAccessMenuAction>());
			nestingMenuAction.spawnedBubbles = new List<QuickAccessMenuBubble>();
			TabletWheel.Setup(nestingMenuAction, template, entry.Name, entry.Icon, entry.Picture);
			BubbleAction.Add(nestingMenuAction, entry);
			return nestingMenuAction;
		}

		// Token: 0x06000074 RID: 116 RVA: 0x00004FF8 File Offset: 0x000031F8
		private static NestingMenuAction MakeBubble(NestingMenuAction nest, QuickAccessMenuAction template, TabletBubble bubble)
		{
			NestingMenuAction nestingMenuAction = Object.Instantiate<NestingMenuAction>(nest);
			TabletWheel.Setup(nestingMenuAction, template, bubble.Name, bubble.Icon, bubble.Picture);
			nestingMenuAction.nestedActions = new Il2CppReferenceArray<QuickAccessMenuAction>((from e in bubble.Entries
			select TabletWheel.MakeEntry(nest, template, e)).ToArray<QuickAccessMenuAction>());
			nestingMenuAction.spawnedBubbles = new List<QuickAccessMenuBubble>();
			TabletWheel.ours.Add(nestingMenuAction.Pointer);
			return nestingMenuAction;
		}

		// Token: 0x06000075 RID: 117 RVA: 0x00005087 File Offset: 0x00003287
		private static QuickAccessMenuAction MakeEntry(NestingMenuAction nest, QuickAccessMenuAction template, BubbleEntry entry)
		{
			if (entry.Sub != null)
			{
				return TabletWheel.MakeBubble(nest, template, entry.Sub);
			}
			return TabletWheel.MakeButton(nest, template, entry);
		}

		// Token: 0x06000076 RID: 118 RVA: 0x000050A8 File Offset: 0x000032A8
		private static void AddBubbles(QuickAccessMenuController controller)
		{
			if (controller == null || (TabletWheel.bubbles.Count == 0 && TabletWheel.Wheel.Entries.Count == 0))
			{
				return;
			}
			int instanceID = controller.GetInstanceID();
			if (TabletWheel.done.Contains(instanceID))
			{
				return;
			}
			Il2CppReferenceArray<QuickAccessMenuAction> quickAccessMenus = controller.quickAccessMenus;
			if (quickAccessMenus == null || quickAccessMenus.Length == 0)
			{
				return;
			}
			TabletWheel.done.Add(instanceID);
			if (TabletWheel.labelSource == null)
			{
				TabletWheel.labelSource = controller.CenteredTitleDisplay;
			}
			QuickAccessMenuAction[] array = (from m in quickAccessMenus
			where m != null
			select m).ToArray<QuickAccessMenuAction>();
			NestingMenuAction nestingMenuAction = (from m in array
			select m.TryCast<NestingMenuAction>()).FirstOrDefault((NestingMenuAction n) => n != null);
			if (nestingMenuAction == null)
			{
				MelonLogger.Warning("no submenu to copy so there are no mod bubbles");
				return;
			}
			List<QuickAccessMenuAction> list = quickAccessMenus.ToList<QuickAccessMenuAction>();
			using (List<TabletBubble>.Enumerator enumerator = TabletWheel.bubbles.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					TabletBubble bubble = enumerator.Current;
					if (bubble.Entries.Count != 0 && !list.Any((QuickAccessMenuAction m) => m != null && m.DisplayName == bubble.Name))
					{
						list.Add(TabletWheel.MakeBubble(nestingMenuAction, array[0], bubble));
						MelonLogger.Msg("added " + bubble.Name + " bubble to the wheel");
					}
				}
			}
			using (List<BubbleEntry>.Enumerator enumerator2 = TabletWheel.Wheel.Entries.GetEnumerator())
			{
				while (enumerator2.MoveNext())
				{
					BubbleEntry entry = enumerator2.Current;
					if (!list.Any((QuickAccessMenuAction m) => m != null && m.DisplayName == entry.Name))
					{
						list.Add(TabletWheel.MakeEntry(nestingMenuAction, array[0], entry));
						MelonLogger.Msg("added " + entry.Name + " to the wheel");
					}
				}
			}
			controller.quickAccessMenus = new Il2CppReferenceArray<QuickAccessMenuAction>(list.ToArray());
		}

		// Token: 0x06000077 RID: 119 RVA: 0x00005314 File Offset: 0x00003514
		private static void AfterInit(QuickAccessMenuController __instance)
		{
			try
			{
				TabletWheel.AddBubbles(__instance);
			}
			catch (Exception ex)
			{
				string str = "couldnt add the mod bubbles: ";
				Exception ex2 = ex;
				MelonLogger.Warning(str + ((ex2 != null) ? ex2.ToString() : null));
			}
		}

		// Token: 0x06000078 RID: 120 RVA: 0x00005358 File Offset: 0x00003558
		private static void AfterSpawn(NestingMenuAction __instance, Vector3 __0)
		{
			if (__instance == null || !TabletWheel.ours.Contains(__instance.Pointer))
			{
				return;
			}
			List<QuickAccessMenuBubble> spawnedBubbles = __instance.spawnedBubbles;
			if (spawnedBubbles == null)
			{
				return;
			}
			for (int i = 0; i < spawnedBubbles.Count; i++)
			{
				QuickAccessMenuBubble quickAccessMenuBubble = spawnedBubbles[i];
				if (!(quickAccessMenuBubble == null))
				{
					quickAccessMenuBubble.transform.position = __0 + (quickAccessMenuBubble.transform.position - __0) * 1.65f;
				}
			}
		}

		// Token: 0x06000079 RID: 121 RVA: 0x000053DC File Offset: 0x000035DC
		internal static void Patch(Harmony harmony)
		{
			BubbleAction.Patch(harmony);
			harmony.Patch(AccessTools.Method(typeof(QuickAccessMenuController), "InitializeControllerExtension", null, null), null, new HarmonyMethod(typeof(TabletWheel), "AfterInit", null), null, null, null);
			harmony.Patch(AccessTools.Method(typeof(NestingMenuAction), "SpawnNestedBubbles", null, null), null, new HarmonyMethod(typeof(TabletWheel), "AfterSpawn", null), null, null, null);
		}

		// Token: 0x0400003D RID: 61
		private static List<TabletBubble> bubbles = new List<TabletBubble>();

		// Token: 0x0400003E RID: 62
		private static TabletBubble apps;

		// Token: 0x0400003F RID: 63
		internal static TabletBubble Wheel = new TabletBubble("wheel", null);

		// Token: 0x04000040 RID: 64
		private static Transform holder;

		// Token: 0x04000041 RID: 65
		private static HashSet<int> done = new HashSet<int>();

		// Token: 0x04000042 RID: 66
		private static HashSet<IntPtr> ours = new HashSet<IntPtr>();

		// Token: 0x04000043 RID: 67
		private static Font font;

		// Token: 0x04000044 RID: 68
		private static TextRenderer labelSource;
	}
}
