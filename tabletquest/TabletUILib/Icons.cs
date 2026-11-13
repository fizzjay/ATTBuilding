using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace TabletUILib
{
	// Token: 0x02000002 RID: 2
	internal static class Icons
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
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
			if (!Il2CppImageConversionManager.LoadImage(texture2D, array))
			{
				MelonLogger.Warning(text + " in " + asm.GetName().Name + " isnt a png or jpg, using the text label");
				Object.Destroy(texture2D);
				return null;
			}
			texture2D.name = text;
			texture2D.wrapMode = 1;
			texture2D.filterMode = 2;
			texture2D.anisoLevel = 4;
			texture2D.hideFlags = 32;
			Object.DontDestroyOnLoad(texture2D);
			return texture2D;
		}

		// Token: 0x06000002 RID: 2 RVA: 0x000021AC File Offset: 0x000003AC
		private static Mesh Quad()
		{
			if (Icons.quad != null)
			{
				return Icons.quad;
			}
			Icons.quad = new Mesh
			{
				name = "TabletUILib quad"
			};
			Icons.quad.vertices = new Vector3[]
			{
				new Vector3(-0.5f, -0.5f, 0f),
				new Vector3(0.5f, -0.5f, 0f),
				new Vector3(-0.5f, 0.5f, 0f),
				new Vector3(0.5f, 0.5f, 0f)
			};
			Icons.quad.uv = new Vector2[]
			{
				new Vector2(0f, 0f),
				new Vector2(1f, 0f),
				new Vector2(0f, 1f),
				new Vector2(1f, 1f)
			};
			Icons.quad.triangles = new int[]
			{
				0,
				3,
				1,
				3,
				0,
				2
			};
			Icons.quad.RecalculateNormals();
			Icons.quad.hideFlags = 32;
			Object.DontDestroyOnLoad(Icons.quad);
			return Icons.quad;
		}

		// Token: 0x06000003 RID: 3 RVA: 0x00002314 File Offset: 0x00000514
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
			Material material;
			if (!Icons.materials.TryGetValue(tex, out material))
			{
				material = new Material(Icons.shader)
				{
					mainTexture = tex,
					name = tex.name
				};
				material.hideFlags = 32;
				Object.DontDestroyOnLoad(material);
				Icons.materials[tex] = material;
			}
			GameObject gameObject = new GameObject("TabletUILib icon");
			gameObject.transform.SetParent(parent, false);
			gameObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
			gameObject.transform.localScale = Vector3.one * 0.12f;
			gameObject.AddComponent<MeshFilter>().sharedMesh = Icons.Quad();
			gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
			return true;
		}

		// Token: 0x04000001 RID: 1
		internal const float Size = 0.12f;

		// Token: 0x04000002 RID: 2
		private static Mesh quad;

		// Token: 0x04000003 RID: 3
		private static Shader shader;

		// Token: 0x04000004 RID: 4
		private static Dictionary<Texture2D, Material> materials = new Dictionary<Texture2D, Material>();
	}
}
