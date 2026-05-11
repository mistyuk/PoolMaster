// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PoolMaster.Editor
{
    public class PoolingDiagnosticsWindow : EditorWindow
    {
        [MenuItem("Window/PoolMaster/Diagnostics", priority = 300)]
        public static void Open() => GetWindow<PoolingDiagnosticsWindow>("Pool Diagnostics");

        private Vector2 scroll;
        private bool autoRefresh = true;
        private float refreshInterval = 0.5f;
        private double lastRefreshTime;
        private PoolSnapshot lastSnapshot;

        // Search and filtering
        private string searchFilter = "";
        private bool showActiveOnly = false;
        private int sortMode = 0; // 0=Name, 1=Active Desc, 2=Utilization Desc, 3=Expansions Desc
        private readonly string[] sortOptions =
        {
            "Name",
            "Active (High)",
            "Utilization (High)",
            "Expansions (High)",
        };

        // The filtered+sorted list is rebuilt only when the snapshot or any filter
        // input changes — *not* on every OnGUI repaint. Avoids running the sort
        // 60-144 times per second when nothing has changed.
        private readonly List<KeyValuePair<string, PoolMetrics>> poolListCache =
            new List<KeyValuePair<string, PoolMetrics>>();
        private string _cachedFilter;
        private bool _cachedShowActiveOnly;
        private int _cachedSortMode = -1;
        private int _cachedSnapshotPoolCount = -1;

        // Cached GUIStyles — avoids per-frame allocation
        private GUIStyle _richBoldLabel;
        private GUIStyle RichBoldLabel
        {
            get
            {
                if (_richBoldLabel == null)
                {
                    _richBoldLabel = new GUIStyle(EditorStyles.boldLabel) { richText = true };
                }
                return _richBoldLabel;
            }
        }

        // Cached static GUIContent — never changes after construction. Building these
        // once vs every OnGUI saves ~30 allocations per repaint when the window is open.
        private static readonly GUIContent LabelAutoRefresh = new GUIContent("Auto Refresh", "Automatically refresh pool data");
        private static readonly GUIContent LabelInterval = new GUIContent("Interval", "Refresh interval in seconds");
        private static readonly GUIContent LabelSearch = new GUIContent("Search", "Filter pools by name");
        private static readonly GUIContent LabelActiveOnly = new GUIContent("Active Only", "Show only pools with active objects");
        private static readonly GUIContent LabelProfilerBtn = new GUIContent("Unity Profiler", "Open Profiler for accurate memory analysis");
        private static readonly GUIContent LabelClearAll = new GUIContent("Clear All Inactive", "Destroy all inactive objects across every pool");
        private static readonly GUIContent LabelCullUnused = new GUIContent("Cull Unused (60s)", "Destroy pools with no activity for 60 seconds");
        private static readonly GUIContent LabelClearInactive = new GUIContent("Clear Inactive", "Destroy all inactive objects in this pool");
        private static readonly GUIContent LabelShrinkTo4 = new GUIContent("Shrink to 4", "Trim inactive objects down to 4");
        private static readonly GUIContent LabelReseed = new GUIContent(
            "Reseed",
            "Force-despawn every active instance, destroy all inactive instances, then re-prewarm. " +
            "Use after editing the prefab at runtime so existing pooled instances get replaced with fresh clones.");
        private static readonly GUIContent LabelDestroy = new GUIContent("Destroy", "Destroy entire pool (active + inactive)");
        private static readonly GUIContent LabelInfoIcon = new GUIContent("ⓘ", "Shows how 'hot' this pool is - higher means more objects are actively in use");

        void OnGUI()
        {
            // Header controls
            EditorGUILayout.BeginHorizontal();
            autoRefresh = EditorGUILayout.Toggle(LabelAutoRefresh, autoRefresh);
            if (autoRefresh)
            {
                refreshInterval = EditorGUILayout.Slider(LabelInterval, refreshInterval, 0.1f, 2f);
            }
            if (GUILayout.Button("Refresh Now", GUILayout.Width(100)))
            {
                RefreshData();
            }
            EditorGUILayout.EndHorizontal();

            // Search and filter controls
            EditorGUILayout.BeginHorizontal();
            searchFilter = EditorGUILayout.TextField(LabelSearch, searchFilter);
            showActiveOnly = EditorGUILayout.Toggle(LabelActiveOnly, showActiveOnly, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Sort By:", GUILayout.Width(60));
            sortMode = EditorGUILayout.Popup(sortMode, sortOptions, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (Application.isPlaying && PoolingManager.Instance != null)
            {
                if (lastSnapshot.TotalPools > 0)
                {
                    DrawPoolingSummary();
                    EditorGUILayout.Space();
                    // Rebuild the filtered+sorted list only if any input changed since the
                    // last build — saves redundant sort work on every repaint.
                    EnsureFilteredListUpToDate();
                    DrawPoolDetails();
                }
                else
                {
                    EditorGUILayout.HelpBox("No active pools found.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view active pools.", MessageType.Info);
            }
        }

        private void RefreshData()
        {
            if (Application.isPlaying && PoolingManager.Instance != null)
            {
                lastSnapshot = PoolingManager.Instance.GetSnapshot();
                lastRefreshTime = EditorApplication.timeSinceStartup;
                // Force a filter-list rebuild on the next OnGUI even if user inputs
                // didn't change — the underlying snapshot did.
                _cachedSnapshotPoolCount = -1;
                Repaint();
            }
        }

        /// <summary>
        /// Rebuilds <see cref="poolListCache"/> only when one of (snapshot pool count,
        /// search filter, active-only toggle, sort mode) has changed. OnGUI fires at
        /// 60-144 Hz; the snapshot only updates ~2 Hz; user inputs change on user
        /// action. So we cache the filtered+sorted result and reuse it across paints.
        /// </summary>
        private void EnsureFilteredListUpToDate()
        {
            int currentCount = lastSnapshot.PoolBreakdown?.Count ?? 0;
            if (_cachedSnapshotPoolCount == currentCount
                && _cachedSortMode == sortMode
                && _cachedShowActiveOnly == showActiveOnly
                && string.Equals(_cachedFilter, searchFilter, StringComparison.Ordinal))
            {
                return;
            }

            _cachedSnapshotPoolCount = currentCount;
            _cachedSortMode = sortMode;
            _cachedShowActiveOnly = showActiveOnly;
            _cachedFilter = searchFilter;

            BuildFilteredSortedList();
        }

        private void BuildFilteredSortedList()
        {
            poolListCache.Clear();
            if (lastSnapshot.PoolBreakdown == null)
                return;

            foreach (var kv in lastSnapshot.PoolBreakdown)
            {
                if (!string.IsNullOrEmpty(searchFilter)
                    && kv.Key.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (showActiveOnly && kv.Value.CurrentActive == 0)
                    continue;

                poolListCache.Add(kv);
            }

            switch (sortMode)
            {
                case 1: poolListCache.Sort(SortByActiveDesc); break;
                case 2: poolListCache.Sort(SortByUtilizationDesc); break;
                case 3: poolListCache.Sort(SortByExpansionsDesc); break;
                default: poolListCache.Sort(SortByName); break;
            }
        }

        // Static delegate fields so the sort comparisons don't allocate per call.
        // C# may cache non-capturing lambdas as statics automatically, but making
        // the caching explicit guarantees no allocation across all compiler versions.
        private static readonly Comparison<KeyValuePair<string, PoolMetrics>> SortByName =
            (a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        private static readonly Comparison<KeyValuePair<string, PoolMetrics>> SortByActiveDesc =
            (a, b) =>
            {
                int result = b.Value.CurrentActive.CompareTo(a.Value.CurrentActive);
                return result != 0 ? result : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            };
        private static readonly Comparison<KeyValuePair<string, PoolMetrics>> SortByUtilizationDesc =
            (a, b) =>
            {
                float utilA = a.Value.TotalCreated > 0 ? (float)a.Value.CurrentActive / a.Value.TotalCreated : 0f;
                float utilB = b.Value.TotalCreated > 0 ? (float)b.Value.CurrentActive / b.Value.TotalCreated : 0f;
                int result = utilB.CompareTo(utilA);
                return result != 0 ? result : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            };
        private static readonly Comparison<KeyValuePair<string, PoolMetrics>> SortByExpansionsDesc =
            (a, b) =>
            {
                int result = b.Value.ExpansionCount.CompareTo(a.Value.ExpansionCount);
                return result != 0 ? result : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            };

        private void DrawPoolingSummary()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Global Pool Statistics", EditorStyles.boldLabel);

            // Counts use thousand separators ({:N0}) so 10,000+ stays readable; widths
            // sized for 7-digit numbers (up to "1,000,000") so the Stress Test demo
            // doesn't visually clip.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Total Pools: {lastSnapshot.TotalPools:N0}",
                GUILayout.Width(120)
            );
            EditorGUILayout.LabelField(
                $"Active Objects: {lastSnapshot.TotalActiveObjects:N0}",
                GUILayout.Width(180)
            );
            EditorGUILayout.LabelField(
                $"Inactive Objects: {lastSnapshot.TotalInactiveObjects:N0}",
                GUILayout.Width(190)
            );
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent(
                    $"Total Objects: {lastSnapshot.TotalObjects:N0}",
                    "Total pooled instances across all pools"
                ),
                GUILayout.Width(180)
            );
            EditorGUILayout.LabelField(
                new GUIContent(
                    $"Utilization: {lastSnapshot.GlobalUtilization:F1}%",
                    "Percentage of objects currently active"
                ),
                GUILayout.Width(140)
            );
            if (GUILayout.Button(LabelProfilerBtn, GUILayout.Width(120)))
            {
                EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            }
            EditorGUILayout.EndHorizontal();

            // Global pool controls
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(LabelClearAll, GUILayout.Width(130)))
            {
                PoolingManager.Instance.ClearAllPools();
                RefreshData();
            }
            if (GUILayout.Button(LabelCullUnused, GUILayout.Width(130)))
            {
                int culled = PoolingManager.Instance.CullUnusedPools(60f);
                PoolLog.Info($"Diagnostics: Culled {culled} unused pools");
                RefreshData();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawPoolDetails()
        {
            EditorGUILayout.LabelField("Individual Pool Details", EditorStyles.boldLabel);

            if (poolListCache.Count == 0)
            {
                if (lastSnapshot.PoolBreakdown == null || lastSnapshot.PoolBreakdown.Count == 0)
                    EditorGUILayout.HelpBox("No active pools found.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("No pools match the current filter.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            foreach (var kv in poolListCache)
            {
                EditorGUILayout.BeginVertical("box");

                // Pool name header with colored indicator
                EditorGUILayout.BeginHorizontal();
                var indicatorColor =
                    kv.Value.CurrentActive > 0 ? "<color=#00FF00>●</color>" : "<color=#808080>○</color>";
                EditorGUILayout.LabelField(
                    $"{indicatorColor} {kv.Key}",
                    RichBoldLabel
                );
                EditorGUILayout.EndHorizontal();

                var m = kv.Value;

                // Core stats with tooltips. Numbers use {:N0} thousand separators and
                // widths are sized for 7-digit counts (e.g. "Spawned: 1,000,000") so
                // Stress Test demos don't visually clip the values.
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    new GUIContent($"Active: {m.CurrentActive:N0}", "Currently spawned objects"),
                    GUILayout.Width(120)
                );
                EditorGUILayout.LabelField(
                    new GUIContent(
                        $"Spawned: {m.TotalSpawned:N0}",
                        "Total times objects were spawned"
                    ),
                    GUILayout.Width(140)
                );
                EditorGUILayout.LabelField(
                    new GUIContent($"Created: {m.TotalCreated:N0}", "Total objects instantiated"),
                    GUILayout.Width(130)
                );
                EditorGUILayout.LabelField(
                    new GUIContent(
                        $"Reuse: {m.ReuseEfficiency:P0}",
                        "Percentage of spawns that reused existing objects"
                    ),
                    GUILayout.Width(90)
                );
                EditorGUILayout.EndHorizontal();

                // Performance stats
                if (m.ExpansionCount > 0 || m.CullCount > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        $"Expansions: {m.ExpansionCount:N0}",
                        GUILayout.Width(130)
                    );
                    EditorGUILayout.LabelField($"Culls: {m.CullCount:N0}", GUILayout.Width(100));

                    if (m.AverageExpansionInterval > 0)
                    {
                        EditorGUILayout.LabelField(
                            $"Avg Expand: {m.AverageExpansionInterval:F1}s",
                            GUILayout.Width(110)
                        );
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // Progress bar for pool utilization with tooltip icon
                if (m.TotalCreated > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    float utilization = (float)m.CurrentActive / m.TotalCreated;
                    var rect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(
                        rect,
                        utilization,
                        $"Utilization: {utilization:P0} (Active/Total Created)"
                    );
                    GUILayout.Label(LabelInfoIcon, GUILayout.Width(20));
                    EditorGUILayout.EndHorizontal();
                }

                // Pool management controls (play mode only)
                DrawPoolControls(kv.Key);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPoolControls(string poolId)
        {
            if (!Application.isPlaying || PoolingManager.Instance == null)
                return;

            // The snapshot might be one refresh-tick stale — the pool may have been
            // destroyed between the snapshot capture and now. GetPool returns null
            // in that case; bail gracefully instead of NRE'ing on ctrl.
            var pool = PoolingManager.Instance.GetPool(poolId);
            if (pool == null || !(pool is IPoolControl ctrl))
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(LabelClearInactive, GUILayout.Width(100)))
            {
                ctrl.Clear();
                RefreshData();
            }

            if (GUILayout.Button(LabelShrinkTo4, GUILayout.Width(80)))
            {
                ctrl.ShrinkInactive(4);
                RefreshData();
            }

            // Reseed and Destroy tint the button background. Save/restore via try/finally
            // so an exception in the user's confirmation dialog or pool callback doesn't
            // leave the GUI stuck in a tinted state.
            var prevBg = GUI.backgroundColor;
            try
            {
                GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
                if (GUILayout.Button(LabelReseed, GUILayout.Width(70)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Reseed Pool",
                        $"Reseed pool '{poolId}'?\n\n" +
                        "All active instances will be force-despawned, all inactive instances destroyed, " +
                        "and the pool re-prewarmed to its original initial size.\n\n" +
                        "Use this after editing the prefab at runtime.",
                        "Reseed", "Cancel"))
                    {
                        ctrl.Reseed(rePrewarm: true);
                        RefreshData();
                    }
                }

                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button(LabelDestroy, GUILayout.Width(70)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Destroy Pool",
                        $"Destroy pool '{poolId}' and all its objects?\nThis cannot be undone.",
                        "Destroy", "Cancel"))
                    {
                        ctrl.DestroyPool();
                        RefreshData();
                    }
                }
            }
            finally
            {
                GUI.backgroundColor = prevBg;
            }

            EditorGUILayout.EndHorizontal();
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Refresh immediately if opening window while already in play mode
            if (Application.isPlaying && PoolingManager.Instance != null)
            {
                RefreshData();
            }
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>
        /// When the editor exits Play Mode the PoolingManager singleton is destroyed
        /// along with all its pools. <see cref="lastSnapshot"/> still holds the
        /// pre-exit data — clearing it prevents one repaint of stale UI before the
        /// next OnEditorUpdate notices the manager is gone.
        /// </summary>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode
                || state == PlayModeStateChange.EnteredEditMode)
            {
                lastSnapshot = default;
                poolListCache.Clear();
                _cachedSnapshotPoolCount = -1;
                Repaint();
            }
        }

        void OnEditorUpdate()
        {
            // Throttled refresh check outside OnGUI
            if (autoRefresh && Application.isPlaying && PoolingManager.Instance != null)
            {
                if (EditorApplication.timeSinceStartup - lastRefreshTime > refreshInterval)
                {
                    RefreshData();
                }
            }
        }
    }
}
#endif
