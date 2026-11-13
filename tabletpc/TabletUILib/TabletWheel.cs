using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.QuickAccessActions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x0200000E RID: 14
	internal static class TabletWheel
	{
		// Token: 0x1700000D RID: 13
		// (get) Token: 0x06000090 RID: 144 RVA: 0x0000568C File Offset: 0x0000388C
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

		// Token: 0x1700000E RID: 14
		// (get) Token: 0x06000091 RID: 145 RVA: 0x000056B0 File Offset: 0x000038B0
		internal static IEnumerable<BubbleEntry> ServerEntries
		{
			get
			{
				return from e in TabletWheel.bubbles.Concat(new TabletBubble[]
				{
					TabletWheel.Wheel
				}).SelectMany((TabletBubble b) => b.All)
				where e.Server != null
				select e;
			}
		}

		// Token: 0x06000092 RID: 146 RVA: 0x00005720 File Offset: 0x00003920
		internal static TabletBubble Add(string name, string icon)
		{
			TabletBubble tabletBubble = new TabletBubble(name, icon);
			TabletWheel.bubbles.Add(tabletBubble);
			return tabletBubble;
		}

		// Token: 0x06000093 RID: 147 RVA: 0x00005741 File Offset: 0x00003941
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

		// Token: 0x06000094 RID: 148 RVA: 0x00005778 File Offset: 0x00003978
		private static Object MakeIcon(Object template, string label, Texture2D image)
		{
			if (template == null)
			{
				return null;
			}
			Object @object = Object.Instantiate(template);
			GameObject gameObject = @object as GameObject;
			if (gameObject == null)
			{
				Component component = @object as Component;
				if (component != null)
				{
					gameObject = component.gameObject;
				}
			}
			if (gameObject == null)
			{
				return null;
			}
			gameObject.transform.SetParent(TabletWheel.Holder(), false);
			Renderer[] componentsInChildren = gameObject.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				componentsInChildren[i].enabled = false;
			}
			if (image != null && Icons.Add(gameObject.transform, image))
			{
				return @object;
			}
			GameObject gameObject2 = new GameObject("TabletUILib icon");
			gameObject2.transform.SetParent(gameObject.transform, false);
			gameObject2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
			TextMesh textMesh = gameObject2.AddComponent<TextMesh>();
			textMesh.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			textMesh.GetComponent<MeshRenderer>().sharedMaterial = textMesh.font.material;
			textMesh.text = label;
			textMesh.anchor = 4;
			textMesh.alignment = 1;
			textMesh.fontStyle = 1;
			textMesh.fontSize = 64;
			textMesh.characterSize = ((label.Length > 3) ? 0.01f : 0.0125f);
			textMesh.color = Color.white;
			return @object;
		}

		// Token: 0x06000095 RID: 149 RVA: 0x000058C8 File Offset: 0x00003AC8
		private static void Setup(QuickAccessMenuAction action, QuickAccessMenuAction template, string name, string label, Texture2D image)
		{
			Object @object = (Object)TabletWheel.prefabField.GetValue(template);
			TabletWheel.prefabField.SetValue(action, TabletWheel.MakeIcon(@object, string.IsNullOrEmpty(label) ? "?" : label, image) ?? @object);
			TabletWheel.nameField.SetValue(action, name);
		}

		// Token: 0x06000096 RID: 150 RVA: 0x0000591A File Offset: 0x00003B1A
		private static BubbleAction MakeButton(QuickAccessMenuAction template, BubbleEntry entry)
		{
			BubbleAction bubbleAction = ScriptableObject.CreateInstance<BubbleAction>();
			bubbleAction.Entry = entry;
			TabletWheel.Setup(bubbleAction, template, entry.Name, entry.Icon, entry.Picture);
			return bubbleAction;
		}

		// Token: 0x06000097 RID: 151 RVA: 0x00005944 File Offset: 0x00003B44
		private static NestingMenuAction MakeBubble(NestingMenuAction nest, QuickAccessMenuAction template, TabletBubble bubble)
		{
			NestingMenuAction nestingMenuAction = Object.Instantiate<NestingMenuAction>(nest);
			TabletWheel.Setup(nestingMenuAction, template, bubble.Name, bubble.Icon, bubble.Picture);
			TabletWheel.nestedField.SetValue(nestingMenuAction, (from e in bubble.Entries
			select TabletWheel.MakeEntry(nest, template, e)).ToArray<QuickAccessMenuAction>());
			TabletWheel.bubblesField.SetValue(nestingMenuAction, new List<QuickAccessMenuBubble>());
			TabletWheel.ours.Add(nestingMenuAction);
			return nestingMenuAction;
		}

		// Token: 0x06000098 RID: 152 RVA: 0x000059D3 File Offset: 0x00003BD3
		private static QuickAccessMenuAction MakeEntry(NestingMenuAction nest, QuickAccessMenuAction template, BubbleEntry entry)
		{
			if (entry.Sub != null)
			{
				return TabletWheel.MakeBubble(nest, template, entry.Sub);
			}
			return TabletWheel.MakeButton(template, entry);
		}

		// Token: 0x06000099 RID: 153 RVA: 0x000059F4 File Offset: 0x00003BF4
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
			QuickAccessMenuAction[] array = (QuickAccessMenuAction[])TabletWheel.menusField.GetValue(controller);
			if (array == null || array.Length == 0)
			{
				return;
			}
			TabletWheel.done.Add(instanceID);
			QuickAccessMenuAction[] array2 = (from m in array
			where m != null
			select m).ToArray<QuickAccessMenuAction>();
			NestingMenuAction nestingMenuAction = array2.OfType<NestingMenuAction>().FirstOrDefault<NestingMenuAction>();
			if (nestingMenuAction == null)
			{
				MelonLogger.Warning("no submenu to copy so there are no mod bubbles");
				return;
			}
			List<QuickAccessMenuAction> list = array.ToList<QuickAccessMenuAction>();
			using (List<TabletBubble>.Enumerator enumerator = TabletWheel.bubbles.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					TabletBubble bubble = enumerator.Current;
					if (bubble.Entries.Count != 0 && !list.Any((QuickAccessMenuAction m) => m != null && m.DisplayName == bubble.Name))
					{
						list.Add(TabletWheel.MakeBubble(nestingMenuAction, array2[0], bubble));
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
						list.Add(TabletWheel.MakeEntry(nestingMenuAction, array2[0], entry));
						MelonLogger.Msg("added " + entry.Name + " to the wheel");
					}
				}
			}
			TabletWheel.menusField.SetValue(controller, list.ToArray());
		}

		// Token: 0x0600009A RID: 154 RVA: 0x00005C10 File Offset: 0x00003E10
		private static void AfterInit(QuickAccessMenuController __instance)
		{
			try
			{
				TabletWheel.AddBubbles(__instance);
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("couldnt add the mod bubbles: " + ex.Message);
			}
		}

		// Token: 0x0600009B RID: 155 RVA: 0x00005C50 File Offset: 0x00003E50
		private static void AfterSpawn(NestingMenuAction __instance, Vector3 __0)
		{
			if (!TabletWheel.ours.Contains(__instance))
			{
				return;
			}
			List<QuickAccessMenuBubble> list = TabletWheel.bubblesField.GetValue(__instance) as List<QuickAccessMenuBubble>;
			if (list == null)
			{
				return;
			}
			foreach (QuickAccessMenuBubble quickAccessMenuBubble in list)
			{
				if (!(quickAccessMenuBubble == null))
				{
					quickAccessMenuBubble.transform.position = __0 + (quickAccessMenuBubble.transform.position - __0) * 1.65f;
				}
			}
		}

		// Token: 0x0600009C RID: 156 RVA: 0x00005CF0 File Offset: 0x00003EF0
		internal static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(QuickAccessMenuController), "InitializeControllerExtension", null, null), null, new HarmonyMethod(typeof(TabletWheel), "AfterInit", null), null, null, null);
			harmony.Patch(AccessTools.Method(typeof(NestingMenuAction), "SpawnNestedBubbles", null, null), null, new HarmonyMethod(typeof(TabletWheel), "AfterSpawn", null), null, null, null);
		}

		// Token: 0x04000056 RID: 86
		private static FieldInfo menusField = AccessTools.Field(typeof(QuickAccessMenuController), "quickAccessMenus");

		// Token: 0x04000057 RID: 87
		private static FieldInfo nestedField = AccessTools.Field(typeof(NestingMenuAction), "nestedActions");

		// Token: 0x04000058 RID: 88
		private static FieldInfo bubblesField = AccessTools.Field(typeof(NestingMenuAction), "spawnedBubbles");

		// Token: 0x04000059 RID: 89
		private static FieldInfo prefabField = AccessTools.Field(typeof(QuickAccessMenuAction), "prefab");

		// Token: 0x0400005A RID: 90
		private static FieldInfo nameField = AccessTools.Field(typeof(QuickAccessMenuAction), "displayName");

		// Token: 0x0400005B RID: 91
		private static List<TabletBubble> bubbles = new List<TabletBubble>();

		// Token: 0x0400005C RID: 92
		private static TabletBubble apps;

		// Token: 0x0400005D RID: 93
		internal static TabletBubble Wheel = new TabletBubble("wheel", null);

		// Token: 0x0400005E RID: 94
		private static Transform holder;

		// Token: 0x0400005F RID: 95
		private static HashSet<int> done = new HashSet<int>();

		// Token: 0x04000060 RID: 96
		private static HashSet<NestingMenuAction> ours = new HashSet<NestingMenuAction>();
	}
}
