using Helpers;
using Helpers.Culling;
using System;
using UI.Map;
using UnityEngine;

public class CanvasCuller : MonoBehaviour, CullingManager.ICullingEventHandler
{
	private static CullingManager? _canvasCullingManager;

	public static void Clear()
	{
		if (_canvasCullingManager != null)
		{
			Destroy(_canvasCullingManager.gameObject);
			_canvasCullingManager = null;
		}
	}

	private void Awake()
	{
		canvas = transform.GetComponent<Canvas>();
		if (canvas == null)
		{
			enabled = false;
			Destroy(this);
			return;
		}
	}

	private void OnEnable()
	{
		if (canvas == null)
		{
			canvas = transform.GetComponent<Canvas>();
			if (canvas == null)
			{
				enabled = false;
				Destroy(this);
				return;
			}
		}

		if (_cullingToken != null)
		{
			_cullingToken.Dispose();
			_cullingToken = null;
		}

		if (_canvasCullingManager == null)
		{
			GameObject go = new GameObject("MapEnhancer_CanvasCullingManager");
			DontDestroyOnLoad(go);
			// Deactivate before AddComponent so CullingManager.OnEnable() does not execute before Configure()
			go.SetActive(false);
			_canvasCullingManager = go.AddComponent<CullingManager>();
			_canvasCullingManager.Configure("canvas", new[] { float.PositiveInfinity }, 128);
			go.SetActive(true);
		}

		var camera = MapBuilder.Shared?.mapCamera;
		if (camera != null && _canvasCullingManager != null && _canvasCullingManager._cullingGroup != null)
		{
			_canvasCullingManager._cullingGroup.targetCamera = camera;
			_canvasCullingManager._cullingGroup.SetDistanceReferencePoint(camera.transform);
		}

		if (_canvasCullingManager != null)
		{
			_cullingToken = _canvasCullingManager.AddSphere(transform, radius, this);
			_cullingToken?.RegisterFixedUpdate(transform);
		}
	}

	private void OnDisable()
	{
		if (_cullingToken != null)
		{
			_cullingToken.Dispose();
			_cullingToken = null;
		}
	}

	private void OnDestroy()
	{
		if (_cullingToken != null)
		{
			_cullingToken.Dispose();
			_cullingToken = null;
		}
	}

	public void CullingSphereStateChanged(bool isVisible, int distanceBand)
	{
		CanvasCuller.CullingState cullingState = (isVisible ? CanvasCuller.CullingState.Visible : CanvasCuller.CullingState.NotVisible);
		if (cullingState == _cullingState)
		{
			return;
		}
		_cullingState = cullingState;
		if (canvas != null)
		{
			canvas.enabled = (_cullingState == CanvasCuller.CullingState.Visible);
		}
	}

	public void RequestUpdateCullingPosition()
	{
		_cullingToken?.UpdatePosition(transform);
	}

	private CullingManager.Token? _cullingToken;

	private CanvasCuller.CullingState _cullingState;

	private Canvas canvas;

	public float radius = 30f;

	private enum CullingState
	{
		Unknown,
		Visible,
		NotVisible
	}
}
