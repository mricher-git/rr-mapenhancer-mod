using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MapEnhancer
{
    /// <summary>
    /// Official Railroader 0.6 entry point discovered by the game's mod manager.
    /// </summary>
    public sealed class MapEnhancerEntry : IModEntryPoint, IWorldLifecycle
    {
        static MapEnhancerEntry()
        {
            PreloadHarmony(null);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHarmony;
        }

        private static void PreloadHarmony(string? modDir)
        {
            try
            {
                string? targetDir = modDir;
                if (string.IsNullOrEmpty(targetDir))
                {
                    var loc = typeof(MapEnhancerEntry).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        targetDir = Path.GetDirectoryName(loc);
                    }
                }

                if (!string.IsNullOrEmpty(targetDir))
                {
                    var harmonyPath = Path.Combine(targetDir, "0Harmony.dll");
                    if (File.Exists(harmonyPath))
                    {
                        Assembly.LoadFrom(harmonyPath);
                    }
                }
            }
            catch
            {
            }
        }

        private static Assembly? ResolveHarmony(object sender, ResolveEventArgs args)
        {
            try
            {
                var reqName = new AssemblyName(args.Name).Name;
                if (string.Equals(reqName, "0Harmony", StringComparison.OrdinalIgnoreCase))
                {
                    var loc = typeof(MapEnhancerEntry).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        var dir = Path.GetDirectoryName(loc);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            var harmonyPath = Path.Combine(dir, "0Harmony.dll");
                            if (File.Exists(harmonyPath))
                            {
                                return Assembly.LoadFrom(harmonyPath);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public void OnModLoad(IModContext context)
        {
            PreloadHarmony(context?.ModDirectory);
            UMM.Loader.OnModLoad(context);
        }

        public void OnModUnload()
        {
            UMM.Loader.OnModUnload();
        }

        public void OnWorldLoad()
        {
            UMM.Loader.OnWorldLoad();
        }

        public void OnWorldUnload()
        {
            UMM.Loader.OnWorldUnload();
        }
    }
}

namespace MapEnhancer.UMM
{
    [Serializable]
    public class KeyBinding
    {
        public KeyCode keyCode = KeyCode.None;
        public int modifiers = 0; // 0 = None, 1 = Shift, 2 = Ctrl, 4 = Alt

        public bool Down()
        {
            if (keyCode == KeyCode.None) return false;
            if (!Input.GetKeyDown(keyCode)) return false;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if ((modifiers & 1) != 0 && !shift) return false;
            if ((modifiers & 2) != 0 && !ctrl) return false;
            if ((modifiers & 4) != 0 && !alt) return false;
            return true;
        }

        public bool IsPressed => Down();
    }

    public static class Loader
    {
        public const string HarmonyId = "vanguard.mapenhancer";

        public static IModContext? Context { get; private set; }
        public static IModLogger? Logger => Context?.Log;
        public static Harmony? HarmonyInstance { get; private set; }
        public static MapEnhancer? Instance { get; private set; }
        public static bool IsModLoaded { get; private set; }

        public static MapEnhancerSettings Settings = new MapEnhancerSettings();

        public static void OnModLoad(IModContext context)
        {
            if (IsModLoaded)
            {
                context.Log.Warning("MapEnhancer is already loaded!");
                return;
            }

            IsModLoaded = true;
            Context = context;

            Logger?.Info($"[MapEnhancer] Initializing official mod from {context.ModDirectory}...");

            // Load or migrate persistent settings
            Settings = MapEnhancerSettings.Load(context.ModDirectory);

            try
            {
                HarmonyInstance = new Harmony(HarmonyId);
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                Logger?.Info("[MapEnhancer] Harmony patches initialized.");
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "[MapEnhancer] Failed to apply Harmony patches:");
            }
        }

        public static void OnWorldLoad()
        {
            Logger?.Info("[MapEnhancer] OnWorldLoad: Setting up session components...");

            if (Instance == null)
            {
                var go = new GameObject("[MapEnhancer]");
                Instance = go.AddComponent<MapEnhancer>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                Instance.Settings = Settings;
            }

            if (Instance.GetComponent<HopaAlertCollector>() == null)
            {
                Instance.gameObject.AddComponent<HopaAlertCollector>();
            }

            MapEnhancerSettingsWindow.RegisterWindow();
            MapEnhancerSettingsWindow.RegisterToolbarButton();
            MapEnhancerAlertsWindow.Register();
        }

        public static void OnWorldUnload()
        {
            Logger?.Info("[MapEnhancer] OnWorldUnload: Cleaning up session components...");

            MapEnhancerSettingsWindow.Unregister();
            MapEnhancerAlertsWindow.Unregister();
            HopaAlertManager.Clear();

            CanvasCuller.Clear();

            if (Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(Instance.gameObject);
                Instance = null;
            }
        }

        public static void OnModUnload()
        {
            Logger?.Info("[MapEnhancer] OnModUnload: Shutting down mod...");

            MapEnhancerSettingsWindow.Unregister();
            MapEnhancerAlertsWindow.Unregister();
            HopaAlertManager.Clear();

            CanvasCuller.Clear();

            if (Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(Instance.gameObject);
                Instance = null;
            }

            try
            {
                HarmonyInstance?.UnpatchAll(HarmonyId);
                HarmonyInstance = null;
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "[MapEnhancer] Error while unpatching Harmony:");
            }

            Settings.Save(Context?.ModDirectory);
            IsModLoaded = false;
            Context = null;
        }

        public class ColorConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("r");
                writer.WriteValue(value.r);
                writer.WritePropertyName("g");
                writer.WriteValue(value.g);
                writer.WritePropertyName("b");
                writer.WriteValue(value.b);
                writer.WritePropertyName("a");
                writer.WriteValue(value.a);
                writer.WriteEndObject();
            }

            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return Color.white;
                var obj = JObject.Load(reader);
                float r = obj["r"]?.Value<float>() ?? 1f;
                float g = obj["g"]?.Value<float>() ?? 1f;
                float b = obj["b"]?.Value<float>() ?? 1f;
                float a = obj["a"]?.Value<float>() ?? 1f;
                return new Color(r, g, b, a);
            }
        }

        public class MapEnhancerSettings
        {
            public const string SettingsFileName = "settings.json";

            private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Converters = { new ColorConverter() }
            };

            public KeyBinding mapToggle = new KeyBinding() { keyCode = KeyCode.Z };
            public KeyBinding mapFollow = new KeyBinding() { modifiers = 1, keyCode = KeyCode.Z };
            public KeyBinding mapRecenter = new KeyBinding() { modifiers = 2, keyCode = KeyCode.Z };
            public KeyBinding mapSettingsToggle = new KeyBinding() { modifiers = 2, keyCode = KeyCode.M };

            public bool DoubleClick = false;

            public float FlareScale = 0.6f;
            public float JunctionMarkerScale = 0.6f;
            public float MarkerCutoff = 0.12f;

            public float WindowSizeMin = 800f;
            public float MapZoomMin = 50f;
            public float MapZoomMax = 10000f;

            public float TrackLineThickness = 2.0f;

            public MsaaQuality MSAA = MsaaQuality.Disabled;

            public static readonly Color TrackColorMainlineOrig = new Color(0f, 155f / 255f, 0f, 1f); // Green (RGB 0, 155, 0)
            public static readonly Color TrackColorBranchOrig = new Color(0f, 0.572f, 0.792f, 1f);
            public static readonly Color TrackColorIndustrialOrig = new Color(0.749f, 0.749f, 0f, 1f);
            public static readonly Color TrackColorUnavailableOrig = new Color(1f, 0f, 0f, 1f);
            public static readonly Color[] TrackClassColorMap = {
                new Color(0,0,0,0),
                new Color(0,0,1,0),
                new Color(0,1,0,0),
                new Color(1,0,0,0)
            };

            public Color TrackColorMainline = TrackColorMainlineOrig;
            public Color TrackColorBranch = TrackColorBranchOrig;
            public Color TrackColorIndustrial = TrackColorIndustrialOrig;
            public Color TrackColorUnavailable = TrackColorUnavailableOrig;
            public static readonly Color TrackColorPaxOrig = new Color(0.5f, 0f, 0.5f, 1f); // Purple
            public Color TrackColorPax = TrackColorPaxOrig;
            public static readonly Color TrackColorUnreachableOrig = new Color(0.7f, 0.7f, 0.7f, 1f); // Light grey
            public Color TrackColorUnreachable = TrackColorUnreachableOrig;
            public static readonly Color MoWTrackColorOrig = new Color(255f / 255f, 220f / 255f, 94f / 255f, 1f);
            public Color MoWTrackColor = MoWTrackColorOrig;

            // Feature toggles
            public bool UseVisualOnlyTrackColors = true;
            public bool EnablePassengerStopTracking = false;
            public bool EnableIndustryAreaColors = true;
            public bool ShowMoWYardTracks = true;
            public MoWTrackColorMode MoWTrackColorMode = MoWTrackColorMode.SeparateMoWTrackColor;
            public bool EnableModdedSpawnPoints = false;
            public bool ShowAdvancedMapWindowSettings = false;
            public bool ShowSwitchResetOption = false;

            // Turntable marker settings
            public bool ShowTurntableMarkers = true;
            public bool EnableTurntableControl = true;
            public bool CheckTurntableClearance = true;
            public Color TurntableMarkerColor = new Color(0.8f, 0.5f, 0.2f, 0.4f);
            public float TurntableMarkerScale = 0.497439742f;
            public bool ShowZoomIndicator = true;
            public bool ShowRoadCrossingMarkers = true;
            public float CrossingMarkerScale = 0.3f;

            // Passenger Station Markers
            public bool ShowStationMarkers = true;
            public float StationMarkerScale = 0.5f;

            // Operational Alerts (Map)
            public bool ShowOperationalAlerts = true;
            public bool ShowHotboxAlert = true;
            public bool ShowLowOilAlert = true;
            public bool ShowHandbrakeAlert = true;
            public bool ShowAirLeakAlert = false;
            public bool ShowDerailmentAlert = true;

            // HOPA map presentation (collection continues when these are disabled)
            public bool EnableHopaMapIndicators = true;
            public bool ShowHotboxIndicator = true;
            public bool ShowLowOilIndicator = true;
            public bool ShowHandbrakeIndicator = false;
            public bool ShowAirLeakIndicator = false;
            public bool ShowDerailmentIndicator = true;
            public bool ShowHopaMapSummary = true;
            public float HopaSummaryTextSize = 13.7951813f;
            public float HopaIndicatorScale = 1.25f;
            public bool GroupHopaAlertsByConsist = true;
            public bool KeepHopaAlertsWindowOpenAfterSelection = true;

            // Grade Indicators
            public bool ShowGradeOnHover = true;
            public bool ShowGradeMarkers = false;
            public float GradeMarkerMinIntensity = 1.0f;
            public bool EnableGradeColorOverlay = false;
            public TrackOverlayMode Overlay = TrackOverlayMode.Routes;
            public bool ShowTrackOverlayLegend = true;
            public bool ShowDefaultOverlayLegend = false;

            public static readonly Color GradeColorFlatOrig = new Color(0.267f, 0.467f, 0.800f, 1f); // Blue
            public static readonly Color GradeColor0to1Orig = new Color(0.133f, 0.733f, 0.267f, 1f); // Green
            public static readonly Color GradeColor1to2Orig = new Color(0.867f, 0.800f, 0.133f, 1f); // Yellow
            public static readonly Color GradeColor2to3Orig = new Color(0.933f, 0.533f, 0.067f, 1f); // Orange
            public static readonly Color GradeColorAbove3Orig = new Color(0.800f, 0.133f, 0.133f, 1f); // Red

            public Color GradeColorFlat = GradeColorFlatOrig;
            public Color GradeColor0to1 = GradeColor0to1Orig;
            public Color GradeColor1to2 = GradeColor1to2Orig;
            public Color GradeColor2to3 = GradeColor2to3Orig;
            public Color GradeColorAbove3 = GradeColorAbove3Orig;

            // Train Intelligence Layer
            public bool ShowTrainHoverDetails = true;
            public bool ShowTrainDriver       = true;
            public bool ShowTrainStats        = true;
            public bool ShowPassengerDestinations = true;

            // Cargo Intelligence Layer
            public bool ShowFuel                 = true;
            public bool ShowTrainCargoSummary    = true;
            public bool ShowDestinations         = true;
            public bool ShowIndividualCarTooltip = true;
            public bool EnableDebugLogging       = false;

            public static MapEnhancerSettings Load(string? modDirectory)
            {
                if (string.IsNullOrEmpty(modDirectory))
                {
                    return new MapEnhancerSettings();
                }

                string jsonPath = Path.Combine(modDirectory, SettingsFileName);
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        var loaded = JsonConvert.DeserializeObject<MapEnhancerSettings>(json, SerializerSettings);
                        if (loaded != null)
                        {
                            if (!Enum.IsDefined(typeof(TrackOverlayMode), loaded.Overlay))
                            {
                                loaded.Overlay = TrackOverlayMode.Routes;
                            }
                            if (!Enum.IsDefined(typeof(MoWTrackColorMode), loaded.MoWTrackColorMode))
                            {
                                loaded.MoWTrackColorMode = MoWTrackColorMode.UseIndustryColor;
                            }
                            if (loaded.Overlay == TrackOverlayMode.Routes && loaded.EnableGradeColorOverlay)
                            {
                                loaded.Overlay = TrackOverlayMode.Grade;
                            }
                            loaded.EnableGradeColorOverlay = (loaded.Overlay == TrackOverlayMode.Grade);

                            Loader.Log($"Settings loaded from {jsonPath}");
                            return loaded;
                        }
                    }
                    catch (Exception ex)
                    {
                        Loader.Log($"Error reading settings file {jsonPath}: {ex.Message}. Using defaults without overwriting.");
                        return new MapEnhancerSettings();
                    }
                }

                return new MapEnhancerSettings();
            }

            public void Save(object? _ = null)
            {
                Save(Loader.Context?.ModDirectory);
            }

            public void Save(string? modDirectory)
            {
                if (string.IsNullOrEmpty(modDirectory)) return;

                string jsonPath = Path.Combine(modDirectory, SettingsFileName);
                try
                {
                    string json = JsonConvert.SerializeObject(this, SerializerSettings);
                    File.WriteAllText(jsonPath, json);
                    Loader.LogDebug($"Settings saved to {jsonPath}");
                }
                catch (Exception ex)
                {
                    Loader.Log($"Failed to save settings to {jsonPath}: {ex.Message}");
                }
            }

            public void OnChange()
            {
                Instance?.OnSettingsChanged();
            }
        }

        public static void Log(string str)
        {
            if (Logger != null)
            {
                Logger.Info(str);
            }
            else
            {
                Debug.Log($"[MapEnhancer] {str}");
            }
        }

        public static void LogDebug(string str)
        {
            if (Settings?.EnableDebugLogging == true)
            {
                if (Logger != null)
                {
                    Logger.Debug(str);
                }
                else
                {
                    Debug.Log($"[MapEnhancer][DEBUG] {str}");
                }
            }
#if DEBUG
            else
            {
                if (Logger != null)
                {
                    Logger.Debug(str);
                }
                else
                {
                    Debug.Log($"[MapEnhancer][DEBUG] {str}");
                }
            }
#endif
        }
    }
}
