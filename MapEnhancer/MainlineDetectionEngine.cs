using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MapEnhancer.UMM;
using Model.Ops;
using Model.Ops.Timetable;
using Track;
using Track.Search;
using Track.Signals;
using UnityEngine;

namespace MapEnhancer
{
    /// <summary>
    /// Explicit provenance source indicating how a track segment was classified as Mainline.
    /// </summary>
    /// <summary>
    /// Explicit functional track category distinguishing operational route classification
    /// from facility ownership and track purpose (RRM-017 Follow-up 2).
    /// </summary>
    public enum TrackFunctionalCategory
    {
        Unclassified = 0,
        Mainline = 1,
        BranchLine = 2,
        Industry = 3,
        Interchange = 4,
        MOWYardTrack = 5,
        PassengerStop = 6,
        YardSwitchingTrack = 7
    }

    public enum MainlineClassificationSource
    {
        None = 0,
        TimetableCorridor = 1,
        CTCBlock = 2,
        VerifiedFallback = 3
    }

    /// <summary>
    /// Detailed classification record preserving the exact provenance and diagnostic rationale for a segment.
    /// </summary>
    public struct MainlineSegmentRecord
    {
        public string SegmentId;
        public TrackFunctionalCategory Category;
        public MainlineClassificationSource Source;
        public string Reason;

        public MainlineSegmentRecord(string segmentId, MainlineClassificationSource source, string reason)
            : this(segmentId, source == MainlineClassificationSource.None ? TrackFunctionalCategory.Unclassified : TrackFunctionalCategory.Mainline, source, reason)
        {
        }

        public MainlineSegmentRecord(string segmentId, TrackFunctionalCategory category, MainlineClassificationSource source, string reason)
        {
            SegmentId = segmentId ?? "";
            Category = category;
            Source = source;
            Reason = reason ?? "";
        }

        public override string ToString() => $"{SegmentId}: {Category} via {Source} ({Reason})";
    }

    /// <summary>
    /// Comprehensive detection result container exposing confirmed mainline tracks, candidate sets,
    /// yard/industry exclusions, and diagnostic metrics.
    /// </summary>
    public class MainlineDetectionResult
    {
        /// <summary>Set of segment IDs confirmed as Mainline tracks.</summary>
        public HashSet<string> ConfirmedMainlineSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of node/switch IDs associated with confirmed Mainline tracks.</summary>
        public HashSet<string> ConfirmedMainlineSwitchIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of candidate segment IDs derived from CTCBlock spans before exclusion filtering.</summary>
        public HashSet<string> CtcCandidateSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs directly confirmed as MOW Yard track.</summary>
        public HashSet<string> MowYardSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs confirmed as Interchange tracks.</summary>
        public HashSet<string> InterchangeSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs confirmed as Passenger Stop tracks.</summary>
        public HashSet<string> PassengerStopSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs with native Yard flag.</summary>
        public HashSet<string> YardSwitchingSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs in CTC spans excluded due to MOW Yard designation.</summary>
        public HashSet<string> ExcludedMowYardSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs in CTC spans excluded due to Industry track designation.</summary>
        public HashSet<string> ExcludedIndustrySegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Set of segment IDs scanned but not classified as Mainline.</summary>
        public HashSet<string> UnclassifiedSegmentIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Segment provenance dictionary recording the exact classification source and reason.</summary>
        public Dictionary<string, MainlineSegmentRecord> Provenance { get; } = new Dictionary<string, MainlineSegmentRecord>(StringComparer.Ordinal);

        // Telemetry & Diagnostic Metrics
        public int TimetableStationPairCount { get; set; }
        public int TimetableStationPairSuccessCount { get; set; }
        public int TimetableSegmentsFound { get; set; }
        public int CtcBlocksScanned { get; set; }
        public int CtcCandidatesFound { get; set; }
        public int CtcConfirmedFound { get; set; }
        public int FallbackSegmentsFound { get; set; }
        public bool UsedCustomMapFallback { get; set; }
        public string DiagnosticSummary { get; set; } = "";
    }

    /// <summary>
    /// Multi-Tier Mainline Detection Engine (RRM-017).
    /// Replaces naive CTCBlock detection with a 4-tier topology-aware classification pipeline:
    /// Tier 1: Timetable Main Corridor Pathfinding (AutoEngineer switch penalties)
    /// Tier 2: CTCBlock Spans Union (Signaled territory candidate extraction)
    /// Tier 3: Yard, MOW &amp; Industry Exclusion Filtering
    /// Tier 4: Custom-Map Fallback (Conservative switch priority &amp; tangent traversal)
    /// </summary>
    public static class MainlineDetectionEngine
    {
        private static MainlineDetectionResult _currentResult = new MainlineDetectionResult();
        private static readonly object _lock = new object();
        private static bool _isInitialized = false;
        private static bool _isExecuting = false;

        public static MainlineDetectionResult CurrentResult
        {
            get
            {
                lock (_lock)
                {
                    if (!_isInitialized && !_isExecuting)
                    {
                        ExecuteDetection();
                    }
                    return _currentResult;
                }
            }
        }

        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// Invalidates cached detection results, forcing recalculation on next query.
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Checks whether a given track segment ID is classified as confirmed Mainline.
        /// </summary>
        public static bool IsMainline(string? segmentId)
        {
            if (string.IsNullOrEmpty(segmentId)) return false;
            return CurrentResult.ConfirmedMainlineSegmentIds.Contains(segmentId!);
        }

        /// <summary>
        /// Checks whether a given track segment is classified as confirmed Mainline.
        /// </summary>
        public static bool IsMainline(TrackSegment? segment)
        {
            if (segment == null || string.IsNullOrEmpty(segment.id)) return false;
            return CurrentResult.ConfirmedMainlineSegmentIds.Contains(segment.id);
        }

        /// <summary>
        /// Checks whether a switch/node ID belongs to confirmed Mainline trackage.
        /// </summary>
        public static bool IsMainlineSwitch(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            return CurrentResult.ConfirmedMainlineSwitchIds.Contains(nodeId!);
        }

        /// <summary>
        /// Retrieves the classification source provenance for a segment.
        /// </summary>
        public static MainlineClassificationSource GetClassificationSource(string? segmentId)
        {
            if (string.IsNullOrEmpty(segmentId)) return MainlineClassificationSource.None;
            if (CurrentResult.Provenance.TryGetValue(segmentId!, out var record))
            {
                return record.Source;
            }
            return MainlineClassificationSource.None;
        }

        /// <summary>
        /// Retrieves the human-readable classification reason for a segment.
        /// </summary>
        public static string GetClassificationReason(string? segmentId)
        {
            if (string.IsNullOrEmpty(segmentId)) return "Unknown segment";
            if (CurrentResult.Provenance.TryGetValue(segmentId!, out var record))
            {
                return record.Reason;
            }
            return "Unclassified";
        }

        /// <summary>
        /// Executes the multi-tier mainline detection pipeline.
        /// Thread-safe and caches the result.
        /// </summary>
        public static MainlineDetectionResult ExecuteDetection()
        {
            lock (_lock)
            {
                if (_isExecuting)
                {
                    return _currentResult;
                }

                _isExecuting = true;
                var result = new MainlineDetectionResult();
                _currentResult = result;

                try
                {
                    var graph = Graph.Shared;
                    if (graph == null)
                    {
                        result.DiagnosticSummary = "Graph.Shared is null; detection aborted.";
                        _currentResult = result;
                        _isInitialized = true;
                        return result;
                    }

                    // =========================================================================
                    // TIER 1: Timetable Main Corridor Pathfinding
                    // =========================================================================
                    ExecuteTier1TimetableCorridor(graph, result);

                    // Copy known facility and purpose sets
                    if (MapEnhancer._mowYardSegments != null)
                    {
                        result.MowYardSegmentIds.UnionWith(MapEnhancer._mowYardSegments);
                    }
                    if (MapEnhancer._interchangeSegments != null)
                    {
                        result.InterchangeSegmentIds.UnionWith(MapEnhancer._interchangeSegments);
                    }
                    if (MapEnhancer._passengerStopSegments != null)
                    {
                        result.PassengerStopSegmentIds.UnionWith(MapEnhancer._passengerStopSegments);
                    }
                    if (graph.Segments != null)
                    {
                        foreach (var seg in graph.Segments)
                        {
                            if (seg != null && (seg.flags & TrackSegment.Flags.Yard) != 0)
                            {
                                result.YardSwitchingSegmentIds.Add(seg.id);
                            }
                        }
                    }

                    // =========================================================================
                    // TIER 2: CTCBlock Spans Candidate Collection
                    // =========================================================================
                    ExecuteTier2CtcCandidates(graph, result);

                    // =========================================================================
                    // TIER 3: Yard, MOW & Industry Exclusion Filtering
                    // =========================================================================
                    ExecuteTier3ExclusionFiltering(graph, result);

                    // =========================================================================
                    // TIER 4: Custom-Map Fallback (if Tier 1 produced zero segments)
                    // =========================================================================
                    if (result.ConfirmedMainlineSegmentIds.Count == 0)
                    {
                        ExecuteTier4CustomMapFallback(graph, result);
                    }

                    // Populate unclassified segments
                    if (graph.Segments != null)
                    {
                        foreach (var seg in graph.Segments)
                        {
                            if (seg != null && !result.ConfirmedMainlineSegmentIds.Contains(seg.id))
                            {
                                result.UnclassifiedSegmentIds.Add(seg.id);
                                if (!result.Provenance.ContainsKey(seg.id))
                                {
                                    var cat = GetTrackCategory(seg.id, result, seg);
                                    result.Provenance[seg.id] = new MainlineSegmentRecord(seg.id, cat, MainlineClassificationSource.None, "Non-mainline route / siding / facility track");
                                }
                            }
                        }
                    }

                    // Build diagnostic summary string
                    var sb = new StringBuilder();
                    sb.Append("Mainline Detection complete. ");
                    sb.Append($"Confirmed: {result.ConfirmedMainlineSegmentIds.Count} segments, {result.ConfirmedMainlineSwitchIds.Count} switches. ");
                    sb.Append($"Timetable: {result.TimetableSegmentsFound} segments ({result.TimetableStationPairSuccessCount}/{result.TimetableStationPairCount} station pairs). ");
                    sb.Append($"CTC: {result.CtcConfirmedFound} confirmed, {result.ExcludedMowYardSegmentIds.Count} MOW excluded, {result.ExcludedIndustrySegmentIds.Count} Industry excluded. ");
                    if (result.UsedCustomMapFallback)
                    {
                        sb.Append($"Fallback: {result.FallbackSegmentsFound} segments. ");
                    }
                    result.DiagnosticSummary = sb.ToString();

                    Loader.Log($"[ME][MainlineEngine] {result.DiagnosticSummary}");
                }
                catch (Exception ex)
                {
                    Loader.Log($"[ME][MainlineEngine] Error executing mainline detection: {ex}");
                    result.DiagnosticSummary = $"Error during detection: {ex.Message}";
                }
                finally
                {
                    _isExecuting = false;
                    _isInitialized = true;
                }

                _currentResult = result;
                return result;
            }
        }

        // =============================================================================
        // TIER 1 IMPLEMENTATION: Timetable Main Corridor
        // =============================================================================
        private static void ExecuteTier1TimetableCorridor(Graph graph, MainlineDetectionResult result)
        {
            var timetableController = TimetableController.Shared;
            if (timetableController == null || timetableController.branches == null || timetableController.branches.Count == 0)
            {
                Loader.LogDebug("[ME][MainlineEngine] Tier 1: TimetableController or branches unavailable.");
                return;
            }

            // Look for branch named "Main", or fallback to branch containing "main"
            TimetableBranch? mainBranch = timetableController.branches.FirstOrDefault(b =>
                b != null && string.Equals(b.name, "Main", StringComparison.OrdinalIgnoreCase));

            if (mainBranch == null)
            {
                mainBranch = timetableController.branches.FirstOrDefault(b =>
                    b != null && b.name != null && b.name.IndexOf("main", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (mainBranch == null || mainBranch.stations == null || mainBranch.stations.Count < 2)
            {
                Loader.LogDebug("[ME][MainlineEngine] Tier 1: No 'Main' timetable branch with >= 2 stations found.");
                return;
            }

            // Collect stations that can resolve to a valid track graph Location
            var resolvedStations = new List<(TimetableStation station, Location location)>();
            foreach (var st in mainBranch.stations)
            {
                if (st == null) continue;
                if (TryResolveStationLocation(st, out var loc))
                {
                    resolvedStations.Add((st, loc));
                }
            }

            if (resolvedStations.Count < 2)
            {
                Loader.LogDebug($"[ME][MainlineEngine] Tier 1: Only {resolvedStations.Count} stations resolved to track locations; minimum 2 required.");
                return;
            }

            result.TimetableStationPairCount = resolvedStations.Count - 1;

            // Route search with topology costs: Diverging routes are penalized (+50m) so through tangents
            // are selected over siding chords. ThrowSwitch and CTC lock penalties are 0 because dynamic switch
            // positions or CTC locks in-game do not change physical mainline geometry.
            var costs = new HeuristicCosts
            {
                DivergingRoute = 50,
                ThrowSwitch = 0,
                ThrowSwitchCTCLocked = 0,
                CarBlockingRoute = 0
            };

            for (int i = 0; i < resolvedStations.Count - 1; i++)
            {
                var stA = resolvedStations[i];
                var stB = resolvedStations[i + 1];

                var route = FindCorridorRoute(graph, stA.location, stB.location, costs);
                if (route == null || route.Count == 0)
                {
                    Loader.LogDebug($"[ME][MainlineEngine] Tier 1: Route search failed between {stA.station.DisplayName} and {stB.station.DisplayName}.");
                    continue;
                }

                result.TimetableStationPairSuccessCount++;
                string reason = $"Timetable Branch '{mainBranch.name}' between {stA.station.DisplayName} and {stB.station.DisplayName}";

                foreach (var seg in route)
                {
                    if (seg == null || string.IsNullOrEmpty(seg.id)) continue;
                    AddConfirmedMainline(result, seg, MainlineClassificationSource.TimetableCorridor, reason);
                    result.TimetableSegmentsFound++;
                }
            }

            Loader.LogDebug($"[ME][MainlineEngine] Tier 1 Timetable: Resolved {result.TimetableSegmentsFound} segments across {result.TimetableStationPairSuccessCount}/{result.TimetableStationPairCount} station pairs.");
        }

        private static bool TryResolveStationLocation(TimetableStation station, out Location location)
        {
            location = default;
            if (station == null) return false;

            // 1. Direct passengerStop property
            PassengerStop? stop = station.passengerStop;

            // 2. Lookup by timetableCode
            if (stop == null && !string.IsNullOrEmpty(station.code) && TimetableController.Shared != null)
            {
                TimetableController.Shared.TryGetPassengerStop(station.code, out stop);
            }

            // 3. Lookup by passengerStopId
            if (stop == null && !string.IsNullOrEmpty(station.passengerStopId))
            {
                stop = PassengerStop.GetPassengerStopForId(station.passengerStopId);
            }

            if (stop != null && stop.Spans != null)
            {
                foreach (var span in stop.Spans)
                {
                    if (span != null && span.lower.HasValue && span.lower.Value.segment != null)
                    {
                        location = span.lower.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<TrackSegment> FindCorridorRoute(IGraphView graph, Location start, Location end, HeuristicCosts costs)
        {
            var segmentsOut = new List<TrackSegment>();

            // Try standard search
            var steps = RouteSearch.FindRoute(graph, start, end, costs, checkForCars: false, trainLength: 0f, trainMomentum: 0f, maxIterations: 20000);

            // If no steps returned, try flipped starting orientation
            if ((steps == null || steps.Count == 0) && start.segment != null)
            {
                steps = RouteSearch.FindRoute(graph, start.Flipped(), end, costs, checkForCars: false, trainLength: 0f, trainMomentum: 0f, maxIterations: 20000);
            }

            // If still no steps, try searching from end to start
            if ((steps == null || steps.Count == 0) && end.segment != null)
            {
                steps = RouteSearch.FindRoute(graph, end, start, costs, checkForCars: false, trainLength: 0f, trainMomentum: 0f, maxIterations: 20000);
            }

            if (steps == null || steps.Count == 0) return segmentsOut;

            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Location.segment != null)
                {
                    segmentsOut.Add(steps[i].Location.segment);
                }

                if (i < steps.Count - 1)
                {
                    if (steps[i].Node != null && steps[i + 1].Node != null)
                    {
                        var commonSeg = graph.SegmentCommonToNodes(steps[i].Node, steps[i + 1].Node);
                        if (commonSeg != null)
                        {
                            segmentsOut.Add(commonSeg);
                        }
                    }
                    else if (steps[i].Location.segment != null)
                    {
                        segmentsOut.Add(steps[i].Location.segment);
                    }
                    else if (steps[i + 1].Location.segment != null)
                    {
                        segmentsOut.Add(steps[i + 1].Location.segment);
                    }
                }
            }

            return segmentsOut.Distinct().ToList();
        }

        // =============================================================================
        // TIER 2 IMPLEMENTATION: CTCBlock Candidates
        // =============================================================================
        private static void ExecuteTier2CtcCandidates(Graph graph, MainlineDetectionResult result)
        {
            var ctcBlocks = UnityEngine.Object.FindObjectsOfType<CTCBlock>(true);
            if (ctcBlocks == null || ctcBlocks.Length == 0)
            {
                Loader.LogDebug("[ME][MainlineEngine] Tier 2: No CTCBlock instances found in scene.");
                return;
            }

            result.CtcBlocksScanned = ctcBlocks.Length;

            // Route search with topology costs: Diverging routes are penalized (+50m) so through tangents
            // are selected over siding chords. ThrowSwitch and CTC lock penalties are 0 because dynamic switch
            // positions or CTC locks in-game do not change physical mainline geometry.
            var topologyCosts = new HeuristicCosts
            {
                DivergingRoute = 50,
                ThrowSwitch = 0,
                ThrowSwitchCTCLocked = 0,
                CarBlockingRoute = 0
            };

            foreach (var block in ctcBlocks)
            {
                if (block == null || block.Spans == null) continue;
                foreach (var span in block.Spans)
                {
                    if (span == null) continue;
                    try
                    {
                        bool resolvedByRoute = false;
                        if (graph != null && span.lower.HasValue && span.upper.HasValue &&
                            span.lower.Value.segment != null && span.upper.Value.segment != null)
                        {
                            var segments = FindCorridorRoute(graph, span.lower.Value, span.upper.Value, topologyCosts);
                            if (segments != null && segments.Count > 0)
                            {
                                foreach (var seg in segments)
                                {
                                    if (seg == null || string.IsNullOrEmpty(seg.id)) continue;
                                    result.CtcCandidateSegmentIds.Add(seg.id);
                                }
                                resolvedByRoute = true;
                            }
                        }

                        if (!resolvedByRoute)
                        {
                            span.UpdateCachedPointsIfNeeded();
                            if (span._cachedSegments != null)
                            {
                                foreach (var seg in span._cachedSegments)
                                {
                                    if (seg == null || string.IsNullOrEmpty(seg.id)) continue;
                                    result.CtcCandidateSegmentIds.Add(seg.id);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Loader.LogDebug($"[ME][MainlineEngine] Tier 2 span update error for block {block.id}: {ex.Message}");
                    }
                }
            }

            result.CtcCandidatesFound = result.CtcCandidateSegmentIds.Count;
            Loader.LogDebug($"[ME][MainlineEngine] Tier 2 CTC: Scanned {result.CtcBlocksScanned} blocks, identified {result.CtcCandidatesFound} candidate segments.");
        }

        // =============================================================================
        // TIER 3 IMPLEMENTATION: Yard, MOW & Industry Exclusion Filtering
        // =============================================================================
        private static void ExecuteTier3ExclusionFiltering(Graph graph, MainlineDetectionResult result)
        {
            var mowYardSegments = MapEnhancer._mowYardSegments;
            var interchangeSegments = MapEnhancer._interchangeSegments;
            var industrialSegments = MapEnhancer._industrialSegments;

            // Precedence Rule 1: Confirmed MOW yard tracks, native yard tracks, and non-passenger
            // industrial/interchange tracks must NEVER be Mainline, even if a route-search heuristic traversed them.
            var toRemove = new List<string>();
            foreach (var confirmedId in result.ConfirmedMainlineSegmentIds)
            {
                var seg = graph.GetSegment(confirmedId);
                if (mowYardSegments != null && mowYardSegments.Contains(confirmedId))
                {
                    toRemove.Add(confirmedId);
                    result.ExcludedMowYardSegmentIds.Add(confirmedId);
                    result.Provenance[confirmedId] = new MainlineSegmentRecord(confirmedId, TrackFunctionalCategory.MOWYardTrack, MainlineClassificationSource.None, "Excluded MOW Yard track");
                }
                else if (seg != null && (seg.flags & TrackSegment.Flags.Yard) != 0)
                {
                    toRemove.Add(confirmedId);
                    result.ExcludedMowYardSegmentIds.Add(confirmedId);
                    result.Provenance[confirmedId] = new MainlineSegmentRecord(confirmedId, TrackFunctionalCategory.YardSwitchingTrack, MainlineClassificationSource.None, "Excluded native Yard flag track");
                }
                else if (interchangeSegments != null && interchangeSegments.Contains(confirmedId))
                {
                    toRemove.Add(confirmedId);
                    result.ExcludedIndustrySegmentIds.Add(confirmedId);
                    result.Provenance[confirmedId] = new MainlineSegmentRecord(confirmedId, TrackFunctionalCategory.Interchange, MainlineClassificationSource.None, "Excluded Interchange track");
                }
                else if (industrialSegments != null && industrialSegments.Contains(confirmedId)
                         && (MapEnhancer._passengerStopSegments == null || !MapEnhancer._passengerStopSegments.Contains(confirmedId)))
                {
                    toRemove.Add(confirmedId);
                    result.ExcludedIndustrySegmentIds.Add(confirmedId);
                    result.Provenance[confirmedId] = new MainlineSegmentRecord(confirmedId, TrackFunctionalCategory.Industry, MainlineClassificationSource.None, "Excluded industrial track");
                }
            }

            foreach (var removeId in toRemove)
            {
                result.ConfirmedMainlineSegmentIds.Remove(removeId);
            }

            // Precedence Rule 2: Process CTC candidates
            // No BFS network flooding: MOW membership requires verified industry span membership.
            // Siding chords diverging from confirmed mainline tangents are kept as unclassified/branch sidings.
            foreach (var candidateId in result.CtcCandidateSegmentIds)
            {
                if (result.ConfirmedMainlineSegmentIds.Contains(candidateId))
                {
                    continue;
                }

                var seg = graph.GetSegment(candidateId);

                // Exclude verified MOW yard tracks (e.g. East Whittier Stky03ew3, Sluu; Bryson mow spans)
                if (mowYardSegments != null && mowYardSegments.Contains(candidateId))
                {
                    result.ExcludedMowYardSegmentIds.Add(candidateId);
                    result.Provenance[candidateId] = new MainlineSegmentRecord(candidateId, TrackFunctionalCategory.MOWYardTrack, MainlineClassificationSource.None, "Excluded MOW Yard track in CTC span");
                    continue;
                }

                // Exclude verified Interchange tracks
                if (interchangeSegments != null && interchangeSegments.Contains(candidateId))
                {
                    result.ExcludedIndustrySegmentIds.Add(candidateId);
                    result.Provenance[candidateId] = new MainlineSegmentRecord(candidateId, TrackFunctionalCategory.Interchange, MainlineClassificationSource.None, "Excluded Interchange track in CTC span");
                    continue;
                }

                // Exclude Industry siding tracks
                if (industrialSegments != null && industrialSegments.Contains(candidateId))
                {
                    result.ExcludedIndustrySegmentIds.Add(candidateId);
                    result.Provenance[candidateId] = new MainlineSegmentRecord(candidateId, TrackFunctionalCategory.Industry, MainlineClassificationSource.None, "Excluded Industry siding in CTC span");
                    continue;
                }

                // Exclude native Yard flag tracks
                if (seg != null && (seg.flags & TrackSegment.Flags.Yard) != 0)
                {
                    result.ExcludedMowYardSegmentIds.Add(candidateId);
                    result.Provenance[candidateId] = new MainlineSegmentRecord(candidateId, TrackFunctionalCategory.YardSwitchingTrack, MainlineClassificationSource.None, "Excluded native Yard flag track in CTC span");
                    continue;
                }

                // Topology-aware Siding Turnout Check:
                // If this candidate connects at a switch to a confirmed Mainline segment, check if it is
                // a diverging leg (priority < 0 or speed lower than confirmed through-leg).
                // A diverging siding turnout at a switch where through-mainline continues is never Mainline.
                if (seg != null)
                {
                    bool isDivergingSiding = false;
                    TrackNode[] endpoints = { seg.a, seg.b };
                    foreach (var node in endpoints)
                    {
                        if (node == null) continue;
                        var connected = graph.SegmentsConnectedTo(node);
                        if (connected == null || connected.Count <= 1) continue;

                        TrackSegment? confirmedMainLeg = null;
                        foreach (var conn in connected)
                        {
                            if (conn != null && conn.id != seg.id && result.ConfirmedMainlineSegmentIds.Contains(conn.id))
                            {
                                confirmedMainLeg = conn;
                                break;
                            }
                        }

                        if (confirmedMainLeg != null)
                        {
                            if (seg.priority < 0 || (confirmedMainLeg.priority >= 0 && seg.priority < confirmedMainLeg.priority) || seg.speedLimit < confirmedMainLeg.speedLimit)
                            {
                                isDivergingSiding = true;
                                break;
                            }
                        }
                    }

                    if (isDivergingSiding)
                    {
                        result.UnclassifiedSegmentIds.Add(candidateId);
                        result.Provenance[candidateId] = new MainlineSegmentRecord(candidateId, TrackFunctionalCategory.Unclassified, MainlineClassificationSource.None, "Diverging siding / switch turnout from confirmed mainline");
                        continue;
                    }
                }

                // Uncontested CTC trackage promotes to confirmed Mainline
                if (seg != null)
                {
                    AddConfirmedMainline(result, seg, MainlineClassificationSource.CTCBlock, "Signaled CTC corridor span");
                    result.CtcConfirmedFound++;
                }
            }

            Loader.LogDebug($"[ME][MainlineEngine] Tier 3 Exclusion Filtering complete. Confirmed: {result.ConfirmedMainlineSegmentIds.Count}, MOW Excluded: {result.ExcludedMowYardSegmentIds.Count}, Industry Excluded: {result.ExcludedIndustrySegmentIds.Count}");
        }

        // TIER 4 IMPLEMENTATION: Custom-Map Fallback
        // =============================================================================
        private static void ExecuteTier4CustomMapFallback(Graph graph, MainlineDetectionResult result)
        {
            Loader.LogDebug("[ME][MainlineEngine] Tier 4: Initiating custom-map fallback traversal.");
            result.UsedCustomMapFallback = true;

            var mowYardSegments = MapEnhancer._mowYardSegments;
            var industrialSegments = MapEnhancer._industrialSegments;

            // Strategy A: Check if any other timetable branch exists with >= 2 stations
            var timetableController = TimetableController.Shared;
            if (timetableController != null && timetableController.branches != null)
            {
                var candidateBranch = timetableController.branches
                    .Where(b => b != null && b.stations != null && b.stations.Count >= 2)
                    .OrderByDescending(b => b.stations.Count)
                    .FirstOrDefault();

                if (candidateBranch != null)
                {
                    var resolvedStations = new List<(TimetableStation station, Location location)>();
                    foreach (var st in candidateBranch.stations)
                    {
                        if (st != null && TryResolveStationLocation(st, out var loc))
                        {
                            resolvedStations.Add((st, loc));
                        }
                    }

                    if (resolvedStations.Count >= 2)
                    {
                        var costs = HeuristicCosts.AutoEngineer;
                        for (int i = 0; i < resolvedStations.Count - 1; i++)
                        {
                            var route = FindCorridorRoute(graph, resolvedStations[i].location, resolvedStations[i + 1].location, costs);
                            if (route != null && route.Count > 0)
                            {
                                string reason = $"Custom map timetable branch '{candidateBranch.name}'";
                                foreach (var seg in route)
                                {
                                    if (seg == null) continue;
                                    AddConfirmedMainline(result, seg, MainlineClassificationSource.VerifiedFallback, reason);
                                    result.FallbackSegmentsFound++;
                                }
                            }
                        }

                        if (result.FallbackSegmentsFound > 0)
                        {
                            Loader.LogDebug($"[ME][MainlineEngine] Tier 4: Recovered {result.FallbackSegmentsFound} segments via custom timetable branch '{candidateBranch.name}'.");
                            return;
                        }
                    }
                }
            }

            // Strategy B: Conservative switch-normal priority through-route traversal
            // Scan for coherent through-corridors of >= 5 connected segments with priority >= 0,
            // strictly rejecting Yard flags, MOW tracks, and Industries.
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<List<TrackSegment>>();

            if (graph.Segments != null)
            {
                foreach (var seg in graph.Segments)
                {
                    if (seg == null || visited.Contains(seg.id)) continue;
                    if (seg.priority < 0) continue;
                    if ((seg.flags & TrackSegment.Flags.Yard) != 0) continue;
                    if (mowYardSegments != null && mowYardSegments.Contains(seg.id)) continue;
                    if (industrialSegments != null && industrialSegments.Contains(seg.id)) continue;

                    // Trace contiguous tangent chain
                    var chain = new List<TrackSegment> { seg };
                    visited.Add(seg.id);

                    // Expand forward from B
                    ExpandTangentChain(graph, seg, seg.b, chain, visited, mowYardSegments, industrialSegments);
                    // Expand backward from A
                    ExpandTangentChain(graph, seg, seg.a, chain, visited, mowYardSegments, industrialSegments);

                    if (chain.Count >= 5)
                    {
                        candidates.Add(chain);
                    }
                }
            }

            // Promote verified coherent chains
            foreach (var chain in candidates)
            {
                foreach (var seg in chain)
                {
                    if (seg == null) continue;
                    AddConfirmedMainline(result, seg, MainlineClassificationSource.VerifiedFallback, "Custom map verified normal-priority through corridor");
                    result.FallbackSegmentsFound++;
                }
            }

            Loader.LogDebug($"[ME][MainlineEngine] Tier 4: Fallback completed. Identified {result.FallbackSegmentsFound} segments across {candidates.Count} through-chains.");
        }

        private static void ExpandTangentChain(Graph graph, TrackSegment prevSeg, TrackNode fromNode, List<TrackSegment> chain, HashSet<string> visited, HashSet<string>? mowYard, HashSet<string>? industrial)
        {
            if (fromNode == null) return;
            var connected = graph.SegmentsConnectedTo(fromNode);
            if (connected == null || connected.Count <= 1) return;

            // Look for non-diverging normal leg (priority >= 0), highest speed limit
            TrackSegment? bestNext = null;
            int bestPriority = -1;
            int bestSpeed = -1;

            foreach (var nextSeg in connected)
            {
                if (nextSeg == null || nextSeg.id == prevSeg.id || visited.Contains(nextSeg.id)) continue;
                if (nextSeg.priority < 0) continue;
                if ((nextSeg.flags & TrackSegment.Flags.Yard) != 0) continue;
                if (mowYard != null && mowYard.Contains(nextSeg.id)) continue;
                if (industrial != null && industrial.Contains(nextSeg.id)) continue;

                int pri = nextSeg.priority;
                int spd = nextSeg.speedLimit;

                if (pri > bestPriority || (pri == bestPriority && spd > bestSpeed))
                {
                    bestPriority = pri;
                    bestSpeed = spd;
                    bestNext = nextSeg;
                }
            }

            if (bestNext != null)
            {
                chain.Add(bestNext);
                visited.Add(bestNext.id);
                var nextNode = (bestNext.a == fromNode) ? bestNext.b : bestNext.a;
                ExpandTangentChain(graph, bestNext, nextNode, chain, visited, mowYard, industrial);
            }
        }

        private static void AddConfirmedMainline(MainlineDetectionResult result, TrackSegment seg, MainlineClassificationSource source, string reason)
        {
            if (seg == null || string.IsNullOrEmpty(seg.id)) return;

            result.ConfirmedMainlineSegmentIds.Add(seg.id);
            if (seg.a != null && !string.IsNullOrEmpty(seg.a.id))
            {
                result.ConfirmedMainlineSwitchIds.Add(seg.a.id);
            }
            if (seg.b != null && !string.IsNullOrEmpty(seg.b.id))
            {
                result.ConfirmedMainlineSwitchIds.Add(seg.b.id);
            }

            // Preserve higher-priority source provenance (Timetable > CTC > Fallback)
            if (!result.Provenance.TryGetValue(seg.id, out var existing) || source < existing.Source)
            {
                result.Provenance[seg.id] = new MainlineSegmentRecord(seg.id, TrackFunctionalCategory.Mainline, source, reason);
            }
        }

        /// <summary>
        /// Resolves the explicit functional category for a given track segment.
        /// Deterministic precedence:
        /// 1. PassengerStop (if in passenger stop tracking set)
        /// 2. Mainline (if confirmed by detection engine)
        /// 3. MOWYardTrack (if in verified MOW facility spans)
        /// 4. Interchange (if in verified interchange spans)
        /// 5. Industry (if in verified industry spans)
        /// 6. YardSwitchingTrack (if native Yard flag)
        /// 7. Unclassified (siding / non-mainline route)
        /// </summary>
        public static TrackFunctionalCategory GetTrackCategory(TrackSegment? segment)
        {
            if (segment == null || string.IsNullOrEmpty(segment.id)) return TrackFunctionalCategory.Unclassified;
            return GetTrackCategory(segment.id, _currentResult, segment);
        }

        public static TrackFunctionalCategory GetTrackCategory(string segmentId, TrackSegment? segment = null)
        {
            return GetTrackCategory(segmentId, _currentResult, segment);
        }

        public static TrackFunctionalCategory GetTrackCategory(string segmentId, MainlineDetectionResult result, TrackSegment? segment = null)
        {
            if (string.IsNullOrEmpty(segmentId)) return TrackFunctionalCategory.Unclassified;

            if (result != null && result.PassengerStopSegmentIds != null && result.PassengerStopSegmentIds.Contains(segmentId))
            {
                return TrackFunctionalCategory.PassengerStop;
            }

            if (MapEnhancer._passengerStopSegments != null && MapEnhancer._passengerStopSegments.Contains(segmentId))
            {
                return TrackFunctionalCategory.PassengerStop;
            }

            if (result != null && result.ConfirmedMainlineSegmentIds != null && result.ConfirmedMainlineSegmentIds.Contains(segmentId))
            {
                return TrackFunctionalCategory.Mainline;
            }

            if (result != null && result.MowYardSegmentIds != null && result.MowYardSegmentIds.Contains(segmentId))
            {
                return TrackFunctionalCategory.MOWYardTrack;
            }

            if (MapEnhancer._mowYardSegments != null && MapEnhancer._mowYardSegments.Contains(segmentId))
            {
                return TrackFunctionalCategory.MOWYardTrack;
            }

            if (result != null && result.InterchangeSegmentIds != null && result.InterchangeSegmentIds.Contains(segmentId))
            {
                return TrackFunctionalCategory.Interchange;
            }

            if (MapEnhancer._interchangeSegments != null && MapEnhancer._interchangeSegments.Contains(segmentId))
            {
                return TrackFunctionalCategory.Interchange;
            }

            if (MapEnhancer._industrialSegments != null && MapEnhancer._industrialSegments.Contains(segmentId))
            {
                return TrackFunctionalCategory.Industry;
            }

            if (result != null && result.YardSwitchingSegmentIds != null && result.YardSwitchingSegmentIds.Contains(segmentId))
            {
                return TrackFunctionalCategory.YardSwitchingTrack;
            }

            if (segment != null && (segment.flags & TrackSegment.Flags.Yard) != 0)
            {
                return TrackFunctionalCategory.YardSwitchingTrack;
            }

            if (Graph.Shared != null)
            {
                var seg = segment ?? Graph.Shared.GetSegment(segmentId);
                if (seg != null && (seg.flags & TrackSegment.Flags.Yard) != 0)
                {
                    return TrackFunctionalCategory.YardSwitchingTrack;
                }
            }

            return TrackFunctionalCategory.Unclassified;
        }
    }
}
