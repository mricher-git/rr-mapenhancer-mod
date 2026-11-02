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
		public static MapResizer? Instance { get; private set; }
		private Vector2 originalSize;
		private float aspect;
		private Window window;
		private MapWindow mapWindow;
		private AspectRatioFitter aspectRatioFitter;
		private ResizeNotifier notifier;
		private Vector2 windowMargins;
		private Camera mapCamera;
		private Canvas canvas;
		Vector2 largeWindowPos;
		Vector2 largeWindowSize;
		private bool isLarge;
		private bool isResizing;
		private int lastLoggedAntiAliasing = -1;
		private bool lastLoggedCameraAllowMsaa;
		private Vector2 defaultSize; // The default/normal size (from WindowSizeMin setting)

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
			Instance = this;

			window = GetComponentInParent<Window>();
			mapWindow = GetComponentInParent<MapWindow>();
			transform.gameObject.SetActive(window.IsShown);
			mapCamera = MapBuilder.Shared.mapCamera;
			canvas = GetComponentInParent<Canvas>().rootCanvas;

			// Use the window's InitialContentSize which is the vanilla size scaled by UI settings
			// This respects screen resolution and UI scale from game settings
			originalSize = window.InitialContentSize + new Vector2(48f, 48f); // Add typical window frame margins
			var scale = MapEnhancer.Instance.Settings.WindowSizeMin / 800f;
			defaultSize = new Vector2(originalSize.x * scale, originalSize.y * scale);
			minSize = defaultSize; // Keep minSize for compatibility
			aspect = originalSize.x / originalSize.y;
			windowMargins = originalSize - window.InitialContentSize;

			// Reset to default size on startup
			_windowRectTransform.sizeDelta = defaultSize;

			// Allow users to resize down to 50% of the default size for more flexibility
			// This gives extra room for users who want a very small map window
			var absoluteMinSize = defaultSize * 0.5f;
			base.minSize = absoluteMinSize;

			var canvasRect = _windowRectTransform.parent.GetComponent<RectTransform>().rect;
			largeWindowSize = canvasRect.size * 0.75f; notifier = canvas.gameObject.AddComponent<ResizeNotifier>();
			notifier.RectTransformDimensionsChanged += OnRectCanvasTransformChanged;

			AdjustRenderTexture();
			AddAspectRatioFitter();
			CreateDragHandle();
		}

		void OnEnable()
		{
			var windowRectTransform = window._rectTransform;
			var parentRectTransform = _windowRectTransform.parent.GetComponent<RectTransform>();
			if (windowRectTransform.sizeDelta.x > parentRectTransform.sizeDelta.x || windowRectTransform.sizeDelta.y > parentRectTransform.sizeDelta.y)
				windowRectTransform.sizeDelta = parentRectTransform.sizeDelta;

			AdjustRenderTexture();
		}

		new public void OnPointerDown(PointerEventData data)
		{
			isResizing = true;
			UpdateWindowSize();
			base.OnPointerDown(data);
		}

		new public void OnDrag(PointerEventData data)
		{
			base.OnDrag(data);
		}

		new public void OnPointerUp(PointerEventData data)
		{
			base.OnPointerUp(data);
			isResizing = false;

			if (isLarge)
				largeWindowSize = _windowRectTransform.sizeDelta;

			AdjustRenderTexture();
		}
		void UpdateWindowSize()
		{
			Rect canvasRect = _windowRectTransform.parent.GetComponent<RectTransform>().rect;

			maxSize = new Vector2(canvasRect.max.x - _windowRectTransform.localPosition.x,
								  _windowRectTransform.localPosition.y - canvasRect.min.y);

			if (canvasRect.width / canvasRect.height < aspect)
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

		public void AdjustRenderTexture()
		{
			if (mapWindow == null || mapWindow._renderTexture == null || window == null)
				return;

			if (window.contentRectTransform != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(window.contentRectTransform);
			}

			RenderTexture rt = mapWindow._renderTexture;

			float displayH = (canvas != null && canvas.renderingDisplaySize.y > 0)
				? canvas.renderingDisplaySize.y
				: Screen.height;
			int displayHeight = Mathf.Max(500, (int)Mathf.Round(displayH - windowMargins.y));
			int displayWidth = Mathf.Max(500, (int)Mathf.Round(displayH * aspect - windowMargins.x));

			Vector2 currentWindowSize = _windowRectTransform != null ? _windowRectTransform.sizeDelta : originalSize;
			Vector2 estimatedContentSize = currentWindowSize - windowMargins;

			float scale = canvas != null ? canvas.scaleFactor : 1f;
			int contentW = Mathf.Max(100, Mathf.RoundToInt(estimatedContentSize.x * scale));
			int contentH = Mathf.Max(100, Mathf.RoundToInt(estimatedContentSize.y * scale));

			int width = Mathf.Max(contentW, displayWidth);
			int height = Mathf.Max(contentH, displayHeight);

			int msaa = (int)(MapEnhancer.Instance?.Settings.MSAA ?? MsaaQuality.Disabled);

			bool sizeChanged = rt.width != width || rt.height != height;
			bool msaaChanged = rt.antiAliasing != msaa;

			if (sizeChanged || msaaChanged || !rt.IsCreated())
			{
				if (mapCamera != null && mapCamera.targetTexture == rt)
					mapCamera.targetTexture = null;
				if (rt.IsCreated())
					rt.Release();

				rt.width = width;
				rt.height = height;
				rt.antiAliasing = msaa;
				rt.filterMode = FilterMode.Bilinear;
				rt.Create();

				if (mapWindow.rawImage != null)
				{
					mapWindow.rawImage.texture = rt;
					mapWindow.rawImage.SetMaterialDirty();
				}

				if (mapCamera != null)
				{
					mapCamera.targetTexture = rt;
				}
			}

			if (mapCamera != null)
			{
				mapCamera.aspect = (float)rt.width / (float)rt.height;
			}

			MapEnhancer.ConfigureMapRenderTexture(rt, mapCamera, msaa);
			bool cameraAllowMsaa = mapCamera != null && mapCamera.allowMSAA;
			if (rt.antiAliasing != lastLoggedAntiAliasing || cameraAllowMsaa != lastLoggedCameraAllowMsaa)
			{
				UMM.Loader.LogDebug($"MapResizer: map AA requested={msaa}x, active={rt.antiAliasing}x, cameraAllowMSAA={cameraAllowMsaa}, textureCreated={rt.IsCreated()}");
				lastLoggedAntiAliasing = rt.antiAliasing;
				lastLoggedCameraAllowMsaa = cameraAllowMsaa;
			}

			var contentRect = window.contentRectTransform != null && window.contentRectTransform.rect.width > 0
				? window.contentRectTransform.rect
				: new Rect(0, 0, Mathf.Max(100f, estimatedContentSize.x), Mathf.Max(100f, estimatedContentSize.y));

			if (mapWindow.mapBuilder != null)
			{
				mapWindow.mapBuilder.windowSizeFactor = (float)Mathf.Min(contentRect.width, contentRect.height) / 500f;
			}
		}

		private void CleanupRenderTexture()
		{
			Rect rect = window.contentRectTransform.rect;
			RenderTexture rt = mapWindow._renderTexture;
			if (mapCamera != null && mapCamera.targetTexture == rt)
				mapCamera.targetTexture = null;
			rt.Release();
			rt.width = (int)rect.width;
			rt.height = (int)rect.height;
			rt.antiAliasing = 1;
		}

		private void OnRectCanvasTransformChanged(RectTransform transform)
		{
			if (!isResizing)
				AdjustRenderTexture();
		}

		private void CreateDragHandle()
		{
			var image = transform.GetComponent<Image>();
			image.color = new Color(70f / 255f, 69f / 255f, 55f / 255f, 5f / 255f);
			image.sprite = Resources.Load<Sprite>("UI/ResizeWidget");

			var rect = GetComponent<RectTransform>();
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 4f, 22f);
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 4f, 22f);
		}

		public void SetMinimumSize(float size)
		{
			if (window == null) return;
			defaultSize = originalSize * (size / 800f);
			minSize = defaultSize; // Keep minSize for compatibility

			// Update absolute minimum to 50% of new default size
			base.minSize = defaultSize * 0.5f;

			if (!isLarge)
			{
				var windowRectTransform = window._rectTransform;
				var currentSize = windowRectTransform.sizeDelta;

				// Preserve user-resized size when settings change; only clamp up to the new minimum bounds.
				var clampedSize = new Vector2(
					Mathf.Max(currentSize.x, base.minSize.x),
					Mathf.Max(currentSize.y, base.minSize.y));

				if ((clampedSize - currentSize).sqrMagnitude > 0.01f)
				{
					windowRectTransform.sizeDelta = clampedSize;
				}
			}
		}
		public void Toggle()
		{
			if (window == null)
			{
				MapWindow.Show();
				return;
			}

			var isShown = window!.IsShown;
			if (!isShown)
			{
				window.ShowWindow();
				isLarge = false;
			}
			else
			{
				isLarge = !isLarge;
			}

			if (isLarge)
			{
				_windowRectTransform.anchoredPosition = largeWindowPos;
				_windowRectTransform.sizeDelta = largeWindowSize;
			}
			else
			{
				largeWindowPos = _windowRectTransform.anchoredPosition;
				_windowRectTransform.anchoredPosition = Vector2.zero;
				_windowRectTransform.sizeDelta = defaultSize; // Use defaultSize, not minSize
			}
			if (isShown)
				window.ClampToParentBounds();

			AdjustRenderTexture();
		}

		void OnDestroy()
		{
			Instance = null;
			if (aspectRatioFitter) DestroyImmediate(aspectRatioFitter);
			window._rectTransform.anchoredPosition = Vector2.zero;
			window._rectTransform.sizeDelta = originalSize;

			if (notifier) DestroyImmediate(notifier);
			CleanupRenderTexture();
		}
	}
}
