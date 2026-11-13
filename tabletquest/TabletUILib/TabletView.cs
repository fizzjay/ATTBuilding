using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using Il2CppAlta.Meta.UI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSystem;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;

namespace TabletUILib
{
	// Token: 0x02000007 RID: 7
	internal class TabletView
	{
		// Token: 0x06000039 RID: 57 RVA: 0x00003250 File Offset: 0x00001450
		private static Dictionary<string, Func<Component, Il2CppObjectBase>> KeepTypes()
		{
			if (TabletView.keepTypes != null)
			{
				return TabletView.keepTypes;
			}
			Dictionary<string, Func<Component, Il2CppObjectBase>> dictionary = new Dictionary<string, Func<Component, Il2CppObjectBase>>();
			string fullName = Il2CppType.Of<Button>().FullName;
			dictionary[fullName] = ((Component c) => c.Cast<Button>());
			string fullName2 = Il2CppType.Of<TouchScreenButton>().FullName;
			dictionary[fullName2] = ((Component c) => c.Cast<TouchScreenButton>());
			string fullName3 = Il2CppType.Of<ButtonWithEvents>().FullName;
			dictionary[fullName3] = ((Component c) => c.Cast<ButtonWithEvents>());
			string fullName4 = Il2CppType.Of<TextButton>().FullName;
			dictionary[fullName4] = ((Component c) => c.Cast<TextButton>());
			string fullName5 = Il2CppType.Of<TouchScreenButtonVisual>().FullName;
			dictionary[fullName5] = ((Component c) => c.Cast<TouchScreenButtonVisual>());
			string fullName6 = Il2CppType.Of<TextRenderer>().FullName;
			dictionary[fullName6] = ((Component c) => c.Cast<TextRenderer>());
			TabletView.keepTypes = dictionary;
			return TabletView.keepTypes;
		}

		// Token: 0x0600003A RID: 58 RVA: 0x000033A3 File Offset: 0x000015A3
		private static Transform Find(Transform parent, string path)
		{
			Transform transform = parent.Find(path);
			if (transform == null)
			{
				throw new Exception("tablet is missing " + path);
			}
			return transform;
		}

		// Token: 0x0600003B RID: 59 RVA: 0x000033C6 File Offset: 0x000015C6
		private static ITouchScreenButton Touch(Component c)
		{
			Button component = c.GetComponent<Button>();
			if (component == null)
			{
				return null;
			}
			return component.TryCast<ITouchScreenButton>();
		}

		// Token: 0x0600003C RID: 60 RVA: 0x000033D9 File Offset: 0x000015D9
		private static bool IsTouch(Transform t)
		{
			return TabletView.Touch(t) != null;
		}

		// Token: 0x0600003D RID: 61 RVA: 0x000033E4 File Offset: 0x000015E4
		private static void FitText(TextRenderer text, float worldWidth, int lines)
		{
			text.supportsHTMLTags = false;
			text.isMultiLine = (lines > 1);
			float num = Mathf.Abs(text.transform.lossyScale.x);
			if (worldWidth <= 0f || num <= 0f)
			{
				return;
			}
			float num2 = worldWidth / num;
			text.maxWidth = num2;
			text.bestFitToWidth = (lines == 1);
			text.hasLineLimit = true;
			text.maxLines = lines;
			float scale = text.scale;
			if (scale > 0f)
			{
				text.lineWidth = num2 / scale;
				text.maxScale = scale;
				text.minScale = scale * 0.65f;
			}
		}

		// Token: 0x0600003E RID: 62 RVA: 0x00003478 File Offset: 0x00001678
		private static bool IsField(Type t, PropertyInfo p)
		{
			return p.CanWrite && t.GetField("NativeFieldInfoPtr_" + p.Name, BindingFlags.Static | BindingFlags.NonPublic) != null;
		}

		// Token: 0x0600003F RID: 63 RVA: 0x000034A4 File Offset: 0x000016A4
		private static void ClearEvents(Il2CppObjectBase c)
		{
			Type type = c.GetType();
			while (type != null && type != typeof(MonoBehaviour))
			{
				foreach (PropertyInfo propertyInfo in type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public))
				{
					if (TabletView.IsField(type, propertyInfo))
					{
						Type propertyType = propertyInfo.PropertyType;
						try
						{
							if (typeof(Delegate).IsAssignableFrom(propertyType))
							{
								propertyInfo.SetValue(c, null);
							}
							else if ((typeof(UnityEventBase).IsAssignableFrom(propertyType) || propertyType == typeof(AltaEvent)) && !propertyType.IsAbstract)
							{
								propertyInfo.SetValue(c, Activator.CreateInstance(propertyType));
							}
							else if (propertyInfo.Name == "_State_k__BackingField")
							{
								propertyInfo.SetValue(c, false);
							}
						}
						catch (Exception ex)
						{
							MelonLogger.Warning(string.Concat(new string[]
							{
								"couldnt clear ",
								type.Name,
								".",
								propertyInfo.Name,
								": ",
								ex.Message
							}));
						}
					}
				}
				type = type.BaseType;
			}
		}

		// Token: 0x06000040 RID: 64 RVA: 0x000035EC File Offset: 0x000017EC
		private static GameObject CopyObject(Transform source, Transform parent, string name)
		{
			GameObject gameObject = Object.Instantiate<GameObject>(source.gameObject, parent, false);
			gameObject.name = "TabletUILib " + name;
			gameObject.SetActive(false);
			gameObject.transform.localRotation = Quaternion.Inverse(parent.rotation) * source.rotation;
			Vector3 lossyScale = source.lossyScale;
			Vector3 lossyScale2 = parent.lossyScale;
			gameObject.transform.localScale = new Vector3(lossyScale.x / lossyScale2.x, lossyScale.y / lossyScale2.y, lossyScale.z / lossyScale2.z);
			Dictionary<string, Func<Component, Il2CppObjectBase>> dictionary = TabletView.KeepTypes();
			foreach (MonoBehaviour monoBehaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (!(monoBehaviour == null))
				{
					Func<Component, Il2CppObjectBase> func;
					if (dictionary.TryGetValue(monoBehaviour.GetIl2CppType().FullName, out func))
					{
						TabletView.ClearEvents(func(monoBehaviour));
					}
					else
					{
						Object.DestroyImmediate(monoBehaviour);
					}
				}
			}
			gameObject.SetActive(true);
			return gameObject;
		}

		// Token: 0x06000041 RID: 65 RVA: 0x0000370C File Offset: 0x0000190C
		private static TextRenderer CopyText(Transform source, Transform parent, string name, Color color, float scale)
		{
			GameObject gameObject = TabletView.CopyObject(source, parent, name);
			TextRenderer component = gameObject.GetComponent<TextRenderer>();
			gameObject.transform.localScale *= scale;
			TabletView.FitText(component, 0f, 1);
			component.SetColor(color);
			return component;
		}

		// Token: 0x06000042 RID: 66 RVA: 0x00003753 File Offset: 0x00001953
		private static string Escape(string text)
		{
			return (text ?? "").Replace("\\", "\\\\");
		}

		// Token: 0x06000043 RID: 67 RVA: 0x0000376E File Offset: 0x0000196E
		private static void SetText(TextRenderer text, string value)
		{
			text.Text = TabletView.Escape(value);
		}

		// Token: 0x06000044 RID: 68 RVA: 0x0000377C File Offset: 0x0000197C
		private static void SetVisible(Button button, bool on)
		{
			button.IsEnabled = on;
			button.gameObject.SetActive(on);
		}

		// Token: 0x06000045 RID: 69 RVA: 0x00003794 File Offset: 0x00001994
		private Button CopyButton(Transform source, string name, Vector3 pos)
		{
			GameObject gameObject = TabletView.CopyObject(source, this.root.transform, name);
			gameObject.transform.localPosition = pos;
			Button component = gameObject.GetComponent<Button>();
			if (component == null || TabletView.Touch(component) == null)
			{
				throw new Exception(source.name + " isnt a touch button");
			}
			component.IsEnabled = true;
			return component;
		}

		// Token: 0x06000046 RID: 70 RVA: 0x000037F4 File Offset: 0x000019F4
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

		// Token: 0x06000047 RID: 71 RVA: 0x00003848 File Offset: 0x00001A48
		private void Wire(Button button, Action click)
		{
			Action<Button> action = delegate(Button b)
			{
				if (!this.closed && button.IsEnabled)
				{
					click();
				}
			};
			button.Released += action;
		}

		// Token: 0x06000048 RID: 72 RVA: 0x0000388C File Offset: 0x00001A8C
		private void Remap()
		{
			if (this.screen == null)
			{
				return;
			}
			this.screen.InstantiatedButtons.Clear();
			this.screen.MapButtons();
		}

		// Token: 0x06000049 RID: 73 RVA: 0x000038B8 File Offset: 0x00001AB8
		private int PageCount()
		{
			if (this.page == null)
			{
				return 1;
			}
			return Mathf.Max(1, (this.page.Rows.Count + 4 - 1) / 4);
		}

		// Token: 0x0600004A RID: 74 RVA: 0x000038E0 File Offset: 0x00001AE0
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

		// Token: 0x0600004B RID: 75 RVA: 0x00003AE0 File Offset: 0x00001CE0
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

		// Token: 0x0600004C RID: 76 RVA: 0x00003B3C File Offset: 0x00001D3C
		private void TurnPage(int dir)
		{
			int num = this.PageCount();
			this.pageIndex = (this.pageIndex + dir + num) % num;
			this.Draw();
		}

		// Token: 0x0600004D RID: 77 RVA: 0x00003B68 File Offset: 0x00001D68
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

		// Token: 0x0600004E RID: 78 RVA: 0x00003BB8 File Offset: 0x00001DB8
		private void Replace(Component target, object wrapper, string field, object value)
		{
			PropertyInfo property = wrapper.GetType().GetProperty(field, BindingFlags.Instance | BindingFlags.Public);
			if (property == null)
			{
				return;
			}
			this.swaps.Add(new TabletView.Swap
			{
				Target = target,
				Wrapper = wrapper,
				Field = property,
				Original = property.GetValue(wrapper)
			});
			property.SetValue(wrapper, value);
		}

		// Token: 0x0600004F RID: 79 RVA: 0x00003C19 File Offset: 0x00001E19
		private void GoHome()
		{
			this.Run(this.Home);
		}

		// Token: 0x06000050 RID: 80 RVA: 0x00003C28 File Offset: 0x00001E28
		private void TakeOverHome(Transform buttons)
		{
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
					foreach (BasicTouchScreenButton basicTouchScreenButton in transform.GetComponentsInChildren<BasicTouchScreenButton>(true))
					{
						AltaEvent altaEvent = new AltaEvent();
						altaEvent.Invoked += new Action(this.GoHome);
						this.Replace(basicTouchScreenButton, basicTouchScreenButton, "onPressed", new AltaEvent());
						this.Replace(basicTouchScreenButton, basicTouchScreenButton, "onReleased", altaEvent);
						num++;
					}
					foreach (Button button in transform.GetComponentsInChildren<Button>(true))
					{
						this.Replace(button, button, "Pressed", null);
						this.Replace(button, button, "Released", delegate(Button x)
						{
							this.GoHome();
						});
						ButtonWithEvents buttonWithEvents = button.TryCast<ButtonWithEvents>();
						if (buttonWithEvents != null)
						{
							this.Replace(button, buttonWithEvents, "onPress", new UnityEvent());
							this.Replace(button, buttonWithEvents, "onRelease", new UnityEvent());
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

		// Token: 0x06000051 RID: 81 RVA: 0x00003DC4 File Offset: 0x00001FC4
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
			ITouchScreenButton touchScreenButton = TabletView.Touch(transform3);
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
			this.Panel.window = this.Window;
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

		// Token: 0x06000052 RID: 82 RVA: 0x0000440C File Offset: 0x0000260C
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
					swap.Field.SetValue(swap.Wrapper, swap.Original);
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
						window._CurrentPanel_k__BackingField = null;
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

		// Token: 0x06000053 RID: 83 RVA: 0x00004554 File Offset: 0x00002754
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

		// Token: 0x04000017 RID: 23
		private const int RowCount = 4;

		// Token: 0x04000018 RID: 24
		private static Color headingColor = new Color(0.76f, 0.96f, 1f);

		// Token: 0x04000019 RID: 25
		private static Color textColor = new Color(0.87f, 0.94f, 1f);

		// Token: 0x0400001A RID: 26
		private static Dictionary<string, Func<Component, Il2CppObjectBase>> keepTypes;

		// Token: 0x0400001B RID: 27
		public Window Window;

		// Token: 0x0400001C RID: 28
		public Panel Panel;

		// Token: 0x0400001D RID: 29
		public Action Home;

		// Token: 0x0400001E RID: 30
		public Action AfterClick;

		// Token: 0x0400001F RID: 31
		private TouchScreenMenuBase screen;

		// Token: 0x04000020 RID: 32
		private Panel gamePanel;

		// Token: 0x04000021 RID: 33
		private GameObject root;

		// Token: 0x04000022 RID: 34
		private TextRenderer title;

		// Token: 0x04000023 RID: 35
		private TextRenderer status;

		// Token: 0x04000024 RID: 36
		private TextRenderer footer;

		// Token: 0x04000025 RID: 37
		private Button[] rowButtons = new Button[4];

		// Token: 0x04000026 RID: 38
		private TextRenderer[] rowLabels = new TextRenderer[4];

		// Token: 0x04000027 RID: 39
		private TextRenderer[] rowDetails = new TextRenderer[4];

		// Token: 0x04000028 RID: 40
		private Button back;

		// Token: 0x04000029 RID: 41
		private Button prev;

		// Token: 0x0400002A RID: 42
		private Button next;

		// Token: 0x0400002B RID: 43
		private List<TabletView.Swap> swaps = new List<TabletView.Swap>();

		// Token: 0x0400002C RID: 44
		private bool closed;

		// Token: 0x0400002D RID: 45
		private Page page;

		// Token: 0x0400002E RID: 46
		private int pageIndex;

		// Token: 0x0200001D RID: 29
		private class Swap
		{
			// Token: 0x0400006C RID: 108
			public Component Target;

			// Token: 0x0400006D RID: 109
			public object Wrapper;

			// Token: 0x0400006E RID: 110
			public PropertyInfo Field;

			// Token: 0x0400006F RID: 111
			public object Original;
		}
	}
}
