using dnlib;
using MapEnhancer.UMM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Common;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MapEnhancer
{
	[RequireComponent(typeof(Image))]
	internal class MapResizer : PanelResizer, IPointerDownHandler, IPointerUpHandler, IEventSystemHandler, IDragHandler
	{
		private float aspect;
		private Window window;
		private MapWindow mapWindow;
		private AspectRatioFitter aspectRatioFitter;
		private Vector2 sizeDelta;
		private Camera mapCamera;
		private bool doOnce;

		public static MapResizer Create()
		{
			var mapWindow = MapWindow.instance;

			var resizerGo = new GameObject("Resizer Handle");
			resizerGo.SetActive(false);
			var resizer = resizerGo.AddComponent<MapResizer>();
			resizerGo.transform.SetParent(MapWindow.instance._window.transform, false);
			resizerGo.SetActive(mapWindow._window.IsShown);

			return resizer;
		}

		new private void Awake()
		{
			base.Awake();

			window = GetComponentInParent<Window>();
			mapWindow = GetComponentInParent<MapWindow>();
			transform.gameObject.SetActive(window.IsShown);
			mapCamera = MapBuilder.Shared.mapCamera;

			var rect = window._rectTransform.rect;
			minSize = new Vector2(rect.width, rect.height);
			aspect = rect.width / rect.height;
			sizeDelta = window.InitialContentSize - minSize;

			AdjustRenderTexture();
			AddAspectRatioFilter();
			CreateDragHandle();
		}

		void OnEnable()
		{
			var windowRectTransform = window._rectTransform;
			if (windowRectTransform.rect.width > Screen.width || windowRectTransform.rect.height > Screen.height)
				windowRectTransform.sizeDelta = new Vector2(Screen.width, Screen.height);
		}
		
		new public void OnPointerDown(PointerEventData data)
		{
			UpdateWindowSize();
			base.OnPointerDown(data);
		}

		public void OnPointerUp(PointerEventData data)
		{
			AdjustRenderTexture();
		}


		void UpdateWindowSize()
		{
			maxSize = new Vector2(Screen.width, Screen.height);
			if ((float)Screen.width / (float)Screen.height < aspect)
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
			else
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
		}

		private void AddAspectRatioFilter()
		{
			aspectRatioFitter = transform.parent.gameObject.AddComponent<AspectRatioFitter>();
			aspectRatioFitter.aspectRatio = aspect;
			aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
		}

		private void AdjustRenderTexture()
		{
			RenderTexture rt = mapWindow._renderTexture;
			var height = (int)Mathf.Round(Screen.height + sizeDelta.y);
			var width = (int)Mathf.Round(Screen.height * aspect + sizeDelta.x);
			if (rt.width != width || rt.height != height)
			{
				rt.Release();
				rt.width = width;
				rt.height = height;
				mapCamera.aspect = (float)rt.width / (float)rt.height;
				rt.antiAliasing = (int)(MapEnhancer.Instance?.Settings.MSAA ?? MsaaQuality._4x);
			}
		}

		private void CreateDragHandle()
		{
			var image = transform.GetComponent<Image>();
			image.color = new Color(77f/255f, 69f/255f, 55f/255f, 1f);
			image.sprite = transform.parent.Find("Chrome/Resize Widget")?.GetComponentInChildren<Image>()?.sprite;

			var rect = GetComponent<RectTransform>();
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 4f, 22f);
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 4f, 22f);
		}

		void OnDestroy()
		{
			if (aspectRatioFitter) DestroyImmediate(aspectRatioFitter);
			window._rectTransform.sizeDelta = minSize;
		}
	}
}
