using Core;
using GalaSoft.MvvmLight.Messaging;
using Game;
using Game.AccessControl;
using Game.Events;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using MapEnhancer.UMM;
using Model.OpsNew;
using System;
using System.Collections.Generic;
using System.Linq;
using Track;
using Track.Signals;
using UI.Map;
using UnityEngine;

namespace MapEnhancer;

public class MapEnhancer : MonoBehaviour
{
	public enum MapStates { MAINMENU, MAPLOADED, MAPUNLOADING }
	public static MapStates MapState { get; private set; } = MapStates.MAINMENU;
	internal Loader.MapEnhancerSettings Settings;
	public GameObject Junctions;
	public GameObject JunctionsBranch;
	public GameObject JunctionsMainline;
	private List<Entry> junctionMarkers = new List<Entry>();
	private CullingGroup cullingGroup;
	private BoundingSphere[] cullingSpheres;

	private static HashSet<string> _mainlineSegments;
	public static HashSet<string> mainlineSegments
	{
		get
		{
			if (_mainlineSegments == null)
				populateSegmentsAndSwitches();

			return _mainlineSegments!;
		}
	}

	private static HashSet<string> _mainlineSwitches;
	public static HashSet<string> mainlineSwitches
	{
		get
		{
			if (_mainlineSwitches == null)
				populateSegmentsAndSwitches();

			return _mainlineSwitches!;
		}
	}

	private static void populateSegmentsAndSwitches()
	{
		_mainlineSegments = new HashSet<string>();
		_mainlineSwitches = new HashSet<string>();
		foreach (var span in FindObjectsOfType<CTCBlock>(true).SelectMany(block => block.Spans))
		{
			span.UpdateCachedPointsIfNeeded();
			foreach (var seg in span._cachedSegments)
			{
				_mainlineSegments.Add(seg.id);
				_mainlineSwitches.Add(seg.a.id);
				_mainlineSwitches.Add(seg.b.id);
			}
		}
	}

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

		if (MapState == MapStates.MAPLOADED)
		{
			OnMapWillUnload(new MapWillUnloadEvent());
			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
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
		JunctionsMainline = new GameObject("Mainline Junctions");
		JunctionsMainline.transform.SetParent(Junctions.transform, false);
		JunctionsBranch = new GameObject("Branch Junctions");
		JunctionsBranch.transform.SetParent(Junctions.transform, false);
		Junctions.SetActive(MapWindow.instance._window.IsShown);
		MapWindow.instance._window.OnShownDidChange += OnMapWindowShown;

		Messenger.Default.Register<WorldDidMoveEvent>(this, new Action<WorldDidMoveEvent>(this.WorldDidMove));
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;

		Rebuild();
		OnSettingsChanged();
	}

	private void OnMapWillUnload(MapWillUnloadEvent evt)
	{
		Loader.LogDebug("OnMapWillUnload");

		MapState = MapStates.MAPUNLOADING;
		Messenger.Default.Unregister<WorldDidMoveEvent>(this);
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}
		cullingGroup = null;

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
			if (mainlineSwitches.Contains(node.id))
				junctionMarker.transform.SetParent(JunctionsMainline.transform, false);
			else
				junctionMarker.transform.SetParent(JunctionsBranch.transform, false);
			junctionMarkers.Add(new Entry(sd, junctionMarker));
			junctionMarker.transform.localPosition = sd.geometry.switchHome + Vector3.up * 50f;
			junctionMarker.transform.localRotation = sd.geometry.aPointRail.Points.First().Rotation;
			JunctionMarker jm = sd.geometry.aPointRail.hand == Hand.Right ?
				JunctionMarker.junctionMarkerPrefabL :
				JunctionMarker.junctionMarkerPrefabR;
			
			jm = GameObject.Instantiate(jm, junctionMarker.transform);
			jm.junction = node;
		}
	}

	public void OnSettingsChanged()
	{
		if (MapState != MapStates.MAPLOADED) return;

		foreach (var junctionMarker in Junctions.GetComponentsInChildren<CanvasRenderer>(true))
		{
			var rt = junctionMarker.GetComponent<RectTransform>();
			if (rt != null)
			{
				rt.anchoredPosition = new Vector2(Mathf.Sign(rt.anchoredPosition.x) * (Settings.MarkerScale * 40f + 8f), 0f);
				rt.localScale = new Vector3(Settings.MarkerScale * 2f, Settings.MarkerScale, Settings.MarkerScale * 2f);
			}
		}

		if (MapWindow.instance._window.IsShown)
		{
			MapWindow.instance.mapBuilder.Rebuild();
			MapBuilder.Shared.UpdateForZoom();
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

			Instance?.Rebuild();

			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorMainline), MethodType.Getter)]
	private static class TrackColorMainlinePatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Loader.Settings.TrackColorMainline;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorBranch), MethodType.Getter)]
	private static class TrackColorBranchPatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Loader.Settings.TrackColorBranch;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorIndustrial), MethodType.Getter)]
	private static class TrackColorIndustrialPatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Loader.Settings.TrackColorIndustrial;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorUnavailable), MethodType.Getter)]
	private static class TrackColorUnavailablePatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Loader.Settings.TrackColorUnavailable;

			return false;
		}
	}

	[HarmonyPatch(typeof(TrackSegment), nameof(TrackSegment.Awake))]
	private static class SegmentTrackClassPatch
	{
		private static void Postfix(TrackSegment __instance)
		{
			Loader.LogDebug($"Setting {__instance.name} {mainlineSegments.Contains(__instance.id)}");
			if (mainlineSegments.Contains(__instance.id))
				__instance.trackClass = TrackClass.Mainline;
			else
				__instance.trackClass = TrackClass.Branch;
		}
	}

	/*
	[HarmonyPatch(typeof(PassengerStop), nameof(PassengerStop.OnEnable))]
	private static class PaxTrackClassPatch
	{
		private static void Postfix(PassengerStop __instance)
		{
			foreach (var tspan in __instance.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					seg.trackClass = Track.TrackClass.Industrial;
				}
			}
		}
	}
	*/

	[HarmonyPatch(typeof(IndustryComponent), nameof(IndustryComponent.Start))]
	private static class IndustryTrackClassPatch
	{
		private static void Postfix(IndustryComponent __instance)
		{
			if (__instance is ProgressionIndustryComponent) return;
			foreach (var tspan in __instance.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					seg.trackClass = Track.TrackClass.Industrial;
				}
			}
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.UpdateForZoom))]
	private static class MapBuilderZoomPatch
	{
		private static void Postfix(MapBuilder __instance)
		{
				Instance?.JunctionsBranch?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff);
		}
	}

	[HarmonyPatch(typeof(TrainController), nameof(TrainController.HandleRequestSetSwitch))]
	private static class HostAccessLevelSetSwitchPatch
	{
		private static bool Prefix(TrainController __instance, RequestSetSwitch setSwitch, IPlayer sender)
		{
			TrackNode node = __instance.graph.GetNode(setSwitch.nodeId);
			if (node.IsCTCSwitch && HostManager.Shared.AccessLevelForPlayerId(sender.PlayerId) < AccessLevel.Dispatcher) return false;
			return true;
		}
	}
}
