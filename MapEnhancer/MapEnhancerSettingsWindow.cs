using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UI;
using UnityEngine.UI;
using UI.Builder;
using UI.Map;
using UI.Windows;
using Helpers;
using MapEnhancer.UMM;

namespace MapEnhancer
{
    /// <summary>
    /// Native in-game settings window for Railroader Map Enhancer using official Railroader 0.6 UI APIs:
    /// UI.Windows.WindowManager, CustomWindowDefinition, WindowConfig, UIPanelBuilder, and UI.Map.MapToolbar.
    /// Provides full coverage of all user-configurable properties with native styling and safe persistence.
    /// </summary>
    public static class MapEnhancerSettingsWindow
    {
        public const string WindowIdentifier = "reaper.mapenhancer.settings";
        public const string WindowTitle = "Map Enhancer Settings";

        private static IDisposable? _toolbarHandle;
        private static CustomWindowDefinition? _windowDefinition;

        private static Loader.MapEnhancerSettings? Settings => Loader.Settings;

        public static void RegisterWindow()
        {
            try
            {
                if (WindowManager.Shared == null)
                {
                    Loader.Log("[MapEnhancer] WindowManager.Shared not available for window registration.");
                    return;
                }

                _windowDefinition = new CustomWindowDefinition
                {
                    Identifier = WindowIdentifier,
                    Title = WindowTitle,
                    Config = WindowConfig.Resizable(new Vector2Int(500, 620), new Vector2Int(420, 380), Window.Position.Center),
                    BuilderAction = BuildSettingsPanel
                };

                WindowManager.Shared.RegisterCustomWindow(_windowDefinition);
                Loader.LogDebug($"[MapEnhancer] Registered custom settings window '{WindowIdentifier}'.");
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Failed to register custom settings window: {ex.Message}");
            }
        }

        public static void RegisterToolbarButton()
        {
            try
            {
                _toolbarHandle?.Dispose();
                _toolbarHandle = MapToolbar.Add(MapToolbarSide.Left, 10, builder =>
                {
                    builder.AddButtonCompact("ME", Toggle)
                           .Tooltip("Map Enhancer Settings", "Open the Map Enhancer configuration and preferences window (Ctrl+M).");
                });
                Loader.LogDebug("[MapEnhancer] Registered map toolbar button.");
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Failed to register map toolbar button: {ex.Message}");
            }
        }

        public static void Toggle()
        {
            try
            {
                if (WindowManager.Shared != null)
                {
                    WindowManager.Shared.ToggleWindow(WindowIdentifier);
                }
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Error toggling settings window: {ex.Message}");
            }
        }

        public static void Show()
        {
            try
            {
                if (WindowManager.Shared != null)
                {
                    WindowManager.Shared.ShowWindow(WindowIdentifier);
                }
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Error showing settings window: {ex.Message}");
            }
        }

        public static void Close()
        {
            try
            {
                if (WindowManager.Shared != null)
                {
                    WindowManager.Shared.CloseWindow(WindowIdentifier);
                }
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Error closing settings window: {ex.Message}");
            }
        }

        public static void Unregister()
        {
            try
            {
                Close();
                _toolbarHandle?.Dispose();
                _toolbarHandle = null;
                _windowDefinition = null;
                Loader.LogDebug("[MapEnhancer] Unregistered settings window and toolbar button.");
            }
            catch (Exception ex)
            {
                Loader.Log($"[MapEnhancer] Error during settings window cleanup: {ex.Message}");
            }
        }

        private static void BuildSettingsPanel(UIPanelBuilder builder)
        {
            var settings = Settings;
            if (settings == null)
            {
                builder.AddTitle("Map Enhancer", "Settings unavailable");
                return;
            }

            builder.VScrollView(scroll =>
            {
                scroll.FieldLabelWidth = 200f;

                // ==========================================================
                // Section 1: Map Display & Quick Overlay
                // ==========================================================
                scroll.AddSection("Map Display & Quick Overlay");

                scroll.AddFieldToggle("Map Quick Overlay HUD", () => settings.ShowAdvancedMapWindowSettings, v =>
                {
                    settings.ShowAdvancedMapWindowSettings = v;
                    ApplyAndSave();
                }, true).Tooltip("Quick Overlay HUD", "Show the upper-right quick teleport and overlay controls inside the Map Window.");

                scroll.AddFieldToggle("Show Switch Reset Option", () => settings.ShowSwitchResetOption, v =>
                {
                    settings.ShowSwitchResetOption = v;
                    ApplyAndSave();
                }, true).Tooltip("Switch Reset", "Show or hide the All to Normal / All to Thrown switch reset dropdown in the map controls.");

                scroll.AddFieldToggle("Double Click Switches", () => settings.DoubleClick, v =>
                {
                    settings.DoubleClick = v;
                    ApplyAndSave();
                }, true).Tooltip("Double Click", "Require double-clicking switch indicators to throw them instead of single click.");

                // Anti-Aliasing (MSAA) Dropdown
                var msaaLabels = new List<string> { "Disabled (1x)", "2x", "4x", "8x" };
                var msaaValues = new[] { MsaaQuality.Disabled, MsaaQuality._2x, MsaaQuality._4x, MsaaQuality._8x };
                int currentMsaaIdx = Array.IndexOf(msaaValues, settings.MSAA);
                if (currentMsaaIdx < 0) currentMsaaIdx = 2; // Default to Disabled (4x)

                scroll.AddField("Anti-Aliasing (MSAA)", scroll.AddDropdown(msaaLabels, currentMsaaIdx, idx =>
                {
                    if (idx >= 0 && idx < msaaValues.Length)
                    {
                        settings.MSAA = msaaValues[idx];
                        ApplyAndSave();
                    }
                })).Tooltip("MSAA Anti-Aliasing", "Render texture anti-aliasing quality for map track geometry and markers.");

                AddSliderField(scroll, "Map Window Min Size", () => settings.WindowSizeMin, v => settings.WindowSizeMin = v, 400f, 1600f, "{0:0} px");

                // ==========================================================
                // Section 2: Track Colors & Styling
                // ==========================================================
                scroll.AddSection("Track Colors & Classification");

                scroll.AddFieldToggle("Visual-Only Track Colors", () => settings.UseVisualOnlyTrackColors, v =>
                {
                    settings.UseVisualOnlyTrackColors = v;
                    ApplyAndSave();
                }, true).Tooltip("Visual Colors", "Color track segments by route classification (mainline, branch, industry, unavailable).");

                scroll.AddFieldToggle("Passenger Stop Tracking", () => settings.EnablePassengerStopTracking, v =>
                {
                    settings.EnablePassengerStopTracking = v;
                    ApplyAndSave();
                }, true).Tooltip("Passenger Stops", "Highlight station track spans designated for passenger stops.");

                scroll.AddFieldToggle("Industry Area Colors", () => settings.EnableIndustryAreaColors, v =>
                {
                    settings.EnableIndustryAreaColors = v;
                    ApplyAndSave();
                }, true).Tooltip("Industry Areas", "Color-code track segments according to industry servicing boundaries.");

                scroll.AddFieldToggle("Show MoW Yard Tracks", () => settings.ShowMoWYardTracks, v =>
                {
                    settings.ShowMoWYardTracks = v;
                    ApplyAndSave();
                }, true).Tooltip("MoW Yard Tracks", "Highlight yard-flagged track segments associated with industries; generic yard tracks are not included.");

                var mowColorModes = new List<string> { "Use Industry Colour", "Separate MoW Track Colour" };
                int currentMowColorMode = (int)settings.MoWTrackColorMode;
                if (currentMowColorMode < 0 || currentMowColorMode >= mowColorModes.Count) currentMowColorMode = 0;
                scroll.AddField("MoW Track Colour Mode", scroll.AddDropdown(mowColorModes, currentMowColorMode, idx =>
                {
                    if (idx >= 0 && idx < mowColorModes.Count)
                    {
                        settings.MoWTrackColorMode = (MoWTrackColorMode)idx;
                        ApplyAndSave();
                        scroll.SetNeedsRebuild();
                    }
                })).Tooltip("MoW Track Colour Mode", "Choose whether MoW yard tracks use their associated industry colour or a dedicated colour.");

                if (settings.MoWTrackColorMode == MoWTrackColorMode.SeparateMoWTrackColor)
                {
                    AddColorField(scroll, "MoW Track Colour", () => settings.MoWTrackColor, c => settings.MoWTrackColor = c, Loader.MapEnhancerSettings.MoWTrackColorOrig, () => scroll.SetNeedsRebuild());
                }

                AddSliderField(scroll, "Track Line Thickness", () => settings.TrackLineThickness, v => settings.TrackLineThickness = v, 1.5f, 4.0f, "{0:0.00}");

                // Restored Track Line Colors
                AddColorField(scroll, "Mainline Track Color", () => settings.TrackColorMainline, c => settings.TrackColorMainline = c, Loader.MapEnhancerSettings.TrackColorMainlineOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Branch Track Color", () => settings.TrackColorBranch, c => settings.TrackColorBranch = c, Loader.MapEnhancerSettings.TrackColorBranchOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Industrial Track Color", () => settings.TrackColorIndustrial, c => settings.TrackColorIndustrial = c, Loader.MapEnhancerSettings.TrackColorIndustrialOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Unavailable Track Color", () => settings.TrackColorUnavailable, c => settings.TrackColorUnavailable = c, Loader.MapEnhancerSettings.TrackColorUnavailableOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Passenger Stop Color", () => settings.TrackColorPax, c => settings.TrackColorPax = c, Loader.MapEnhancerSettings.TrackColorPaxOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Unreachable Track Color", () => settings.TrackColorUnreachable, c => settings.TrackColorUnreachable = c, Loader.MapEnhancerSettings.TrackColorUnreachableOrig, () => scroll.SetNeedsRebuild());

                scroll.AddButton("Reset Track Colors to Defaults", () =>
                {
                    ResetTrackColors();
                    scroll.SetNeedsRebuild();
                });

                // ==========================================================
                // Section 3: Track Grade Indicators & Colors
                // ==========================================================
                scroll.AddSection("Track Grade Indicators & Colors");

                scroll.AddFieldToggle("Grade on Hover", () => settings.ShowGradeOnHover, v =>
                {
                    settings.ShowGradeOnHover = v;
                    ApplyAndSave();
                }, true).Tooltip("Grade on Hover", "Show track grade percentage when hovering over tracks in the map.");

                scroll.AddFieldToggle("Grade Markers", () => settings.ShowGradeMarkers, v =>
                {
                    settings.ShowGradeMarkers = v;
                    ApplyAndSave();
                }, true).Tooltip("Grade Markers", "Display track slope and grade badges along track segments.");

                // Grade Marker Min Slope Quantized Slider (0.5% - 5.0% by 0.5% steps)
                var sliderQuantized = scroll.AddSliderQuantized(
                    () => settings.GradeMarkerMinIntensity,
                    () => $"{settings.GradeMarkerMinIntensity:0.0}%",
                    val =>
                    {
                        settings.GradeMarkerMinIntensity = Mathf.Round(val * 2f) / 2f;
                    },
                    0.5f,
                    0.5f,
                    5.0f,
                    val =>
                    {
                        settings.GradeMarkerMinIntensity = Mathf.Round(val * 2f) / 2f;
                        ApplyAndSave();
                    }
                );
                scroll.AddField("Grade Marker Min Slope", sliderQuantized)
                      .Tooltip("Min Slope", "Only display grade badges on track spans with slope at or exceeding this threshold (0.5% to 5.0%).");

                // Pointer Scale Presets (0.5%, 1%, 2%, 3%, 4%, 5%)
                var segControl = scroll.AddSegmentedControlCompact(seg =>
                {
                    float current = settings.GradeMarkerMinIntensity;
                    seg.AddSegment("0.5%", Mathf.Abs(current - 0.5f) < 0.1f, () => SetGradeSlope(0.5f, scroll));
                    seg.AddSegment("1.0%", Mathf.Abs(current - 1.0f) < 0.1f, () => SetGradeSlope(1.0f, scroll));
                    seg.AddSegment("2.0%", Mathf.Abs(current - 2.0f) < 0.1f, () => SetGradeSlope(2.0f, scroll));
                    seg.AddSegment("3.0%", Mathf.Abs(current - 3.0f) < 0.1f, () => SetGradeSlope(3.0f, scroll));
                    seg.AddSegment("4.0%", Mathf.Abs(current - 4.0f) < 0.1f, () => SetGradeSlope(4.0f, scroll));
                    seg.AddSegment("5.0%", Mathf.Abs(current - 5.0f) < 0.1f, () => SetGradeSlope(5.0f, scroll));
                }, true);
                scroll.AddField("Slope Pointer Scale", segControl)
                      .Tooltip("Slope Scale Presets", "Quickly select a standard slope cutoff threshold on the 1, 2, 3, 4, 5 scale.");

                var overlayOptions = new List<string> { "Routes (Default)", "Track Grade", "FRA Track Class", "Track Condition / Wear", "Vegetation Clearance" };
                scroll.AddField("Active Map Overlay", scroll.AddDropdown(overlayOptions, (int)settings.Overlay, idx =>
                {
                    var mode = (TrackOverlayMode)Mathf.Clamp(idx, 0, 4);
                    settings.Overlay = mode;
                    settings.EnableGradeColorOverlay = (mode == TrackOverlayMode.Grade);
                    ApplyAndSave();
                })).Tooltip("Active Map Overlay", "Select which intelligence layer to color-code track segments with on the map.");

                scroll.AddFieldToggle("Show Overlay Legend", () => settings.ShowTrackOverlayLegend, v =>
                {
                    settings.ShowTrackOverlayLegend = v;
                    ApplyAndSave();
                }, true).Tooltip("Show Overlay Legend", "Display the dynamic color legend above the map toolbar for active track overlays.");

                scroll.AddFieldToggle("Show Default Overlay Legend", () => settings.ShowDefaultOverlayLegend, v =>
                {
                    settings.ShowDefaultOverlayLegend = v;
                    ApplyAndSave();
                }, true).Tooltip("Show Default Overlay Legend", "Display the dynamic color legend when the default Routes overlay is active.");

                // Restored Grade Overlay Colors
                AddColorField(scroll, "Flat Grade (< 0.1%)", () => settings.GradeColorFlat, c => settings.GradeColorFlat = c, Loader.MapEnhancerSettings.GradeColorFlatOrig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "0% - 1% Grade", () => settings.GradeColor0to1, c => settings.GradeColor0to1 = c, Loader.MapEnhancerSettings.GradeColor0to1Orig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "1% - 2% Grade", () => settings.GradeColor1to2, c => settings.GradeColor1to2 = c, Loader.MapEnhancerSettings.GradeColor1to2Orig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "2% - 3% Grade", () => settings.GradeColor2to3, c => settings.GradeColor2to3 = c, Loader.MapEnhancerSettings.GradeColor2to3Orig, () => scroll.SetNeedsRebuild());
                AddColorField(scroll, "Above 3% Grade", () => settings.GradeColorAbove3, c => settings.GradeColorAbove3 = c, Loader.MapEnhancerSettings.GradeColorAbove3Orig, () => scroll.SetNeedsRebuild());

                scroll.AddButton("Reset Grade Colors to Defaults", () =>
                {
                    ResetGradeColors();
                    scroll.SetNeedsRebuild();
                });

                // ==========================================================
                // Section 4: Markers & Zoom Scaling
                // ==========================================================
                scroll.AddSection("Marker Scaling & Zoom");

                scroll.AddFieldToggle("Show Zoom Indicator", () => settings.ShowZoomIndicator, v =>
                {
                    settings.ShowZoomIndicator = v;
                    ApplyAndSave();
                }, true).Tooltip("Zoom Indicator", "Display current map zoom level percentage and distance in the corner of the map window.");

                AddSliderField(scroll, "Junction Marker Scale", () => settings.JunctionMarkerScale, v => settings.JunctionMarkerScale = v, 0.2f, 2.0f, "{0:0.00}x");
                AddSliderField(scroll, "Flare Marker Scale", () => settings.FlareScale, v => settings.FlareScale = v, 0.2f, 2.0f, "{0:0.00}x");
                AddSliderField(scroll, "Crossing Marker Scale", () => settings.CrossingMarkerScale, v => settings.CrossingMarkerScale = v, 0.1f, 1.0f, "{0:0.00}x");
                AddSliderField(scroll, "Marker Zoom Cutoff", () => settings.MarkerCutoff, v => settings.MarkerCutoff = v, 0.02f, 0.50f, "{0:0.00}");
                AddSliderField(scroll, "Map Min Zoom", () => settings.MapZoomMin, v => settings.MapZoomMin = v, 10f, 200f, "{0:0}");
                AddSliderField(scroll, "Map Max Zoom", () => settings.MapZoomMax, v => settings.MapZoomMax = v, 2000f, 30000f, "{0:0}");

                scroll.AddFieldToggle("Road Crossing Markers", () => settings.ShowRoadCrossingMarkers, v =>
                {
                    settings.ShowRoadCrossingMarkers = v;
                    ApplyAndSave();
                }, true).Tooltip("Road Crossing Markers", "Display highway/rail crossing warning markers on the map.");

                scroll.AddFieldToggle("Station Agents & Depots", () => settings.ShowStationMarkers, v =>
                {
                    settings.ShowStationMarkers = v;
                    ApplyAndSave();
                    MapEnhancer.Instance?.UpdateStationMarkersVisibility();
                }, true).Tooltip("Station Agents & Depots", "Enable hovering over station huts for directional passenger waiting information and clicking to open the Station Agent window.");

                AddSliderField(scroll, "Station Marker Scale", () => settings.StationMarkerScale, v => settings.StationMarkerScale = v, 0.2f, 2.0f, "{0:0.00}x");

                // ==========================================================
                // Section 5: Turntable Controls & Markers
                // ==========================================================
                scroll.AddSection("Turntable Controls & Markers");

                scroll.AddFieldToggle("Turntable Markers", () => settings.ShowTurntableMarkers, v =>
                {
                    settings.ShowTurntableMarkers = v;
                    ApplyAndSave();
                }, true).Tooltip("Turntable Markers", "Display circular markers and live rotation bridge beams on turntables.");

                AddSliderField(scroll, "Turntable Marker Scale", () => settings.TurntableMarkerScale, v => settings.TurntableMarkerScale = v, 0.2f, 1.5f, "{0:0.00}x");

                scroll.AddFieldToggle("Turntable Controls", () => settings.EnableTurntableControl, v =>
                {
                    settings.EnableTurntableControl = v;
                    ApplyAndSave();
                }, true).Tooltip("Turntable Controls", "Allow opening the native turntable control window and using rotation shortcuts from the map.");

                scroll.AddFieldToggle("Check Turntable Clearance", () => settings.CheckTurntableClearance, v =>
                {
                    settings.CheckTurntableClearance = v;
                    ApplyAndSave();
                }, true).Tooltip("Turntable Clearance", "Prevent rotating turntables when cars are fouling the lead tracks.");

                // ==========================================================
                // Section 6: Train & Cargo Telemetry
                // ==========================================================
                scroll.AddSection("Train & Cargo Telemetry");

                scroll.AddFieldToggle("Train Hover Tooltip", () => settings.ShowTrainHoverDetails, v =>
                {
                    settings.ShowTrainHoverDetails = v;
                    ApplyAndSave();
                }, true).Tooltip("Train Tooltip", "Show details when hovering over train icons on the map.");

                scroll.AddFieldToggle("Show Engineer / Driver", () => settings.ShowTrainDriver, v =>
                {
                    settings.ShowTrainDriver = v;
                    ApplyAndSave();
                }, true).Tooltip("Engineer Display", "Display the engineer or AI assignment in train hover details.");

                scroll.AddFieldToggle("Show Train Stats (Speed/Mass)", () => settings.ShowTrainStats, v =>
                {
                    settings.ShowTrainStats = v;
                    ApplyAndSave();
                }, true).Tooltip("Train Stats", "Display current velocity, car count, total tonnage, and length.");

                scroll.AddFieldToggle("Show Passenger Destinations", () => settings.ShowPassengerDestinations, v =>
                {
                    settings.ShowPassengerDestinations = v;
                    ApplyAndSave();
                }, true).Tooltip("Passenger Destinations", "Display passenger counts grouped by destination in train hover details.");

                scroll.AddFieldToggle("Show Fuel / Tender Levels", () => settings.ShowFuel, v =>
                {
                    settings.ShowFuel = v;
                    ApplyAndSave();
                }, true).Tooltip("Fuel Telemetry", "Show coal, water, and diesel percentages in train hover tooltips.");

                scroll.AddFieldToggle("Show Consist Cargo Summary", () => settings.ShowTrainCargoSummary, v =>
                {
                    settings.ShowTrainCargoSummary = v;
                    ApplyAndSave();
                }, true).Tooltip("Cargo Summary", "Summarize loaded commodities and cargo status across the consist.");

                scroll.AddFieldToggle("Show Waybill Destinations", () => settings.ShowDestinations, v =>
                {
                    settings.ShowDestinations = v;
                    ApplyAndSave();
                }, true).Tooltip("Destinations", "Display car destination towns and industries.");

                scroll.AddFieldToggle("Individual Car Hover Tooltip", () => settings.ShowIndividualCarTooltip, v =>
                {
                    settings.ShowIndividualCarTooltip = v;
                    ApplyAndSave();
                }, true).Tooltip("Car Tooltips", "Show per-car details when hovering specific cars in consist.");

                scroll.AddFieldToggle("Monitor Operational Alerts", () => settings.ShowOperationalAlerts, v =>
                {
                    settings.ShowOperationalAlerts = v;
                    ApplyAndSave();
                }, true).Tooltip("Operational Alerts", "Collect HOPA conditions for the rolling stock alerts window. Map indicator visibility is controlled separately below.");

                if (settings.ShowOperationalAlerts)
                {
                    scroll.AddFieldToggle("  • Monitor Hotbox", () => settings.ShowHotboxAlert, v =>
                    {
                        settings.ShowHotboxAlert = v;
                        ApplyAndSave();
                    }, true).Tooltip("Hotbox Detection", "Include hotbox conditions in the active alert collection.");

                    scroll.AddFieldToggle("  • Monitor Low Journal Oil", () => settings.ShowLowOilAlert, v =>
                    {
                        settings.ShowLowOilAlert = v;
                        ApplyAndSave();
                    }, true).Tooltip("Low Oil Detection", "Include journal oil below 25% in the active alert collection.");

                    scroll.AddFieldToggle("  • Monitor Applied Handbrake", () => settings.ShowHandbrakeAlert, v =>
                    {
                        settings.ShowHandbrakeAlert = v;
                        ApplyAndSave();
                    }, true).Tooltip("Handbrake Detection", "Include applied handbrakes in the active alert collection.");

                    scroll.AddFieldToggle("  • Monitor Air Leak", () => settings.ShowAirLeakAlert, v =>
                    {
                        settings.ShowAirLeakAlert = v;
                        ApplyAndSave();
                    }, true).Tooltip("Air Leak Detection", "Include the existing anglecock and exhaust flow conditions in the active alert collection.");

                    scroll.AddFieldToggle("  • Monitor Derailment", () => settings.ShowDerailmentAlert, v =>
                    {
                        settings.ShowDerailmentAlert = v;
                        ApplyAndSave();
                    }, true).Tooltip("Derailment Detection", "Include cars whose verified game derailment state is active in the alert collection.");
                }

                scroll.AddSection("HOPA Map Indicators");
                scroll.AddFieldToggle("Enable HOPA Map Indicators", () => settings.EnableHopaMapIndicators, v =>
                {
                    settings.EnableHopaMapIndicators = v;
                    ApplyAndSave();
                }, true).Tooltip("HOPA Map Indicators", "Show or hide HOPA map visualization. Alert collection and the alert window continue to work when this is off.");
                if (settings.EnableHopaMapIndicators)
                {
                    scroll.AddFieldToggle("Show Hotbox Indicators", () => settings.ShowHotboxIndicator, v => { settings.ShowHotboxIndicator = v; ApplyAndSave(); }, true);
                    scroll.AddFieldToggle("Show Low Journal Oil Indicators", () => settings.ShowLowOilIndicator, v => { settings.ShowLowOilIndicator = v; ApplyAndSave(); }, true);
                    scroll.AddFieldToggle("Show Applied Handbrake Indicators", () => settings.ShowHandbrakeIndicator, v => { settings.ShowHandbrakeIndicator = v; ApplyAndSave(); }, true);
                    scroll.AddFieldToggle("Show Air Leak Indicators", () => settings.ShowAirLeakIndicator, v => { settings.ShowAirLeakIndicator = v; ApplyAndSave(); }, true);
                    scroll.AddFieldToggle("Show Derailment Indicators", () => settings.ShowDerailmentIndicator, v => { settings.ShowDerailmentIndicator = v; ApplyAndSave(); }, true);
                    scroll.AddFieldToggle("Show Map Alert Summary", () => settings.ShowHopaMapSummary, v => { settings.ShowHopaMapSummary = v; ApplyAndSave(); }, true);
                    AddSliderField(scroll, "Alert Summary Text Size", () => settings.HopaSummaryTextSize, v => settings.HopaSummaryTextSize = v, 8f, 16f, "{0:0} px");
                    AddSliderField(scroll, "Alert Indicator Scale", () => settings.HopaIndicatorScale, v => settings.HopaIndicatorScale = v, 0.75f, 2f, "{0:0.00}x");
                }

                // ==========================================================
                // Section 7: Navigation & Shortcuts
                // ==========================================================
                scroll.AddSection("Navigation & Shortcuts");

                scroll.AddFieldToggle("Include Modded Spawn Points", () => settings.EnableModdedSpawnPoints, v =>
                {
                    settings.EnableModdedSpawnPoints = v;
                    ApplyAndSave();
                }, true).Tooltip("Modded Spawn Points", "Include third-party and custom mod spawn points in the map teleport dropdown.");

                scroll.AddField("Active Keybindings", "Map: Z | Follow: Shift+Z | Recenter: Ctrl+Z | Settings: Ctrl+M")
                      .Tooltip("Keybindings", "Configurable via settings.json: mapToggle, mapFollow, mapRecenter, and mapSettingsToggle.");

                // ==========================================================
                // Section 8: Diagnostics & Maintenance
                // ==========================================================
                scroll.AddSection("Diagnostics & Maintenance");

                scroll.AddFieldToggle("Verbose Debug Logging", () => settings.EnableDebugLogging, v =>
                {
                    settings.EnableDebugLogging = v;
                    ApplyAndSave();
                }, true).Tooltip("Debug Logging", "Output detailed diagnostic and telemetry logs to Player.log.");

                scroll.AddButton("Reset All Settings to Defaults", () =>
                {
                    ResetToDefaults();
                    scroll.SetNeedsRebuild();
                });
            });
        }

        private static void SetGradeSlope(float slope, UIPanelBuilder scroll)
        {
            if (Settings == null) return;
            Settings.GradeMarkerMinIntensity = slope;
            ApplyAndSave();
            scroll.SetNeedsRebuild();
        }

        private static void AddSliderField(UIPanelBuilder builder, string label, Func<float> getter, Action<float> setter, float min, float max, string format)
        {
            var slider = builder.AddSlider(
                getter,
                () => string.Format(format, getter()),
                val =>
                {
                    setter(val);
                    Settings?.OnChange();
                },
                min,
                max,
                false,
                val =>
                {
                    setter(val);
                    ApplyAndSave();
                }
            );

            builder.AddField(label, slider);
        }

        private static void AddColorField(
            UIPanelBuilder builder,
            string label,
            Func<Color> getter,
            Action<Color> setter,
            Color defaultColor,
            Action onRebuildNeeded)
        {
            var current = getter();
            string currentHex = ColorHelper.HexFromColor(current);
            string defaultHex = ColorHelper.HexFromColor(defaultColor);

            Image? swatchImg = null;

            var hstack = builder.HStack(h =>
            {
                // Ingame color picker dropdown button with embedded color swatch
                var pickerRt = h.AddColorDropdown(currentHex, hex =>
                {
                    if (ColorHelper.TryColorFromHex(hex, out var picked))
                    {
                        if (swatchImg != null) swatchImg.color = picked;
                        setter(picked);
                        ApplyAndSave();
                    }
                });

                // Constrain the dropdown button to match compact UI height and provide width for swatch + caret
                var le = pickerRt.GetComponent<LayoutElement>() ?? pickerRt.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 48f;
                le.preferredHeight = 20f;
                le.minWidth = 48f;
                le.minHeight = 20f;

                // Color swatch embedded INSIDE the dropdown button (leaves space on the right for dropdown arrow caret)
                var swatchGo = new GameObject("ColorSwatch", typeof(RectTransform), typeof(Image), typeof(Outline));
                swatchGo.transform.SetParent(pickerRt, false);
                swatchGo.transform.SetAsFirstSibling();
                var swatchRect = swatchGo.GetComponent<RectTransform>();
                swatchRect.anchorMin = Vector2.zero;
                swatchRect.anchorMax = Vector2.one;
                swatchRect.offsetMin = new Vector2(2f, 2f);
                swatchRect.offsetMax = new Vector2(-18f, -2f); // Leaves 18px on the right for the dropdown arrow caret

                swatchImg = swatchGo.GetComponent<Image>();
                swatchImg.color = current;
                swatchImg.raycastTarget = false; // Clicks pass through to the DropdownColorPicker button

                var outline = swatchGo.GetComponent<Outline>();
                outline.effectColor = new Color(0.12f, 0.12f, 0.12f, 0.75f);
                outline.effectDistance = new Vector2(1f, -1f);

                // Compact Reset to Default Button
                h.AddButtonCompact("Reset", () =>
                {
                    if (swatchImg != null) swatchImg.color = defaultColor;
                    setter(defaultColor);
                    var picker = pickerRt.GetComponent<DropdownColorPicker>();
                    picker?.Configure(defaultHex, hex =>
                    {
                        if (ColorHelper.TryColorFromHex(hex, out var picked))
                        {
                            if (swatchImg != null) swatchImg.color = picked;
                            setter(picked);
                            ApplyAndSave();
                        }
                    });
                    ApplyAndSave();
                }).Tooltip("Reset to Default", $"Restore original default color ({defaultHex}).");
            });

            builder.AddField(label, hstack);
        }

        private static void ApplyAndSave()
        {
            Settings?.OnChange();
            Settings?.Save();
            MapEnhancer.Instance?.RefreshOverlayDropdown();
            MapEnhancer.Instance?.UpdateDynamicLegend();
            MapBuilder.Shared?.RefreshSegmentColors();
        }

        private static void ResetTrackColors()
        {
            var settings = Settings;
            if (settings == null) return;

            settings.TrackColorMainline = Loader.MapEnhancerSettings.TrackColorMainlineOrig;
            settings.TrackColorBranch = Loader.MapEnhancerSettings.TrackColorBranchOrig;
            settings.TrackColorIndustrial = Loader.MapEnhancerSettings.TrackColorIndustrialOrig;
            settings.TrackColorUnavailable = Loader.MapEnhancerSettings.TrackColorUnavailableOrig;
            settings.TrackColorPax = Loader.MapEnhancerSettings.TrackColorPaxOrig;
            settings.TrackColorUnreachable = Loader.MapEnhancerSettings.TrackColorUnreachableOrig;
            settings.MoWTrackColor = Loader.MapEnhancerSettings.MoWTrackColorOrig;

            ApplyAndSave();
            Loader.Log("[MapEnhancer] Track colors reset to default values.");
        }

        private static void ResetGradeColors()
        {
            var settings = Settings;
            if (settings == null) return;

            settings.GradeColorFlat = Loader.MapEnhancerSettings.GradeColorFlatOrig;
            settings.GradeColor0to1 = Loader.MapEnhancerSettings.GradeColor0to1Orig;
            settings.GradeColor1to2 = Loader.MapEnhancerSettings.GradeColor1to2Orig;
            settings.GradeColor2to3 = Loader.MapEnhancerSettings.GradeColor2to3Orig;
            settings.GradeColorAbove3 = Loader.MapEnhancerSettings.GradeColorAbove3Orig;

            ApplyAndSave();
            Loader.Log("[MapEnhancer] Grade overlay colors reset to default values.");
        }

        private static void ResetToDefaults()
        {
            var settings = Settings;
            if (settings == null) return;

            var defaults = new Loader.MapEnhancerSettings();

            // Display & HUD
            settings.ShowAdvancedMapWindowSettings = defaults.ShowAdvancedMapWindowSettings;
            settings.ShowSwitchResetOption = defaults.ShowSwitchResetOption;
            settings.DoubleClick = defaults.DoubleClick;
            settings.MSAA = defaults.MSAA;
            settings.WindowSizeMin = defaults.WindowSizeMin;

            // Track Classification & Colors
            settings.UseVisualOnlyTrackColors = defaults.UseVisualOnlyTrackColors;
            settings.EnablePassengerStopTracking = defaults.EnablePassengerStopTracking;
            settings.EnableIndustryAreaColors = defaults.EnableIndustryAreaColors;
            settings.ShowMoWYardTracks = defaults.ShowMoWYardTracks;
            settings.MoWTrackColorMode = defaults.MoWTrackColorMode;
            settings.TrackLineThickness = defaults.TrackLineThickness;
            settings.TrackColorMainline = defaults.TrackColorMainline;
            settings.TrackColorBranch = defaults.TrackColorBranch;
            settings.TrackColorIndustrial = defaults.TrackColorIndustrial;
            settings.TrackColorUnavailable = defaults.TrackColorUnavailable;
            settings.TrackColorPax = defaults.TrackColorPax;
            settings.TrackColorUnreachable = defaults.TrackColorUnreachable;
            settings.MoWTrackColor = defaults.MoWTrackColor;

            // Grade Indicators & Colors
            settings.ShowGradeOnHover = defaults.ShowGradeOnHover;
            settings.ShowGradeMarkers = defaults.ShowGradeMarkers;
            settings.GradeMarkerMinIntensity = defaults.GradeMarkerMinIntensity;
            settings.EnableGradeColorOverlay = defaults.EnableGradeColorOverlay;
            settings.Overlay = defaults.Overlay;
            settings.ShowTrackOverlayLegend = defaults.ShowTrackOverlayLegend;
            settings.ShowDefaultOverlayLegend = defaults.ShowDefaultOverlayLegend;
            settings.GradeColorFlat = defaults.GradeColorFlat;
            settings.GradeColor0to1 = defaults.GradeColor0to1;
            settings.GradeColor1to2 = defaults.GradeColor1to2;
            settings.GradeColor2to3 = defaults.GradeColor2to3;
            settings.GradeColorAbove3 = defaults.GradeColorAbove3;

            // Markers & Scaling
            settings.JunctionMarkerScale = defaults.JunctionMarkerScale;
            settings.FlareScale = defaults.FlareScale;
            settings.CrossingMarkerScale = defaults.CrossingMarkerScale;
            settings.MarkerCutoff = defaults.MarkerCutoff;
            settings.MapZoomMin = defaults.MapZoomMin;
            settings.MapZoomMax = defaults.MapZoomMax;
            settings.ShowRoadCrossingMarkers = defaults.ShowRoadCrossingMarkers;
            settings.ShowZoomIndicator = defaults.ShowZoomIndicator;

            // Turntables
            settings.ShowTurntableMarkers = defaults.ShowTurntableMarkers;
            settings.EnableTurntableControl = defaults.EnableTurntableControl;
            settings.CheckTurntableClearance = defaults.CheckTurntableClearance;
            settings.TurntableMarkerColor = defaults.TurntableMarkerColor;
            settings.TurntableMarkerScale = defaults.TurntableMarkerScale;

            // Telemetry
            settings.ShowTrainHoverDetails = defaults.ShowTrainHoverDetails;
            settings.ShowTrainDriver = defaults.ShowTrainDriver;
            settings.ShowTrainStats = defaults.ShowTrainStats;
            settings.ShowPassengerDestinations = defaults.ShowPassengerDestinations;
            settings.ShowFuel = defaults.ShowFuel;
            settings.ShowTrainCargoSummary = defaults.ShowTrainCargoSummary;
            settings.ShowDestinations = defaults.ShowDestinations;
            settings.ShowIndividualCarTooltip = defaults.ShowIndividualCarTooltip;

            // HOPA presentation (legacy monitor toggles remain backward compatible)
            settings.ShowOperationalAlerts = defaults.ShowOperationalAlerts;
            settings.ShowHotboxAlert = defaults.ShowHotboxAlert;
            settings.ShowLowOilAlert = defaults.ShowLowOilAlert;
            settings.ShowHandbrakeAlert = defaults.ShowHandbrakeAlert;
            settings.ShowAirLeakAlert = defaults.ShowAirLeakAlert;
            settings.ShowDerailmentAlert = defaults.ShowDerailmentAlert;
            settings.EnableHopaMapIndicators = defaults.EnableHopaMapIndicators;
            settings.ShowHotboxIndicator = defaults.ShowHotboxIndicator;
            settings.ShowLowOilIndicator = defaults.ShowLowOilIndicator;
            settings.ShowHandbrakeIndicator = defaults.ShowHandbrakeIndicator;
            settings.ShowAirLeakIndicator = defaults.ShowAirLeakIndicator;
            settings.ShowDerailmentIndicator = defaults.ShowDerailmentIndicator;
            settings.ShowHopaMapSummary = defaults.ShowHopaMapSummary;
            settings.HopaSummaryTextSize = defaults.HopaSummaryTextSize;
            settings.HopaIndicatorScale = defaults.HopaIndicatorScale;
            settings.GroupHopaAlertsByConsist = defaults.GroupHopaAlertsByConsist;
            settings.KeepHopaAlertsWindowOpenAfterSelection = defaults.KeepHopaAlertsWindowOpenAfterSelection;

            // Navigation & Diagnostics
            settings.EnableModdedSpawnPoints = defaults.EnableModdedSpawnPoints;
            settings.EnableDebugLogging = defaults.EnableDebugLogging;

            ApplyAndSave();
            Loader.Log("[MapEnhancer] All settings reset to default values.");
        }
    }
}
