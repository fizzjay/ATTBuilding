using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000004 RID: 4
	internal static class Icons
	{
		// Token: 0x06000003 RID: 3 RVA: 0x00002068 File Offset: 0x00000268
		internal static Texture2D Load(Assembly asm, string name)
		{
			string[] manifestResourceNames = asm.GetManifestResourceNames();
			string text = manifestResourceNames.FirstOrDefault((string n) => n == name) ?? manifestResourceNames.FirstOrDefault((string n) => n.EndsWith("." + name));
			if (text == null)
			{
				MelonLogger.Warning(string.Concat(new string[]
				{
					"no ",
					name,
					" embedded in ",
					asm.GetName().Name,
					", using the text label"
				}));
				return null;
			}
			byte[] array;
			using (Stream manifestResourceStream = asm.GetManifestResourceStream(text))
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					manifestResourceStream.CopyTo(memoryStream);
					array = memoryStream.ToArray();
				}
			}
			Texture2D texture2D = new Texture2D(2, 2, 4, true);
			if (!ImageConversion.LoadImage(texture2D, array))
			{
				MelonLogger.Warning(text + " in " + asm.GetName().Name + " isnt a png or jpg, using the text label");
				Object.Destroy(texture2D);
				return null;
			}
			texture2D.name = text;
			texture2D.wrapMode = 1;
			texture2D.filterMode = 2;
			texture2D.anisoLevel = 4;
			Object.DontDestroyOnLoad(texture2D);
			return texture2D;
		}

		// Token: 0x06000004 RID: 4 RVA: 0x000021B4 File Offset: 0x000003B4
		internal static bool Add(Transform parent, Texture2D tex)
		{
			if (Icons.shader == null)
			{
				Icons.shader = (Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent"));
			}
			if (Icons.shader == null)
			{
				return false;
			}
			if (Icons.quad == null)
			{
				GameObject gameObject = GameObject.CreatePrimitive(5);
				Icons.quad = gameObject.GetComponent<MeshFilter>().sharedMesh;
				Object.DestroyImmediate(gameObject);
			}
			Material material;
			if (!Icons.materials.TryGetValue(tex, out material))
			{
				material = new Material(Icons.shader)
				{
					mainTexture = tex,
					name = tex.name
				};
				Object.DontDestroyOnLoad(material);
				Icons.materials[tex] = material;
			}
			GameObject gameObject2 = new GameObject("TabletUILib icon");
			gameObject2.transform.SetParent(parent, false);
			gameObject2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
			gameObject2.transform.localScale = Vector3.one * 0.12f;
			gameObject2.AddComponent<MeshFilter>().sharedMesh = Icons.quad;
			gameObject2.AddComponent<MeshRenderer>().sharedMaterial = material;
			return true;
		}

		// Token: 0x04000002 RID: 2
		internal const float Size = 0.12f;

		// Token: 0x04000003 RID: 3
		private static Mesh quad;

		// Token: 0x04000004 RID: 4
		private static Shader shader;

		// Token: 0x04000005 RID: 5
		private static Dictionary<Texture2D, Material> materials = new Dictionary<Texture2D, Material>();
	}
}
