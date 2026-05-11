// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace PoolMaster.NoCode
{
    /// <summary>
    /// The main PoolMaster system that manages all pools in your scene.
    /// Add this to a GameObject to get started with pooling.
    /// </summary>
    [AddComponentMenu("PoolMaster/PoolMaster Manager")]
    [DisallowMultipleComponent]
    public class PoolMasterManager : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip(
            "List of pools to create when the scene starts. Add pools here to pre-configure them."
        )]
        [SerializeField]
        private List<PoolDefinition> pools = new List<PoolDefinition>();

        [Header("Performance")]
        [Tooltip(
            "Create all pooled objects immediately when the scene starts (recommended for better performance)."
        )]
        [SerializeField]
        private bool prewarmOnStart = true;

        [Header("Debug")]
        [Tooltip("Show helpful warnings in the Console when issues are detected.")]
        [SerializeField]
        private bool showWarnings = true;

        [Tooltip("Show detailed information about pool operations in the Console.")]
        [SerializeField]
        private bool showDebugInfo = false;

        private Dictionary<GameObject, PoolDefinition> poolLookup =
            new Dictionary<GameObject, PoolDefinition>();
        private static PoolMasterManager instance;

        /// <summary>
        /// Gets the singleton instance of PoolMaster Manager.
        /// </summary>
        public static PoolMasterManager Instance
        {
            get
            {
                if (instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    // Unity 6: FindObjectOfType is deprecated
                    instance = Object.FindFirstObjectByType<PoolMasterManager>();
#else
                    instance = FindObjectOfType<PoolMasterManager>();
#endif
                    if (instance == null)
                    {
                        var go = new GameObject("PoolMaster Manager");
                        instance = go.AddComponent<PoolMasterManager>();
                    }
                }
                return instance;
            }
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                if (showWarnings)
                    Debug.LogWarning(
                        "[PoolMaster] Multiple PoolMaster Managers detected. Only one should exist."
                    );
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        void Start()
        {
            if (prewarmOnStart)
            {
                PrewarmAllPools();
            }
        }

        private void InitializePools()
        {
            poolLookup.Clear();

            foreach (var pool in pools)
            {
                if (pool != null && pool.Prefab != null)
                {
                    poolLookup[pool.Prefab] = pool;
                    pool.Initialize(transform, showDebugInfo);
                }
            }
        }

        private void PrewarmAllPools()
        {
            foreach (var pool in pools)
            {
                if (pool != null)
                {
                    pool.Prewarm();
                }
            }
        }

        /// <summary>
        /// Spawns an object from the pool at the specified position.
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (poolLookup.TryGetValue(prefab, out var pool))
            {
                return pool.Spawn(position, rotation);
            }

            if (showWarnings)
            {
                Debug.LogWarning(
                    $"[PoolMaster] No pool found for prefab '{prefab.name}'. Add it to the Pools list in PoolMaster Manager."
                );
            }

            return null;
        }

        /// <summary>
        /// Returns an object to its pool.
        /// </summary>
        public void ReturnToPool(GameObject instance)
        {
            if (instance == null)
                return;

            // Find which pool owns this object
            foreach (var pool in pools)
            {
                if (pool != null && pool.ContainsInstance(instance))
                {
                    pool.ReturnToPool(instance);
                    return;
                }
            }

            if (showWarnings)
            {
                Debug.LogWarning($"[PoolMaster] Could not find pool for object '{instance.name}'.");
            }
        }

        /// <summary>
        /// Gets statistics about all pools.
        /// </summary>
        public string GetStatsOverview()
        {
            int totalPools = pools.Count;
            int totalActive = 0;
            int totalInactive = 0;

            foreach (var pool in pools)
            {
                if (pool != null)
                {
                    totalActive += pool.ActiveCount;
                    totalInactive += pool.InactiveCount;
                }
            }

            return $"Pools: {totalPools} | Active: {totalActive} | Inactive: {totalInactive}";
        }

        /// <summary>
        /// Adds a pool definition at runtime.
        /// </summary>
        public void AddPool(PoolDefinition pool)
        {
            if (pool != null && pool.Prefab != null && !poolLookup.ContainsKey(pool.Prefab))
            {
                pools.Add(pool);
                poolLookup[pool.Prefab] = pool;
                pool.Initialize(transform, showDebugInfo);
            }
        }
    }
}
