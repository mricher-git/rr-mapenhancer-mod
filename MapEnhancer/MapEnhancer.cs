using Core;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.State;
using HarmonyLib;
using Helpers;
using MapEnhancer.UMM;
using System;
using System.Collections.Generic;
using System.Linq;
using Track;
using UI.Map;
using UnityEngine;

namespace MapEnhancer;

public class MapEnhancer : MonoBehaviour
{
	public enum MapStates { MAINMENU, MAPLOADED, MAPUNLOADING }
	public static MapStates MapState { get; private set; } = MapStates.MAINMENU;
	internal Loader.MapEnhancerSettings Settings;
	public GameObject Junctions;
	private List<Entry> junctionMarkers = new List<Entry>();
	private CullingGroup cullingGroup;
	private BoundingSphere[] cullingSpheres;
	public static MapEnhancer Instance
	{
		get { return Loader.Instance; }
	}

	void Start()
	{
		Messenger.Default.Register<MapDidLoadEvent>(this, new Action<MapDidLoadEvent>(this.OnMapDidLoad));
		Messenger.Default.Register<MapWillUnloadEvent>(this, new Action<MapWillUnloadEvent>(this.OnMapWillUnload));

		if (StateManager.Shared.Storage != null)
		{
			OnMapDidLoad(new MapDidLoadEvent());
		}
	}

	void OnDestroy()
	{
		Loader.LogDebug("OnDestroy");

		if (JunctionMarker.matJunctionGreen != null) Destroy(JunctionMarker.matJunctionGreen);
		if (JunctionMarker.matJunctionRed != null) Destroy(JunctionMarker.matJunctionRed);

		if (JunctionMarker.junctionMarkerPrefabL != null)
		{
			Destroy(JunctionMarker.junctionMarkerPrefabL.transform.parent.gameObject);
		}
		Messenger.Default.Unregister<MapDidLoadEvent>(this);
		Messenger.Default.Unregister<MapWillUnloadEvent>(this);

		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}
		cullingGroup = null;

		if (MapState != MapStates.MAPUNLOADING)
		{
			OnMapWillUnload(new MapWillUnloadEvent());
		}
		MapState = MapStates.MAINMENU;
	}

	private void OnMapDidLoad(MapDidLoadEvent evt)
	{
		Loader.LogDebug("OnMapDidLoad");
		if (MapState == MapStates.MAPLOADED) return;
		Loader.LogDebug("OnMapDidLoad2");

		MapState = MapStates.MAPLOADED;
		JunctionMarker.CreatePrefab();

		Junctions = new GameObject("Junctions");
		Junctions.SetActive(MapWindow.instance._window.IsShown);
		MapWindow.instance._window.OnShownDidChange += OnMapWindowShown;

		Messenger.Default.Register<WorldDidMoveEvent>(this, new Action<WorldDidMoveEvent>(this.WorldDidMove));
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;

		//CreateSwitches();
		Rebuild();
		OnSettingsChanged();
	}

	private void OnMapWillUnload(MapWillUnloadEvent evt)
	{
		Loader.LogDebug("OnMapWillUnload");

		MapState = MapStates.MAPUNLOADING;
		Messenger.Default.Unregister<WorldDidMoveEvent>(this);
		if (Junctions != null) Destroy(Junctions);
		junctionMarkers.Clear();
		MapWindow.instance._window.OnShownDidChange -= OnMapWindowShown;
	}

	private void WorldDidMove(WorldDidMoveEvent evt)
	{
		Loader.LogDebug("WorldDidMove");

		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;
		UpdateCullingSpheres();
	}

	private void OnMapWindowShown(bool shown)
	{
		Junctions?.SetActive(shown);
	}

	public void Rebuild()
	{
		Loader.LogDebug("Rebuild");
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}

		CreateSwitches();

		Camera mapCamera = MapBuilder.Shared.mapCamera;
		cullingGroup = new CullingGroup();
		cullingGroup.targetCamera = mapCamera;
		cullingGroup.SetBoundingSphereCount(0);
		cullingGroup.SetBoundingSpheres(cullingSpheres);
		cullingGroup.onStateChanged = new CullingGroup.StateChanged(CullingGroupStateChanged);
		cullingGroup.SetBoundingDistances(new float[] { float.PositiveInfinity });
		cullingGroup.SetDistanceReferencePoint(mapCamera.transform);
		//List<MapBuilder.Entry> list = (_entries = graph.Segments.Select((TrackSegment s) => new MapBuilder.Entry(s)).ToList<MapBuilder.Entry>());
		cullingSpheres = new BoundingSphere[junctionMarkers.Count];
		UpdateCullingSpheres();
		cullingGroup.SetBoundingSpheres(cullingSpheres);
		cullingGroup.SetBoundingSphereCount(cullingSpheres.Length);
	}

	private void CullingGroupStateChanged(CullingGroupEvent sphere)
	{
		int index = sphere.index;
	
		var sd = junctionMarkers[index].SwitchDescriptor;

		if (sphere.isVisible && !sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(true);
		}
		else if (!sphere.isVisible && sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(false);
		}
	}

	private void UpdateCullingSpheres()
	{
		for (int i = 0; i < TrackObjectManager.Instance._descriptors.switches.Count; i++)
		{
			var geo = junctionMarkers[i].SwitchDescriptor.geometry;
			Vector3 vector = WorldTransformer.GameToWorld(geo.switchHome);
			this.cullingSpheres[i] = new BoundingSphere(vector, 1f);
		}
	}

	private void CreateSwitches()
	{
		Loader.LogDebug("CreateSwitches");
		//foreach (var jm in Junctions.GetComponentsInChildren<JunctionMarker>()) Destroy(jm.transform.parent.gameObject);
		foreach (var jm in junctionMarkers) Destroy(jm.JunctionMarker);
		junctionMarkers.Clear();
		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var sd = kvp.Value;
			TrackNode node = sd.node;
			
			var junctionMarker = new GameObject($"JunctionMarker ({node.id})");
			junctionMarker.SetActive(false);
			junctionMarker.transform.SetParent(Junctions.transform, false);
			junctionMarkers.Add(new Entry(sd, junctionMarker));
			junctionMarker.transform.localPosition = sd.geometry.switchHome + Vector3.up * 100f;
			junctionMarker.transform.localRotation = sd.geometry.aPointRail.Points.First().Rotation;

			GameObject indicator = sd.geometry.aPointRail.hand == Hand.Right ?
					indicator = GameObject.Instantiate<GameObject>(JunctionMarker.junctionMarkerPrefabL, junctionMarker.transform) :
					indicator = GameObject.Instantiate<GameObject>(JunctionMarker.junctionMarkerPrefabR, junctionMarker.transform);

			indicator.GetComponent<JunctionMarker>().Init(node);
		}
	}

	public void OnSettingsChanged()
	{
		foreach (var junctionMarker in Junctions.GetComponentsInChildren<CanvasRenderer>())
		{
			var rt = junctionMarker.GetComponent<RectTransform>();
			if (rt != null)
			{
				rt.anchoredPosition = new Vector2(Mathf.Sign(rt.anchoredPosition.x) * (Settings.MarkerScale * 43.75f + 10f), 0f);
				rt.localScale = new Vector3(Settings.MarkerScale * 2f, Settings.MarkerScale, 1f);
			}
		}
	}

	private class Entry
	{
		public Entry(TrackObjectManager.SwitchDescriptor switchDescriptor, GameObject junctionMarker)
		{
			SwitchDescriptor = switchDescriptor;
			JunctionMarker = junctionMarker;
		}

		public readonly TrackObjectManager.SwitchDescriptor SwitchDescriptor;
		public readonly GameObject JunctionMarker;
	}

	[HarmonyPatch(typeof(TrackObjectManager), nameof(TrackObjectManager.Rebuild))]
	private static class TrackObjectManagerRebuildPatch
	{
		private static void Postfix()
		{
			if (MapState != MapStates.MAPLOADED) return;
			//Instance?.CreateSwitches();
			Instance?.Rebuild();

			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
		}
	}
}

