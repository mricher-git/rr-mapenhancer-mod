using System;
using Game.Mow;
using MapEnhancer.UMM;
using Track;
using UnityEngine;

namespace MapEnhancer
{
    public enum MoWTrackColorMode
    {
        UseIndustryColor = 0,
        SeparateMoWTrackColor = 1
    }

    /// <summary>
    /// Centralized track visualization modes supported by Map Enhancer.
    /// </summary>
    public enum TrackOverlayMode
    {
        Routes = 0,
        Grade = 1,
        TrackClass = 2,
        Condition = 3,
        Cleanliness = 4
    }

    /// <summary>
    /// Centralized overlay engine responsible for resolving track segment colors across all overlay modes,
    /// deterministic banding algorithms, and safe fallbacks for missing or invalid game states.
    /// </summary>
    public static class TrackOverlayManager
    {
        /// <summary>
        /// FRA track speed class colors (Class 0 through Class 5).
        /// Class 0: Red (10 mph)
        /// Class 1: Orange (15 mph)
        /// Class 2: Yellow (25 mph)
        /// Class 3: Green (40 mph)
        /// Class 4: Cyan (60 mph)
        /// Class 5: Blue (80 mph)
        /// </summary>
        public static readonly Color[] ClassColors =
        {
            new Color(0.70f, 0.15f, 0.15f, 1f), // Class 0 (Red)
            new Color(0.90f, 0.40f, 0.10f, 1f), // Class 1 (Orange)
            new Color(0.90f, 0.75f, 0.15f, 1f), // Class 2 (Yellow)
            new Color(0.35f, 0.70f, 0.30f, 1f), // Class 3 (Green)
            new Color(0.15f, 0.70f, 0.80f, 1f), // Class 4 (Cyan)
            new Color(0.40f, 0.40f, 0.90f, 1f)  // Class 5 (Blue)
        };

        /// <summary>
        /// Maintenance & clearing colors (5 bands: 0%, 1-24%, 25-49%, 50-74%, 75-100%).
        /// Band 0: Red (Critical wear / Overgrown)
        /// Band 1: Orange (Severe wear)
        /// Band 2: Yellow (Moderate wear)
        /// Band 3: Light Green (Fair condition)
        /// Band 4: Cyan/Blue (Pristine / Cleared)
        /// </summary>
        public static readonly Color[] MaintenanceColors =
        {
            new Color(0.75f, 0.15f, 0.18f, 1f), // Band 0 (Red)
            new Color(0.90f, 0.40f, 0.10f, 1f), // Band 1 (Orange)
            new Color(0.90f, 0.75f, 0.15f, 1f), // Band 2 (Yellow)
            new Color(0.35f, 0.70f, 0.30f, 1f), // Band 3 (Light Green)
            new Color(0.10f, 0.65f, 0.85f, 1f)  // Band 4 (Cyan/Blue)
        };

        public static readonly Color UnknownColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>
        /// Categorizes absolute track gradient percentage into 5 deterministic bands:
        /// 0: &lt; 0.1% (Flat)
        /// 1: 0.1% - 1.0% (Easy)
        /// 2: 1.0% - 2.0% (Moderate)
        /// 3: 2.0% - 3.0% (Steep)
        /// 4: &gt;= 3.0% (Very Steep)
        /// Returns -1 for NaN or Infinity.
        /// </summary>
        public static int GradeBand(float percent)
        {
            if (float.IsNaN(percent) || float.IsInfinity(percent)) return -1;
            percent = Math.Abs(percent);
            return percent < 0.1f ? 0 : percent < 1.0f ? 1 : percent < 2.0f ? 2 : percent < 3.0f ? 3 : 4;
        }

        /// <summary>
        /// Validates FRA track class (0 through 5). Returns -1 for invalid or uninitialized classes.
        /// </summary>
        public static int ClassBand(int trackClass)
        {
            return (trackClass >= 0 && trackClass <= 5) ? trackClass : -1;
        }

        /// <summary>
        /// Categorizes normalized [0.0, 1.0] unit values (condition or vegetation clearance) into 5 bands:
        /// 0: &lt;= 0% (0.0)
        /// 1: 1% - 24% (0.001 - 0.249)
        /// 2: 25% - 49% (0.250 - 0.499)
        /// 3: 50% - 74% (0.500 - 0.749)
        /// 4: 75% - 100% (0.750 - 1.000)
        /// Returns -1 for NaN or Infinity.
        /// </summary>
        public static int UnitBand(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return -1;
            return value <= 0f ? 0 : value < 0.25f ? 1 : value < 0.50f ? 2 : value < 0.75f ? 3 : 4;
        }

        /// <summary>
        /// Resolves the final track color for a segment based on the active overlay mode and precedence rules.
        /// Ensures all colors are fully opaque (alpha = 1.0f) for the analytical SDF quad renderer.
        /// </summary>
        public static Color ResolveColor(TrackSegment segment, Color nativeColor, Loader.MapEnhancerSettings settings)
        {
            if (segment == null || settings == null) return nativeColor;

            var mode = settings.Overlay;

            // Precedence 1: Unavailable track
            if (!segment.Available)
            {
                if (mode == TrackOverlayMode.Routes)
                {
                    return settings.EnableIndustryAreaColors
                        ? settings.TrackColorUnreachable
                        : settings.TrackColorUnavailable;
                }
                // Non-route overlays preserve the game's native unavailable styling (or gray)
                return nativeColor;
            }

            // Precedence 2: Track currently under construction
            if (segment.ConstructionState != default)
            {
                // Incomplete track retains the game's native construction color
                return nativeColor;
            }

            switch (mode)
            {
                case TrackOverlayMode.Grade:
                {
                    float grade = 0f;
                    if (MapEnhancer._segmentGrades.TryGetValue(segment.id, out var info))
                    {
                        grade = info.Grade;
                    }
                    else
                    {
                        grade = MapEnhancer.ComputeSegmentGrade(segment);
                    }

                    int band = GradeBand(grade);
                    switch (band)
                    {
                        case 0: return settings.GradeColorFlat;
                        case 1: return settings.GradeColor0to1;
                        case 2: return settings.GradeColor1to2;
                        case 3: return settings.GradeColor2to3;
                        case 4: return settings.GradeColorAbove3;
                        default: return UnknownColor;
                    }
                }

                case TrackOverlayMode.TrackClass:
                {
                    int band = ClassBand(segment.PostedTrackClass);
                    return (band >= 0 && band < ClassColors.Length) ? ClassColors[band] : UnknownColor;
                }

                case TrackOverlayMode.Condition:
                {
                    float condition = float.NaN;
                    try
                    {
                        var controller = TrackConditionController.Shared;
                        if (controller != null)
                        {
                            condition = controller.GetCondition(segment);
                        }
                    }
                    catch
                    {
                        condition = float.NaN;
                    }

                    int band = UnitBand(condition);
                    return (band >= 0 && band < MaintenanceColors.Length) ? MaintenanceColors[band] : UnknownColor;
                }

                case TrackOverlayMode.Cleanliness:
                {
                    float clearing = float.NaN;
                    try
                    {
                        var controller = TrackConditionController.Shared;
                        if (controller != null)
                        {
                            clearing = controller.GetRightOfWayClearing(segment);
                        }
                        else
                        {
                            clearing = segment.RightOfWayClearing;
                        }
                    }
                    catch
                    {
                        clearing = segment.RightOfWayClearing;
                    }

                    int band = UnitBand(clearing);
                    return (band >= 0 && band < MaintenanceColors.Length) ? MaintenanceColors[band] : UnknownColor;
                }

                case TrackOverlayMode.Routes:
                default:
                {
                    // Check passenger stops (shows purple ONLY if tracking enabled)
                    if (settings.EnablePassengerStopTracking && MapEnhancer._passengerStopSegments.Contains(segment.id))
                    {
                        return settings.TrackColorPax;
                    }

                    // Check mainline (Multi-Tier Mainline Detection Engine: Timetable, CTC, & Fallbacks)
                    if (MainlineDetectionEngine.IsMainline(segment))
                    {
                        return settings.TrackColorMainline;
                    }

                    // Check industrial (industry tracks override branch)
                    // BUT: Never show industrial if this is a passenger stop
                    if (MapEnhancer._industrialSegments.Contains(segment.id)
                        && !MainlineDetectionEngine.IsMainline(segment)
                        && !MapEnhancer._passengerStopSegments.Contains(segment.id))
                    {
                        if (settings.ShowMoWYardTracks
                            && MapEnhancer._mowYardSegments.Contains(segment.id)
                            && settings.MoWTrackColorMode == MoWTrackColorMode.SeparateMoWTrackColor)
                        {
                            return settings.MoWTrackColor;
                        }

                        if (settings.EnableIndustryAreaColors
                            && MapEnhancer._industrialSegmentColors.TryGetValue(segment.id, out Color segmentColor))
                        {
                            return segmentColor;
                        }
                        return settings.TrackColorIndustrial;
                    }

                    // Default branch line
                    return settings.TrackColorBranch;
                }
            }
        }
    }
}
