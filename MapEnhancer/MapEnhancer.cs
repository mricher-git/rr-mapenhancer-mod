using Core;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.State;
using HarmonyLib;
using Helpers;
using MapEnhancer.UMM;
using System;
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
	public static GameObject Junctions;

	void Start()
	{
		Messenger.Default.Register<MapDidLoadEvent>(this, new Action<MapDidLoadEvent>(this.OnMapDidLoad));
		Messenger.Default.Register<MapWillUnloadEvent>(this, new Action<MapWillUnloadEvent>(this.OnMapWillUnload));

		if (StateManager.Shared.Storage != null)
		{
			OnMapDidLoad(new MapDidLoadEvent());
			//CreateSwitches();
		}
	}

	void OnDestroy()
	{
		Loader.LogDebug("OnDestroy");

		if (Junctions != null) Destroy(Junctions);
	
		if (JunctionMarker.matJunctionGreen != null) Destroy(JunctionMarker.matJunctionGreen);
		if (JunctionMarker.matJunctionRed != null) Destroy(JunctionMarker.matJunctionRed);

		if (JunctionMarker.junctionMarkerPrefabL != null)
		{
			Destroy(JunctionMarker.junctionMarkerPrefabL.transform.parent.gameObject);
		}
		Messenger.Default.Unregister<MapDidLoadEvent>(this);
		Messenger.Default.Unregister<MapWillUnloadEvent>(this);

		if (MapState != MapStates.MAPUNLOADING)
		{
			Messenger.Default.Unregister<WorldDidMoveEvent>(this);
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
		Messenger.Default.Register<WorldDidMoveEvent>(this, new Action<WorldDidMoveEvent>(this.WorldDidMove));
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;

		CreateSwitches();
		OnSettingsChanged();
	}

	private static void CreateSwitches()
	{
		Loader.LogDebug("CreateSwitches");
		foreach (var jm in Junctions.GetComponentsInChildren<JunctionMarker>()) Destroy(jm.transform.parent.gameObject);

		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var sd = kvp.Value;
			TrackNode node = sd.node;

			if (node.IsCTCSwitch || !Graph.Shared.IsSwitch(node)) continue;
			
			var junctionMarker = new GameObject($"JunctionMarker ({node.id})");
			junctionMarker.transform.SetParent(Junctions.transform, false);

			junctionMarker.transform.localPosition = sd.geometry.switchHome + Vector3.up * 100f;
			junctionMarker.transform.localRotation = sd.geometry.aPointRail.Points.First().Rotation;

			GameObject indicator = sd.geometry.aPointRail.hand == Hand.Right ?
					indicator = GameObject.Instantiate<GameObject>(JunctionMarker.junctionMarkerPrefabL, junctionMarker.transform) :
					indicator = GameObject.Instantiate<GameObject>(JunctionMarker.junctionMarkerPrefabR, junctionMarker.transform);

			indicator.GetComponent<JunctionMarker>().Init(node);
		}
	}

	private void OnMapWillUnload(MapWillUnloadEvent evt)
	{
		Loader.LogDebug("OnMapWillUnload");

		MapState = MapStates.MAPUNLOADING;
		Messenger.Default.Unregister<WorldDidMoveEvent>(this);
		Destroy(Junctions);
	}

	private void WorldDidMove(WorldDidMoveEvent evt)
	{
		Loader.LogDebug("WorldDidMove");
		//Vector3 offset = evt.Offset;
		//Junctions.transform.position += offset;
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;
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

	[HarmonyPatch(typeof(TrackObjectManager), nameof(TrackObjectManager.Rebuild))]
	private static class TrackObjectManagerRebuildPatch
	{
		private static void Postfix()
		{
			if (MapState != MapStates.MAPLOADED) return;
			CreateSwitches();

			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
		}
	}
}

