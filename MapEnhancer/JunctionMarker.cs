using Game.Messages;
using Game.State;
using MapEnhancer.UMM;
using Track;
using UI.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapEnhancer
{
	public class JunctionMarker : MonoBehaviour
	{
		public static Material matJunctionGreen;
		public static Material matJunctionRed;

		private static GameObject prefabHolder;
		public static JunctionMarker junctionMarkerPrefabL;
		public static JunctionMarker junctionMarkerPrefabR;

		//public MeshRenderer left;
		//public MeshRenderer right;
		public CanvasRenderer left;
		public CanvasRenderer right;
		public int id { get; private set; }
		public TrackNode junction;
		
		public void Start()
		{
			SetColors();
			foreach (var icon in GetComponentsInChildren<MapIcon>())
			{
				icon.OnClick += OnMapMarkerPressed;
			}
			junction.OnDidChangeThrown += SetColors;
		}

		void OnMapMarkerPressed()
		{
			var setSwitch = new RequestSetSwitch(junction.id, !junction.isThrown);
			StateManager.ApplyLocal(setSwitch);
			SetColors();
		}

		void SetColors()
		{
			if (!junction.isThrown)
			{
				left.SetColor(Color.green);
				left.SetAlpha(0.8f);
				right.SetColor(Color.red);
				right.SetAlpha(0.5f);
			}
			else
			{
				left.SetColor(Color.red);
				left.SetAlpha(0.5f);
				right.SetColor(Color.green);
				right.SetAlpha(0.8f);
			}
		}

		void OnDestroy()
		{
			foreach (var icon in GetComponentsInChildren<MapIcon>())
			{
				icon.OnClick -= OnMapMarkerPressed;
			}
			junction.OnDidChangeThrown -= SetColors;
		}

		public static void CreatePrefab()
		{
			//MapMarkersController controller = Component.FindObjectOfType<MapMarkersController>();
			//var MapIcon = UnityEngine.Object.Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, base.transform);
			// Holder stops "prefab" from going active immediately
			if (prefabHolder != null) return;

			prefabHolder = new GameObject("Prefab Holder");
			prefabHolder.hideFlags = HideFlags.HideAndDontSave;
			prefabHolder.SetActive(false);


			MapIcon mapIcon = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
			mapIcon.SetText("");
			GameObject junctionMarker = mapIcon.gameObject;
			junctionMarker.hideFlags = HideFlags.HideAndDontSave;

			//UnityEngine.Object.DestroyImmediate(junctionMarker.transform.GetChild(0).gameObject);

			var arrow = CreateTrianglePrimitive();
			var doubleArrow = CreateDoubleTrianglePrimitive();

			junctionMarker.name = "Indicators (L)";
			//junctionMarker.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);
			var markerController = junctionMarker.AddComponent<JunctionMarker>();
			var left = junctionMarker.transform.GetChild(0);
			left.gameObject.name = "Indicator";
			//left.GetComponentInChildren<MeshFilter>().mesh = doubleArrow;

			var right = Instantiate(junctionMarker.transform.GetChild(0), junctionMarker.transform);
			right.gameObject.name = "Indicator";
			//right.GetComponentInChildren<MeshFilter>().mesh = arrow;
			markerController.left = left.GetComponentInChildren<CanvasRenderer>(true);
			markerController.right = right.GetComponentInChildren<CanvasRenderer>(true);

			//left.transform.localPosition = new Vector3(40f, 0f, 0f);

			left.transform.localScale = new Vector3(0.5f, 0.25f, 1f);
			right.transform.localScale = new Vector3(0.5f, 0.25f, 1f);
			//right.transform.localPosition = new Vector3(-40f, 0f, 0f);
			left.GetComponent<RectTransform>().anchoredPosition = new Vector2(0.25f * 43.75f + 10f, 0f);
			right.GetComponent<RectTransform>().anchoredPosition = new Vector2(-(0.25f * 43.75f + 10f), 0f);
			left.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
			right.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
			junctionMarkerPrefabR = Instantiate(markerController, prefabHolder.transform);
			junctionMarkerPrefabR.gameObject.hideFlags = HideFlags.HideAndDontSave;
			left.GetComponent<RectTransform>().anchoredPosition = new Vector2(-(0.25f * 43.75f + 10f), 0f);
			right.GetComponent<RectTransform>().anchoredPosition = new Vector2(0.25f * 43.75f + 10f, 0f);
			left.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
			right.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
			junctionMarkerPrefabR.name = "Indicators (R)";
			//left.GetComponentInChildren<MeshFilter>().mesh = arrow;
			//right.GetComponentInChildren<MeshFilter>().mesh = doubleArrow;
			junctionMarkerPrefabL = markerController;

			/*
            var matMarker = new Material(junctionMarker.GetComponentInChildren<MeshRenderer>().sharedMaterial);
            matMarker.color = Color.red;
            matMarker.color = new Color(1f, 0f, 0f, 0.75f);
            matMarker.SetColor("_EmissionColor", new Color(0.2f, 0, 0, 0.75f));
            //matMarker.DisableKeyword("_EMISSION");
            matMarker.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            //Transparent
            SetShaderMode(matMarker, BlendMode.Fade);
            matJunctionRed = matMarker;

            matMarker = new Material(matMarker);
            matMarker.color = Color.green;
            matMarker.color = new Color(0f, 1f, 0f, 0.75f);
            matMarker.SetColor("_EmissionColor", new Color(0, 0.2f, 0, 0.75f));
            matJunctionGreen = matMarker;
			*/
		}

		public enum BlendMode
		{
			Opaque,
			Cutout,
			Fade,
			Transparent
		}

		private static void SetShaderMode(Material material, BlendMode blendMode)
		{
			switch (blendMode)
			{
				case BlendMode.Opaque:
					material.SetOverrideTag("RenderType", "Opaque");
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
					material.SetInt("_ZWrite", 1);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int)RenderQueue.Geometry;
					break;
				case BlendMode.Cutout:
					material.SetOverrideTag("RenderType", "TransparentCutout");
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
					material.SetInt("_ZWrite", 1);
					material.EnableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
					break;
				case BlendMode.Fade:
					material.SetOverrideTag("RenderType", "Transparent");
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					material.SetInt("_ZWrite", 0);
					material.DisableKeyword("_ALPHATEST_ON");
					material.EnableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
					break;
				case BlendMode.Transparent:
					material.SetOverrideTag("RenderType", "Transparent");
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					material.SetInt("_ZWrite", 0);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
					break;
			}
		}

		public static Mesh CreateTrianglePrimitive()
		{
			var mesh = new Mesh();

			mesh.vertices = new Vector3[] {
				new Vector3(-1f, 0f, -2f),
				new Vector3(0f,  0f,  2f),
				new Vector3(1f,  0f, -2f)
			};
			mesh.uv = new Vector2[] {
				new Vector2(0f,   0f),
				new Vector2(0.5f, 1f),
				new Vector2(1f,   0f)
			};
			mesh.triangles = new int[] { 0, 1, 2 };

			mesh.RecalculateBounds();
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();

			return mesh;
		}

		public static Mesh CreateDoubleTrianglePrimitive()
		{
			var mesh = new Mesh();

			mesh.vertices = new Vector3[] {
				new Vector3(-1.000000f, 0.000000f, -2.000000f),
				new Vector3( 1.000000f, 0.000000f, -2.000000f),
				new Vector3( 0.500000f, 0.000000f,  0.000000f),
				new Vector3(-0.500000f, 0.000000f,  0.000000f),
				new Vector3( 0.000000f, 0.000000f,  2.400000f),
				new Vector3( 1.000000f, 0.000000f,  0.000000f),
				new Vector3(-1.000000f, 0.000000f,  0.000000f)
			};
			mesh.uv = new Vector2[] {
				new Vector2(0.500000f, 1.000000f),
				new Vector2(0.377273f, 0.454545f),
				new Vector2(0.622727f, 0.454545f),
				new Vector2(0.727273f, 0.454545f),
				new Vector2(0.272727f, 0.454545f),
				new Vector2(0.727273f, 0.000000f),
				new Vector2(0.272727f, 0.000000f),
			};
			mesh.triangles = new int[] {
				4,2,3,
				3,6,4,
				4,5,2,
				2,0,3,
				2,1,0
			};

			mesh.RecalculateBounds();
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();

			return mesh;
		}
	}

	public static class Tex2DExtension
	{
		public static Texture2D Circle(this Texture2D tex, int x, int y, int r, Color color)
		{
			float rSquared = r * r;

			for (int u = 0; u < tex.width; u++)
			{
				for (int v = 0; v < tex.height; v++)
				{
					if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared) tex.SetPixel(u, v, color);
				}
			}

			return tex;
		}
	}
}
