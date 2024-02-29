using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityModManagerNet;

namespace MapEnhancer.UMM;

#if DEBUG
[EnableReloading]
#endif
public static class Loader
{
	public static UnityModManager.ModEntry ModEntry { get; private set; }
	public static Harmony HarmonyInstance { get; private set; }
	public static MapEnhancer Instance { get; private set; }

	public static MapEnhancerSettings Settings;

	private static bool Load(UnityModManager.ModEntry modEntry)
	{
		if (ModEntry != null || Instance != null)
		{
			modEntry.Logger.Warning("Utilities is already loaded!");
			return false;
		}

		ModEntry = modEntry;
		Settings = UnityModManager.ModSettings.Load<MapEnhancerSettings>(modEntry);
		ModEntry.OnUnload = Unload;
		ModEntry.OnToggle = OnToggle;
		ModEntry.OnGUI = OnGUI;
		ModEntry.OnSaveGUI = Settings.Save;

		HarmonyInstance = new Harmony(modEntry.Info.Id);
		//Harmony.DEBUG = true;
		return true;
	}

	public static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
	{
		if (value)
		{
			try
			{
				HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
				var go = new GameObject("[MapEnhancer]");
				Instance = go.AddComponent<MapEnhancer>();
				UnityEngine.Object.DontDestroyOnLoad(go);
				Instance.Settings = Settings;
			}
			catch (Exception ex)
			{
				modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
				HarmonyInstance?.UnpatchAll(modEntry.Info.Id);
				if (Instance != null) UnityEngine.Object.DestroyImmediate(Instance.gameObject);
				Instance = null;
				return false;
			}
		}
		else
		{
			HarmonyInstance.UnpatchAll(modEntry.Info.Id);
			if (Instance != null) UnityEngine.Object.DestroyImmediate(Instance.gameObject);
			Instance = null;
		}

		return true;
	}

	private static bool Unload(UnityModManager.ModEntry modEntry)
	{
		
		return true;
	}

	public class MapEnhancerSettings : UnityModManager.ModSettings
	{
		public bool DoubleClick = false;

		public float FlareScale = 0.6f;
		public float JunctionMarkerScale = 0.6f;
		public float MarkerCutoff = 0.12f;

		public float MapZoomMin = 50f;
		public float MapZoomMax = 10000f;

		public float TrackLineThickness = 1.25f;

		public MsaaQuality MSAA = MsaaQuality._4x;

		public static readonly Color TrackColorMainlineOrig = new Color(0f, 0f, 1f, 1f);
		public static readonly Color TrackColorBranchOrig = new Color(0f, 0.572f, 0.792f, 1f);
		public static readonly Color TrackColorIndustrialOrig = new Color(0.749f, 0.749f, 0f, 1f);
		public static readonly Color TrackColorUnavailableOrig = new Color(1f, 0f, 0f, 1f);
		public static readonly Color[] TrackClassColorMap = {
			new Color(0,0,0,0),
			new Color(0,0,1,0),
			new Color(0,1,0,0),
			new Color(1,0,0,0)};

		public Color TrackColorMainline = TrackColorMainlineOrig;
		public Color TrackColorBranch = TrackColorBranchOrig;
		public Color TrackColorIndustrial = TrackColorIndustrialOrig;
		public Color TrackColorUnavailable = TrackColorUnavailableOrig;

		public override void Save(UnityModManager.ModEntry modEntry)
		{
			Save(this, modEntry);
		}

		public void OnChange()
		{
			Instance?.OnSettingsChanged();
		}
	}

	private static void OnGUI(UnityModManager.ModEntry modEntry)
	{
		bool changed = false;

		using (new GUILayout.VerticalScope())
		{
			using (new GUILayout.HorizontalScope())
			{
				var dc = GUILayout.Toggle(Settings.DoubleClick, "Require Double Click");
				if (Settings.DoubleClick != dc)
				{
					Settings.DoubleClick = dc;
					changed = true;
				}
			}

			GUILayout.Space(5);		
			GUILayout.Label("Map Zoom Min (lower = more zoom)");
			using (new GUILayout.HorizontalScope())
			{
				var zoomMin = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MapZoomMin, 50f, 100f, GUILayout.Width(UnityModManager.UI.Scale(200))), 0, MidpointRounding.AwayFromZero);
				GUILayout.Label(zoomMin.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MapZoomMin != zoomMin)
				{
					Settings.MapZoomMin = zoomMin;
					changed = true;
				}
			}

			GUILayout.Label("Map Zoom Max (higher = more zoom)");
			using (new GUILayout.HorizontalScope())
			{
				var zoomMax = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MapZoomMax, 5000f, 15000f, GUILayout.Width(UnityModManager.UI.Scale(200))), 0, MidpointRounding.AwayFromZero);
				GUILayout.Label(zoomMax.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MapZoomMax != zoomMax)
				{
					Settings.MapZoomMax = zoomMax;
					changed = true;
				}
			}

			GUILayout.Space(5);
			GUILayout.Label("Fusee Marker Scale");
			using (new GUILayout.HorizontalScope())
			{
				var fs = (float)Math.Round(GUILayout.HorizontalSlider(Settings.FlareScale, 0.1f, 1f, GUILayout.Width(UnityModManager.UI.Scale(200))), 1, MidpointRounding.AwayFromZero);
				GUILayout.Label(fs.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.FlareScale != fs)
				{
					Settings.FlareScale = fs;
					changed = true;
				}
			}

			GUILayout.Label("Junction Marker Scale");
			using (new GUILayout.HorizontalScope())
			{
				var ms = (float)Math.Round(GUILayout.HorizontalSlider(Settings.JunctionMarkerScale, 0.50f, 1f, GUILayout.Width(UnityModManager.UI.Scale(200))), 2, MidpointRounding.AwayFromZero);
				GUILayout.Label(ms.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.JunctionMarkerScale != ms)
				{
					Settings.JunctionMarkerScale = ms;
					changed = true;
				}
			}

			GUILayout.Label("Junction Non-Mainline Marker Cutoff");
			using (new GUILayout.HorizontalScope())
			{
				var co = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MarkerCutoff, 0.01f, 1f, GUILayout.Width(UnityModManager.UI.Scale(200))), 2, MidpointRounding.AwayFromZero);
				GUILayout.Label(co.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MarkerCutoff != co)
				{
					Settings.MarkerCutoff = co;
					changed = true;
				}
			}

			GUILayout.Space(5);
			GUILayout.Label("Track Line Thickness");
			using (new GUILayout.HorizontalScope())
			{
				var thickness = (float)Math.Round(GUILayout.HorizontalSlider(Settings.TrackLineThickness, 0.5f, 2f, GUILayout.Width(UnityModManager.UI.Scale(200))) * 4, 0, MidpointRounding.AwayFromZero) / 4;
				GUILayout.Label(thickness.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.TrackLineThickness != thickness)
				{
					Settings.TrackLineThickness = thickness;
					changed = true;
				}
			}

			GUILayout.Space(5);
			GUILayout.Label("Map Window Antialiasing");
			using (new GUILayout.HorizontalScope())
			{
				var values = (int[])Enum.GetValues(Settings.MSAA.GetType());
				int msaaIndex = Array.IndexOf(values, Settings.MSAA);
				if (UnityModManager.UI.PopupToggleGroup(ref msaaIndex, Enum.GetNames(Settings.MSAA.GetType()), null, GUILayout.ExpandWidth(false)))
				{
					if (values[msaaIndex] != (int)Settings.MSAA)
					{
						Settings.MSAA = (MsaaQuality)values[msaaIndex];
						changed = true;
					}
				}
			}

			GUILayout.Space(5);
			GUILayout.Label("Mainline Track Color");
			if (DrawColor(ref Settings.TrackColorMainline)) changed = true;
			GUILayout.Label("Branch/Yard Track Color");
			if (DrawColor(ref Settings.TrackColorBranch)) changed = true;
			GUILayout.Label("Industry Track Color");
			if (DrawColor(ref Settings.TrackColorIndustrial)) changed = true;
			GUILayout.Label("Unavailable Track Color");
			if (DrawColor(ref Settings.TrackColorUnavailable)) changed = true;
		}

		if (changed) Settings.OnChange();

		static bool DrawColor(ref Color color)
		{
			bool changed = false;
			using (new GUILayout.HorizontalScope())
			{
				using (new GUILayout.HorizontalScope("box"))
				{
					float r, g, b, a;
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"R: {color.r * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(UnityModManager.UI.Scale(133))))
						{
							r = (int)GUILayout.HorizontalSlider(color.r * 255f, 0f, 255f, GUILayout.Width(UnityModManager.UI.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"G: {color.g * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(UnityModManager.UI.Scale(133))))
						{
							g = (int)GUILayout.HorizontalSlider(color.g * 255f, 0f, 255f, GUILayout.Width(UnityModManager.UI.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"B: {color.b * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(UnityModManager.UI.Scale(133))))
						{
							b = (int)GUILayout.HorizontalSlider(color.b * 255f, 0f, 255f, GUILayout.Width(UnityModManager.UI.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"A: {color.a}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(UnityModManager.UI.Scale(133))))
						{
							a = (float)Math.Round(GUILayout.HorizontalSlider(color.a, 0f, 1f, GUILayout.Width(UnityModManager.UI.Scale(128))), 2, MidpointRounding.AwayFromZero);
						}
					}
					if (color.r != r || color.g != g || color.b != b || color.a != a)
					{
						color = new Color(r, g, b, a);
						changed = true;
					}
				}
				GUILayout.FlexibleSpace();
			}

			return changed;
		}
	}

	public static void Log(string str)
	{
		ModEntry?.Logger.Log(str);
	}

	public static void LogDebug(string str)
	{
#if DEBUG
		ModEntry?.Logger.Log(str);
#endif
	}
}
