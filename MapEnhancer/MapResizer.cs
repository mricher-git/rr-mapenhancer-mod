using MapEnhancer.UMM;
using UI.Common;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MapEnhancer
{
	[RequireComponent(typeof(Image))]
	public class MapResizer : PanelResizer, IPointerDownHandler, IPointerUpHandler, IEventSystemHandler, IDragHandler
	{
		private Vector2 originalSize;
		private float aspect;
		private Window window;
		private MapWindow mapWindow;
		private AspectRatioFitter aspectRatioFitter;
		private Vector2 windowMargins;
		private Camera mapCamera;
		private Canvas canvas;

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
			canvas = GetComponentInParent<Canvas>().rootCanvas;

			var rect = window._rectTransform.rect;
			originalSize = rect.size;
			minSize = new Vector2(rect.width * .75f, rect.height * .75f);
			aspect = rect.width / rect.height;
			windowMargins = originalSize - window.InitialContentSize;

			var scale = MapEnhancer.Instance.Settings.WindowSizeScaleMin;
			_rectTransform.sizeDelta *= scale;

			AdjustRenderTexture();
			AddAspectRatioFitter();
			CreateDragHandle();
		}

		void OnEnable()
		{
			var windowRectTransform = window._rectTransform;
			var parentRectTransform = _rectTransform.parent.GetComponent<RectTransform>();
			if (windowRectTransform.sizeDelta.x > parentRectTransform.sizeDelta.x || windowRectTransform.sizeDelta.y > parentRectTransform.sizeDelta.y)
				windowRectTransform.sizeDelta = parentRectTransform.sizeDelta;
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
			Rect parentRect = _rectTransform.parent.GetComponent<RectTransform>().rect;
			
			maxSize = new Vector2(parentRect.max.x - _rectTransform.localPosition.x,
								  _rectTransform.localPosition.y - parentRect.min.y);

			if (parentRect.width / parentRect.height < aspect)
			{
				maxSize.x = Mathf.Min(maxSize.x, maxSize.y * aspect);
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
			}
			else
			{
				maxSize.y = Mathf.Min(maxSize.y, maxSize.x / aspect);
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
			}
		}

		private void AddAspectRatioFitter()
		{
			aspectRatioFitter = transform.parent.gameObject.AddComponent<AspectRatioFitter>();
			aspectRatioFitter.aspectRatio = aspect;
			aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
		}

		private void AdjustRenderTexture()
		{
			RenderTexture rt = mapWindow._renderTexture;
			var height = (int)Mathf.Round(canvas.renderingDisplaySize.y - windowMargins.y);
			var width = (int)Mathf.Round(canvas.renderingDisplaySize.y * aspect - windowMargins.x);
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

		public void SetMinimumSize(float scale)
		{
			minSize = originalSize * scale;

			var windowRectTransform = window._rectTransform;
			if (windowRectTransform.sizeDelta.x < minSize.x || windowRectTransform.sizeDelta.y < minSize.y)
				windowRectTransform.sizeDelta = minSize;
		}

		void OnDestroy()
		{
			if (aspectRatioFitter) DestroyImmediate(aspectRatioFitter);
			window._rectTransform.sizeDelta = originalSize;
		}
	}
}
