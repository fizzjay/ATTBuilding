using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Meta.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;

namespace TabletUILib
{
	// Token: 0x0200000A RID: 10
	internal class TabletView
	{
		// Token: 0x06000058 RID: 88 RVA: 0x00003C65 File Offset: 0x00001E65
		private static Transform Find(Transform parent, string path)
		{
			Transform transform = parent.Find(path);
			if (transform == null)
			{
				throw new Exception("tablet is missing " + path);
			}
			return transform;
		}

		// Token: 0x06000059 RID: 89 RVA: 0x00003C88 File Offset: 0x00001E88
		private static bool IsTouch(Transform t)
		{
			return t.GetComponent<Button>() is ITouchScreenButton;
		}

		// Token: 0x0600005A RID: 90 RVA: 0x00003C98 File Offset: 0x00001E98
		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType.IsInstanceOfType(value))
			{
				field.SetValue(target, value);
			}
		}

		// Token: 0x0600005B RID: 91 RVA: 0x00003CD4 File Offset: 0x00001ED4
		private static void FitText(TextRenderer text, float worldWidth, int lines)
		{
			TabletView.SetField(text, "supportsHTMLTags", false);
			TabletView.SetField(text, "isMultiLine", lines > 1);
			float num = Mathf.Abs(text.transform.lossyScale.x);
			if (worldWidth <= 0f || num <= 0f)
			{
				return;
			}
			float num2 = worldWidth / num;
			TabletView.SetField(text, "maxWidth", num2);
			TabletView.SetField(text, "bestFitToWidth", lines == 1);
			TabletView.SetField(text, "hasLineLimit", true);
			TabletView.SetField(text, "maxLines", lines);
			FieldInfo field = text.GetType().GetField("scale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			float num3 = (field != null) ? ((float)field.GetValue(text)) : 0f;
			if (num3 > 0f)
			{
				TabletView.SetField(text, "lineWidth", num2 / num3);
				TabletView.SetField(text, "maxScale", num3);
				TabletView.SetField(text, "minScale", num3 * 0.65f);
			}
		}

		// Token: 0x0600005C RID: 92 RVA: 0x00003DEC File Offset: 0x00001FEC
		private static void ClearEvents(Component c)
		{
			Type type = c.GetType();
			while (type != null && type != typeof(MonoBehaviour))
			{
				foreach (FieldInfo fieldInfo in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (!fieldInfo.IsInitOnly)
					{
						if (typeof(Delegate).IsAssignableFrom(fieldInfo.FieldType))
						{
							fieldInfo.SetValue(c, null);
						}
						else if (typeof(UnityEventBase).IsAssignableFrom(fieldInfo.FieldType) || fieldInfo.FieldType == typeof(AltaEvent))
						{
							fieldInfo.SetValue(c, Activator.CreateInstance(fieldInfo.FieldType));
						}
						else if (fieldInfo.Name == "<State>k__BackingField")
						{
							fieldInfo.SetValue(c, false);
						}
					}
				}
				type = type.BaseType;
			}
		}

		// Token: 0x0600005D RID: 93 RVA: 0x00003ED8 File Offset: 0x000020D8
		private static GameObject CopyObject(Transform source, Transform parent, string name)
		{
			GameObject gameObject = Object.Instantiate<GameObject>(source.gameObject, parent, false);
			gameObject.name = "TabletUILib " + name;
			gameObject.SetActive(false);
			gameObject.transform.localRotation = Quaternion.Inverse(parent.rotation) * source.rotation;
			Vector3 lossyScale = source.lossyScale;
			Vector3 lossyScale2 = parent.lossyScale;
			gameObject.transform.localScale = new Vector3(lossyScale.x / lossyScale2.x, lossyScale.y / lossyScale2.y, lossyScale.z / lossyScale2.z);
			foreach (MonoBehaviour monoBehaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (!(monoBehaviour == null))
				{
					if (Array.IndexOf<Type>(TabletView.keepTypes, monoBehaviour.GetType()) < 0)
					{
						Object.DestroyImmediate(monoBehaviour);
					}
					else
					{
						TabletView.ClearEvents(monoBehaviour);
					}
				}
			}
			gameObject.SetActive(true);
			return gameObject;
		}

		// Token: 0x0600005E RID: 94 RVA: 0x00003FC8 File Offset: 0x000021C8
		private static TextRenderer CopyText(Transform source, Transform parent, string name, Color color, float scale)
		{
			GameObject gameObject = TabletView.CopyObject(source, parent, name);
			TextRenderer component = gameObject.GetComponent<TextRenderer>();
			gameObject.transform.localScale *= scale;
			TabletView.FitText(component, 0f, 1);
			component.SetColor(color);
			return component;
		}

		// Token: 0x0600005F RID: 95 RVA: 0x0000400F File Offset: 0x0000220F
		private static string Escape(string text)
		{
			return (text ?? "").Replace("\\", "\\\\");
		}

		// Token: 0x06000060 RID: 96 RVA: 0x0000402A File Offset: 0x0000222A
		private static void SetText(TextRenderer text, string value)
		{
			text.Text = TabletView.Escape(value);
		}

		// Token: 0x06000061 RID: 97 RVA: 0x00004038 File Offset: 0x00002238
		private static void SetVisible(Button button, bool on)
		{
			button.IsEnabled = on;
			button.gameObject.SetActive(on);
		}

		// Token: 0x06000062 RID: 98 RVA: 0x00004050 File Offset: 0x00002250
		private Button CopyButton(Transform source, string name, Vector3 pos)
		{
			GameObject gameObject = TabletView.CopyObject(source, this.root.transform, name);
			gameObject.transform.localPosition = pos;
			Button component = gameObject.GetComponent<Button>();
			if (!(component is ITouchScreenButton))
			{
				throw new Exception(source.name + " isnt a touch button");
			}
			component.IsEnabled = true;
			return component;
		}

		// Token: 0x06000063 RID: 99 RVA: 0x000040A8 File Offset: 0x000022A8
		private void Run(Action action)
		{
			if (this.closed || action == null)
			{
				return;
			}
			try
			{
				action();
			}
			catch (Exception ex)
			{
				string str = "tablet button broke: ";
				Exception ex2 = ex;
				MelonLogger.Warning(str + ((ex2 != null) ? ex2.ToString() : null));
			}
		}

		// Token: 0x06000064 RID: 100 RVA: 0x000040FC File Offset: 0x000022FC
		private void Wire(Button button, Action click)
		{
			button.Released += delegate(Button b)
			{
				if (!this.closed && button.IsEnabled)
				{
					click();
				}
			};
		}

		// Token: 0x06000065 RID: 101 RVA: 0x0000413B File Offset: 0x0000233B
		private void Remap()
		{
			if (this.screen == null)
			{
				return;
			}
			this.screen.InstantiatedButtons.Clear();
			this.screen.MapButtons();
		}

		// Token: 0x06000066 RID: 102 RVA: 0x00004167 File Offset: 0x00002367
		private int PageCount()
		{
			if (this.page == null)
			{
				return 1;
			}
			return Mathf.Max(1, (this.page.Rows.Count + 4 - 1) / 4);
		}

		// Token: 0x06000067 RID: 103 RVA: 0x00004190 File Offset: 0x00002390
		private void Draw()
		{
			if (this.page == null)
			{
				return;
			}
			int num = this.PageCount();
			this.pageIndex = Mathf.Clamp(this.pageIndex, 0, num - 1);
			TabletView.SetText(this.title, this.page.Title);
			TabletView.SetText(this.status, this.page.Status);
			string text = this.page.Footer ?? "";
			if (num > 1)
			{
				text = (this.pageIndex + 1).ToString() + " / " + num.ToString() + ((text != "") ? ("   " + text) : "");
			}
			TabletView.SetText(this.footer, text);
			for (int i = 0; i < 4; i++)
			{
				int num2 = this.pageIndex * 4 + i;
				Row row = (num2 < this.page.Rows.Count) ? this.page.Rows[num2] : null;
				this.rowButtons[i].gameObject.SetActive(row != null);
				this.rowButtons[i].IsEnabled = (row != null && row.Click != null);
				this.rowButtons[i].Label = TabletView.Escape((row != null) ? row.Label : null);
				TabletView.SetText(this.rowLabels[i], (row != null) ? row.Label : null);
				TabletView.SetText(this.rowDetails[i], (row != null) ? row.Detail : null);
				this.rowDetails[i].gameObject.SetActive(row != null && !string.IsNullOrEmpty(row.Detail));
			}
			TabletView.SetVisible(this.back, this.page.Back != null);
			TabletView.SetVisible(this.prev, num > 1);
			TabletView.SetVisible(this.next, num > 1);
			this.Remap();
		}

		// Token: 0x06000068 RID: 104 RVA: 0x00004390 File Offset: 0x00002590
		private void ClickRow(int index)
		{
			int num = this.pageIndex * 4 + index;
			if (this.page == null || num >= this.page.Rows.Count)
			{
				return;
			}
			this.Run(this.page.Rows[num].Click);
			this.Run(this.AfterClick);
		}

		// Token: 0x06000069 RID: 105 RVA: 0x000043EC File Offset: 0x000025EC
		private void TurnPage(int dir)
		{
			int num = this.PageCount();
			this.pageIndex = (this.pageIndex + dir + num) % num;
			this.Draw();
		}

		// Token: 0x0600006A RID: 106 RVA: 0x00004418 File Offset: 0x00002618
		public void Show(Page p, bool keepPage)
		{
			if (this.closed)
			{
				return;
			}
			if (!keepPage || p == null || this.page == null || p.Title != this.page.Title)
			{
				this.pageIndex = 0;
			}
			this.page = p;
			this.Draw();
		}

		// Token: 0x0600006B RID: 107 RVA: 0x00004468 File Offset: 0x00002668
		private void Replace(Component target, FieldInfo field, object value)
		{
			if (field == null)
			{
				return;
			}
			this.swaps.Add(new TabletView.Swap
			{
				Target = target,
				Field = field,
				Original = field.GetValue(target)
			});
			field.SetValue(target, value);
		}

		// Token: 0x0600006C RID: 108 RVA: 0x000044A7 File Offset: 0x000026A7
		private void GoHome()
		{
			this.Run(this.Home);
		}

		// Token: 0x0600006D RID: 109 RVA: 0x000044B8 File Offset: 0x000026B8
		private void TakeOverHome(Transform buttons)
		{
			FieldInfo field = AccessTools.Field(typeof(BasicTouchScreenButton), "onPressed");
			FieldInfo field2 = AccessTools.Field(typeof(BasicTouchScreenButton), "onReleased");
			int num = 0;
			foreach (string text in new string[]
			{
				"Home Button",
				"Extended Home Button"
			})
			{
				Transform transform = buttons.Find(text);
				if (!(transform == null))
				{
					foreach (BasicTouchScreenButton target in transform.GetComponentsInChildren<BasicTouchScreenButton>(true))
					{
						AltaEvent altaEvent = new AltaEvent();
						altaEvent.Invoked += this.GoHome;
						this.Replace(target, field, new AltaEvent());
						this.Replace(target, field2, altaEvent);
						num++;
					}
					foreach (Button button in transform.GetComponentsInChildren<Button>(true))
					{
						this.Replace(button, AccessTools.Field(typeof(Button), "Pressed"), null);
						this.Replace(button, AccessTools.Field(typeof(Button), "Released"), new Action<Button>(delegate(Button x)
						{
							this.GoHome();
						}));
						if (button is ButtonWithEvents)
						{
							this.Replace(button, AccessTools.Field(typeof(ButtonWithEvents), "onPress"), new UnityEvent());
							this.Replace(button, AccessTools.Field(typeof(ButtonWithEvents), "onRelease"), new UnityEvent());
						}
						num++;
					}
				}
			}
			if (num == 0)
			{
				MelonLogger.Warning("no home button on the tablet");
			}
		}

		// Token: 0x0600006E RID: 110 RVA: 0x00004664 File Offset: 0x00002864
		private void Build(SocialTablet tablet)
		{
			this.Window = tablet.GetComponent<Window>();
			this.screen = tablet.GetComponentInChildren<TouchScreenMenuBase>(true);
			if (this.Window == null || this.screen == null)
			{
				throw new Exception("no window or touch screen on the tablet");
			}
			this.gamePanel = this.Window.CurrentPanel;
			Transform transform = TabletView.Find(this.screen.transform, "Components/Pages");
			Transform buttons = TabletView.Find(this.screen.transform, "Components/Buttons");
			Transform transform2 = TabletView.Find(transform, "Player Actions Page");
			Transform parent = TabletView.Find(transform, "Recent Players Page");
			Transform transform3 = TabletView.Find(transform2, "Add Friend Button");
			Transform transform4 = TabletView.Find(transform2, "Back Page Button");
			Transform transform5 = TabletView.Find(parent, "Title");
			Transform transform6 = TabletView.Find(parent, "Back Button");
			Transform transform7 = TabletView.Find(parent, "Forward Button");
			TextRenderer componentInChildren = transform3.GetComponentInChildren<TextRenderer>(true);
			ITouchScreenButton touchScreenButton = transform3.GetComponent<Button>() as ITouchScreenButton;
			if (componentInChildren == null || touchScreenButton == null)
			{
				throw new Exception("tablet row is missing its text or touch");
			}
			float num = touchScreenButton.ButtonSize.y * Mathf.Abs(this.screen.transform.lossyScale.y) / Mathf.Abs(transform2.lossyScale.y);
			this.root = new GameObject("TabletUILib page");
			this.root.layer = transform2.gameObject.layer;
			this.root.SetActive(false);
			this.root.transform.SetParent(transform, false);
			this.root.transform.localPosition = transform2.localPosition;
			this.root.transform.localRotation = transform2.localRotation;
			this.root.transform.localScale = transform2.localScale;
			this.Panel = this.root.AddComponent<Panel>();
			AccessTools.Field(typeof(Panel), "window").SetValue(this.Panel, this.Window);
			float y = transform2.InverseTransformPoint(transform5.position).y;
			Vector3 vector = transform2.InverseTransformPoint(transform6.position);
			Vector3 vector2 = transform2.InverseTransformPoint(transform7.position);
			float num2 = Mathf.Max(vector.y, vector2.y);
			float num3 = y - num * 1.5f;
			float num4 = num2 + num * 1.5f;
			float num5 = (num3 - num4) / 3f;
			if (num5 < num * 1.05f)
			{
				throw new Exception("not enough room on the tablet for the rows");
			}
			this.title = TabletView.CopyText(transform5, this.root.transform, "title", TabletView.headingColor, 1f);
			this.title.transform.localPosition = transform2.InverseTransformPoint(transform5.position);
			Vector3 vector3 = transform2.InverseTransformPoint(componentInChildren.transform.position);
			this.status = TabletView.CopyText(componentInChildren.transform, this.root.transform, "status", TabletView.textColor, 0.65f);
			this.status.transform.localPosition = new Vector3(vector3.x, y - (y - num3) * 0.6f, vector3.z);
			for (int i = 0; i < 4; i++)
			{
				int index = i;
				Button button = this.CopyButton(transform3, "row " + (i + 1).ToString(), new Vector3(transform3.localPosition.x, num3 - num5 * (float)i, transform3.localPosition.z));
				TextRenderer componentInChildren2 = button.GetComponentInChildren<TextRenderer>(true);
				componentInChildren2.SetColor(TabletView.textColor);
				Vector3 vector4 = button.transform.InverseTransformPoint(componentInChildren2.transform.position);
				Vector3 vector5 = button.transform.InverseTransformVector(this.root.transform.TransformVector(new Vector3(0f, num5, 0f)));
				componentInChildren2.transform.position = button.transform.TransformPoint(vector4 + vector5 * 0.13f);
				TextRenderer textRenderer = TabletView.CopyText(componentInChildren.transform, button.transform, "detail", TabletView.textColor, 0.65f);
				textRenderer.transform.localPosition = vector4 - vector5 * 0.18f;
				this.rowButtons[i] = button;
				this.rowLabels[i] = componentInChildren2;
				this.rowDetails[i] = textRenderer;
				this.Wire(button, delegate
				{
					this.ClickRow(index);
				});
			}
			this.prev = this.CopyButton(TabletView.IsTouch(transform6) ? transform6 : transform4, "previous", vector);
			this.next = this.CopyButton(TabletView.IsTouch(transform7) ? transform7 : transform4, "next", vector2);
			this.back = this.CopyButton(transform4, "back", new Vector3(vector.x, y, vector.z));
			this.Wire(this.prev, delegate
			{
				this.TurnPage(-1);
			});
			this.Wire(this.next, delegate
			{
				this.TurnPage(1);
			});
			this.Wire(this.back, delegate
			{
				this.Run((this.page != null) ? this.page.Back : null);
			});
			this.footer = TabletView.CopyText(componentInChildren.transform, this.root.transform, "footer", TabletView.textColor, 0.65f);
			this.footer.transform.localPosition = new Vector3(vector3.x, (num4 + vector.y) / 2f, vector3.z);
			float num6 = touchScreenButton.ButtonSize.x * Mathf.Abs(this.screen.transform.lossyScale.x) * 0.95f;
			TabletView.FitText(this.title, num6 * 0.8f, 1);
			TabletView.FitText(this.status, num6, 2);
			TabletView.FitText(this.footer, num6, 2);
			for (int j = 0; j < 4; j++)
			{
				TabletView.FitText(this.rowLabels[j], num6, 1);
				TabletView.FitText(this.rowDetails[j], num6, 1);
			}
			this.TakeOverHome(buttons);
			this.Remap();
		}

		// Token: 0x0600006F RID: 111 RVA: 0x00004CC8 File Offset: 0x00002EC8
		public void Dispose()
		{
			if (this.closed)
			{
				return;
			}
			this.closed = true;
			for (int i = this.swaps.Count - 1; i >= 0; i--)
			{
				TabletView.Swap swap = this.swaps[i];
				if (!(swap.Target == null))
				{
					AltaEvent altaEvent = swap.Field.GetValue(swap.Target) as AltaEvent;
					if (altaEvent != null)
					{
						altaEvent.Invoked -= this.GoHome;
					}
					swap.Field.SetValue(swap.Target, swap.Original);
				}
			}
			this.swaps.Clear();
			Window window = this.Window;
			Panel panel = this.Panel;
			this.Window = null;
			this.Panel = null;
			try
			{
				if (window != null && panel != null && window.CurrentPanel == panel)
				{
					if (this.gamePanel != null)
					{
						window.SwitchToPanel(this.gamePanel);
					}
					else
					{
						AccessTools.Field(typeof(Window), "<CurrentPanel>k__BackingField").SetValue(window, null);
					}
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("couldnt put the tablet back: " + ex.Message);
			}
			if (this.root != null)
			{
				this.root.SetActive(false);
				this.root.transform.SetParent(null, false);
				Object.Destroy(this.root);
				this.root = null;
			}
			this.Remap();
			this.screen = null;
		}

		// Token: 0x06000070 RID: 112 RVA: 0x00004E54 File Offset: 0x00003054
		public static TabletView Create(SocialTablet tablet)
		{
			TabletView tabletView = new TabletView();
			TabletView result;
			try
			{
				tabletView.Build(tablet);
				result = tabletView;
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("tablet page failed: " + ex.Message);
				tabletView.Dispose();
				result = null;
			}
			return result;
		}

		// Token: 0x04000029 RID: 41
		private const int RowCount = 4;

		// Token: 0x0400002A RID: 42
		private const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// Token: 0x0400002B RID: 43
		private static Color headingColor = new Color(0.76f, 0.96f, 1f);

		// Token: 0x0400002C RID: 44
		private static Color textColor = new Color(0.87f, 0.94f, 1f);

		// Token: 0x0400002D RID: 45
		private static Type[] keepTypes = new Type[]
		{
			typeof(Button),
			typeof(TouchScreenButton),
			typeof(ButtonWithEvents),
			typeof(TextButton),
			typeof(TouchScreenButtonVisual),
			typeof(TextRenderer)
		};

		// Token: 0x0400002E RID: 46
		public Window Window;

		// Token: 0x0400002F RID: 47
		public Panel Panel;

		// Token: 0x04000030 RID: 48
		public Action Home;

		// Token: 0x04000031 RID: 49
		public Action AfterClick;

		// Token: 0x04000032 RID: 50
		private TouchScreenMenuBase screen;

		// Token: 0x04000033 RID: 51
		private Panel gamePanel;

		// Token: 0x04000034 RID: 52
		private GameObject root;

		// Token: 0x04000035 RID: 53
		private TextRenderer title;

		// Token: 0x04000036 RID: 54
		private TextRenderer status;

		// Token: 0x04000037 RID: 55
		private TextRenderer footer;

		// Token: 0x04000038 RID: 56
		private Button[] rowButtons = new Button[4];

		// Token: 0x04000039 RID: 57
		private TextRenderer[] rowLabels = new TextRenderer[4];

		// Token: 0x0400003A RID: 58
		private TextRenderer[] rowDetails = new TextRenderer[4];

		// Token: 0x0400003B RID: 59
		private Button back;

		// Token: 0x0400003C RID: 60
		private Button prev;

		// Token: 0x0400003D RID: 61
		private Button next;

		// Token: 0x0400003E RID: 62
		private List<TabletView.Swap> swaps = new List<TabletView.Swap>();

		// Token: 0x0400003F RID: 63
		private bool closed;

		// Token: 0x04000040 RID: 64
		private Page page;

		// Token: 0x04000041 RID: 65
		private int pageIndex;

		// Token: 0x0200002B RID: 43
		private class Swap
		{
			// Token: 0x0400009D RID: 157
			public Component Target;

			// Token: 0x0400009E RID: 158
			public FieldInfo Field;

			// Token: 0x0400009F RID: 159
			public object Original;
		}
	}
}
