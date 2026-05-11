// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoolMaster
{
    /// <summary>
    /// Central singleton manager for all object pools. Manages pool registry, command buffers, and lifecycle events.
    /// </summary>
    public sealed class PoolingManager : MonoBehaviour
    {
        #region Singleton

        private static PoolingManager instance;
        private static bool _destroyed;

        /// <summary>
        /// Gets the global instance of the pooling manager. Creates one automatically if none exists.
        /// Returns null during application quit to prevent resurrection.
        /// </summary>
        public static PoolingManager Instance
        {
            get
            {
                if (_destroyed)
                    return null;

                if (ReferenceEquals(instance, null) || instance == null)
                {
                    instance = FindFirstObjectByType<PoolingManager>();
                    if (instance == null)
                    {
                        if (!Application.isPlaying)
                            return null;

                        var go = new GameObject("PoolingManager");
                        instance = go.AddComponent<PoolingManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        #endregion

        #region Fields

        /// <summary>
        /// Registry of all pools by their prefab.
        /// </summary>
        private readonly Dictionary<GameObject, IPool> pools = new Dictionary<GameObject, IPool>();

        /// <summary>
        /// Fast lookup of pools by poolId for O(1) access.
        /// </summary>
        private readonly Dictionary<string, IPool> poolsById = new Dictionary<string, IPool>();

        /// <summary>
        /// Command buffers for thread-safe spawning and despawning per pool.
        /// </summary>
        private readonly Dictionary<string, PoolCommandBuffer> commandBuffersById =
            new Dictionary<string, PoolCommandBuffer>();

        /// <summary>
        /// Caches constructor delegates to avoid reflection overhead.
        /// </summary>
        private readonly Dictionary<Type, Func<PoolRequest, IPool>> _ctorCache =
            new Dictionary<Type, Func<PoolRequest, IPool>>();

        /// <summary>
        /// Preset pool configurations created during bootstrap. Configure in inspector or via code.
        /// </summary>
        [Header("Bootstrap Configuration")]
        [SerializeField]
        private List<PoolRequest> presets = new List<PoolRequest>();

        /// <summary>
        /// Tracks which pools have been created to avoid duplicates during bootstrap.
        /// </summary>
        private readonly HashSet<GameObject> createdPools = new HashSet<GameObject>();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Ensure singleton pattern
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            PoolLog.Info("PoolingManager initialized");

            // Bootstrap pools that should be created on Awake
            BootstrapPools(PoolInitializationTiming.OnAwake);
        }

        private void Start()
        {
            // Bootstrap pools that should be created on Start
            BootstrapPools(PoolInitializationTiming.OnStart);
        }

        private void LateUpdate()
        {
            // Flush all command buffers on main thread
            foreach (var kvp in commandBuffersById)
            {
                var poolId = kvp.Key;
                var buffer = kvp.Value;
                if (!buffer.HasPendingOperations)
                    continue; // micro win

                // O(1) lookup using poolsById dictionary
                if (poolsById.TryGetValue(poolId, out var pool) && pool is IPoolControl ctrl)
                {
                    buffer.FlushTo(ctrl); // no reflection
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up all pools and buffers
            foreach (var pool in pools.Values)
            {
                if (pool is IPoolControl poolControl)
                {
                    poolControl.DestroyPool();
                }
            }

            pools.Clear();
            poolsById.Clear();
            commandBuffersById.Clear();
            createdPools.Clear();
            _ctorCache.Clear();

            if (instance == this)
            {
                instance = null;
                _destroyed = true;
            }
        }

        private void OnApplicationQuit()
        {
            _destroyed = true;
        }

        /// <summary>
        /// Resets the destroyed flag. Called automatically on domain reload in editor.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            _destroyed = false;
        }

        #endregion

        #region Pool Management

        /// <summary>
        /// Gets an existing pool or creates a new one for the specified prefab and component type.
        /// </summary>
        /// <typeparam name="T">Component type that implements IPoolable.</typeparam>
        /// <param name="request">Pool configuration request.</param>
        /// <returns>The pool instance.</returns>
        public IPool GetOrCreatePool<T>(PoolRequest request)
            where T : Component, IPoolable
        {
            if (request.prefab == null)
            {
                throw new ArgumentNullException(
                    nameof(request.prefab),
                    "PoolRequest.prefab cannot be null"
                );
            }

            // Check if pool already exists
            if (pools.TryGetValue(request.prefab, out var existingPool))
            {
                return existingPool;
            }

            // Validate prefab has the required component
            var component = request.prefab.GetComponent<T>();
            if (component == null)
            {
                throw new ArgumentException(
                    $"Prefab '{request.prefab.name}' does not have component '{typeof(T).Name}'",
                    nameof(request.prefab)
                );
            }

            // Create new pool
            var pool = new Pool<T>(request.prefab, request, transform);
            pools[request.prefab] = pool;
            poolsById[pool.PoolId] = pool; // Register for O(1) lookup

            // Mark as created to prevent duplicates
            createdPools.Add(request.prefab);

            // Create command buffer for this pool by poolId
            var commandBuffer = new PoolCommandBuffer();
            commandBuffersById[pool.PoolId] = commandBuffer;

            // Publish pool created event
            PoolingEvents.PublishPoolCreated(pool.PoolId, request.prefab);

            PoolLog.Info(
                $"PoolingManager: Created new pool '{pool.PoolId}' for prefab '{request.prefab.name}'"
            );

            return pool;
        }

        /// <summary>
        /// Gets an existing pool or creates a new one for the specified prefab. Convenience overload with explicit prefab parameter.
        /// </summary>
        /// <typeparam name="T">Component type that implements IPoolable.</typeparam>
        /// <param name="prefab">Prefab to create pool for.</param>
        /// <param name="request">Pool configuration request.</param>
        /// <returns>The pool instance.</returns>
        public IPool GetOrCreatePool<T>(GameObject prefab, PoolRequest request)
            where T : Component, IPoolable
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            // Ensure request.prefab is set to avoid inconsistency
            request.prefab = prefab;

            return GetOrCreatePool<T>(request);
        }

        /// <summary>
        /// Non-generic pool registration for plain prefabs that do NOT implement <see cref="IPoolable"/>.
        /// Use this when you want custom <see cref="PoolRequest"/> settings (dynamic expansion,
        /// hard cap, prewarm count) on a prefab that's just geometry/particles without a pooling
        /// component. If the prefab implements IPoolable, routes to the generic overload so the
        /// type-safe Pool&lt;T&gt; path is used instead.
        /// </summary>
        /// <param name="prefab">Prefab to pool.</param>
        /// <param name="request">Pool configuration (expansion, cap, prewarm).</param>
        /// <returns>Existing or newly-created pool.</returns>
        public IPool GetOrCreateGameObjectPool(GameObject prefab, PoolRequest request)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            if (pools.TryGetValue(prefab, out var existing))
                return existing;

            request.prefab = prefab;

            // If the prefab happens to implement IPoolable, prefer the generic type-safe path
            // so behavior matches Spawn()'s auto-creation rules exactly.
            var poolable = prefab.GetComponent<IPoolable>();
            if (poolable != null)
            {
                var componentType = poolable.GetType();
                var constructor = GetOrCreateConstructor(componentType);
                var pool = constructor(request);
                return pool;
            }

            var goPool = new GameObjectPool(prefab, request, transform, request.poolId);
            pools[prefab] = goPool;
            poolsById[goPool.PoolId] = goPool;
            createdPools.Add(prefab);
            commandBuffersById[goPool.PoolId] = new PoolCommandBuffer();
            PoolingEvents.PublishPoolCreated(goPool.PoolId, prefab);
            PoolLog.Debug(
                $"PoolingManager: Created GameObjectPool '{goPool.PoolId}' for prefab '{prefab.name}' (via GetOrCreateGameObjectPool)"
            );
            return goPool;
        }

        /// <summary>
        /// Tries to get an existing pool for the specified prefab.
        /// </summary>
        /// <param name="prefab">Prefab to find pool for.</param>
        /// <param name="pool">Output pool if found.</param>
        /// <returns>True if pool exists, false otherwise.</returns>
        public bool TryGetPool(GameObject prefab, out IPool pool)
        {
            return pools.TryGetValue(prefab, out pool);
        }

        /// <summary>
        /// Gets an existing pool by pool ID using O(1) lookup.
        /// </summary>
        /// <param name="poolId">ID of the pool to find.</param>
        /// <returns>Pool if found, null otherwise.</returns>
        public IPool GetPool(string poolId)
        {
            poolsById.TryGetValue(poolId, out var pool);
            return pool;
        }

        /// <summary>
        /// Gets or creates a command buffer for the specified pool ID. Command buffers enable thread-safe deferred operations.
        /// </summary>
        /// <param name="poolId">Pool ID to get command buffer for.</param>
        /// <returns>Command buffer for the pool.</returns>
        public PoolCommandBuffer GetCommandBuffer(string poolId)
        {
            if (!commandBuffersById.TryGetValue(poolId, out var buffer))
            {
                buffer = new PoolCommandBuffer();
                commandBuffersById[poolId] = buffer;
            }

            return buffer;
        }

        /// <summary>
        /// Gets or creates a cached constructor delegate for the specified component type to eliminate reflection overhead.
        /// </summary>
        /// <param name="componentType">Component type that implements IPoolable.</param>
        /// <returns>Cached delegate to GetOrCreatePool&lt;T&gt; for the component type.</returns>
        private Func<PoolRequest, IPool> GetOrCreateConstructor(Type componentType)
        {
            if (!_ctorCache.TryGetValue(componentType, out var constructor))
            {
                // Build delegate once and cache it
                // This creates a delegate equivalent to: (request) => GetOrCreatePool<T>(request)
                var method = GetType()
                    .GetMethod(nameof(GetOrCreatePool), new[] { typeof(PoolRequest) })
                    .MakeGenericMethod(componentType);

                constructor =
                    (Func<PoolRequest, IPool>)
                        Delegate.CreateDelegate(typeof(Func<PoolRequest, IPool>), this, method);

                _ctorCache[componentType] = constructor;

                PoolLog.Debug(
                    $"PoolingManager: Cached constructor for component type '{componentType.Name}'"
                );
            }

            return constructor;
        }

        /// <summary>
        /// Destroys a pool and all its objects.
        /// </summary>
        /// <param name="prefab">Prefab whose pool to destroy.</param>
        /// <returns>True if pool was destroyed, false if not found.</returns>
        public bool DestroyPool(GameObject prefab)
        {
            if (!pools.TryGetValue(prefab, out var pool))
            {
                return false;
            }

            // Destroy the pool and all its objects (active and inactive)
            if (pool is IPoolControl poolControl)
            {
                poolControl.DestroyPool();
            }

            // Remove from registries
            pools.Remove(prefab);
            poolsById.Remove(pool.PoolId);
            commandBuffersById.Remove(pool.PoolId);
            createdPools.Remove(prefab);

            // Clear cached pool request to prevent memory leaks
            PoolingUtility.RemoveCachedPoolRequest(prefab);

            // Publish pool destroyed event
            PoolingEvents.PublishPoolDestroyed(pool.PoolId, prefab);

            PoolLog.Info(
                $"PoolingManager: Destroyed pool '{pool.PoolId}' for prefab '{prefab.name}'"
            );

            return true;
        }

        #endregion

        #region Bootstrap Management

        /// <summary>
        /// Bootstraps pools based on their initialization timing.
        /// </summary>
        /// <param name="timing">The timing phase to bootstrap.</param>
        private void BootstrapPools(PoolInitializationTiming timing)
        {
            if (presets == null || presets.Count == 0)
            {
                return;
            }

            int bootstrapped = 0;

            foreach (var preset in presets)
            {
                if (preset.initializationTiming == timing && preset.prefab != null)
                {
                    if (TryBootstrapPool(preset))
                    {
                        bootstrapped++;
                    }
                }
            }

            if (bootstrapped > 0)
            {
                PoolLog.Info($"PoolingManager: Bootstrapped {bootstrapped} pools during {timing}");
            }
        }

        /// <summary>
        /// Triggers bootstrap for pools configured with OnEvent timing.
        /// </summary>
        /// <param name="eventId">The event ID to match against preset configurations.</param>
        public void TriggerBootstrap(string eventId)
        {
            if (string.IsNullOrEmpty(eventId) || presets == null || presets.Count == 0)
            {
                return;
            }

            int bootstrapped = 0;

            foreach (var preset in presets)
            {
                if (
                    preset.initializationTiming == PoolInitializationTiming.OnEvent
                    && preset.eventId == eventId
                    && preset.prefab != null
                )
                {
                    if (TryBootstrapPool(preset))
                    {
                        bootstrapped++;
                    }
                }
            }

            if (bootstrapped > 0)
            {
                PoolLog.Info(
                    $"PoolingManager: Bootstrapped {bootstrapped} pools for event '{eventId}'"
                );
            }
        }

        /// <summary>
        /// Attempts to bootstrap a single pool from a preset.
        /// </summary>
        /// <param name="preset">The pool preset configuration.</param>
        /// <returns>True if pool was created, false if it already existed or failed.</returns>
        private bool TryBootstrapPool(PoolRequest preset)
        {
            // Check if pool already exists (reentrancy safety)
            if (createdPools.Contains(preset.prefab) || pools.ContainsKey(preset.prefab))
            {
                return false;
            }

            try
            {
                // If prefab doesn't implement IPoolable, create a non-generic pool.
                var poolable = preset.prefab.GetComponent<IPoolable>();
                if (poolable == null)
                {
                    var goPool = new GameObjectPool(preset.prefab, preset, transform, preset.poolId);
                    pools[preset.prefab] = goPool;
                    poolsById[goPool.PoolId] = goPool;
                    createdPools.Add(preset.prefab);
                    commandBuffersById[goPool.PoolId] = new PoolCommandBuffer();
                    PoolingEvents.PublishPoolCreated(goPool.PoolId, preset.prefab);
                    PoolLog.Debug(
                        $"PoolingManager: Bootstrapped GameObjectPool '{goPool.PoolId}' for prefab '{preset.prefab.name}'"
                    );
                    return true;
                }

                // Otherwise use the generic type-safe pool.
                var componentType = poolable.GetType();
                var constructor = GetOrCreateConstructor(componentType);
                var pool = constructor(preset);

                createdPools.Add(preset.prefab);
                PoolLog.Debug(
                    $"PoolingManager: Bootstrapped pool '{pool.PoolId}' for prefab '{preset.prefab.name}'"
                );
                return true;
            }
            catch (Exception e)
            {
                PoolLog.Error(
                    $"PoolingManager.TryBootstrapPool: Failed to bootstrap pool for prefab '{preset.prefab.name}': {e}"
                );
                return false;
            }
        }

        /// <summary>
        /// Adds a preset pool configuration programmatically.
        /// </summary>
        /// <param name="preset">The pool preset to add.</param>
        public void AddPreset(PoolRequest preset)
        {
            if (preset.prefab == null)
            {
                PoolLog.Warn("PoolingManager.AddPreset: Cannot add preset with null prefab");
                return;
            }

            // Validate configuration before adding
            if (!preset.IsValid())
            {
                PoolLog.Warn($"PoolingManager.AddPreset: Invalid preset for '{preset.prefab.name}', skipping");
                return;
            }

            if (presets == null)
            {
                presets = new List<PoolRequest>();
            }

            // Dedup: skip if this prefab is already registered as a preset or has a live pool
            if (createdPools.Contains(preset.prefab) || pools.ContainsKey(preset.prefab))
            {
                PoolLog.Debug($"PoolingManager.AddPreset: Pool already exists for '{preset.prefab.name}', skipping");
                return;
            }

            // Also check existing presets list to avoid duplicate entries before bootstrap
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].prefab == preset.prefab)
                {
                    PoolLog.Debug($"PoolingManager.AddPreset: Preset already registered for '{preset.prefab.name}', skipping");
                    return;
                }
            }

            presets.Add(preset);
            PoolLog.Debug($"PoolingManager: Added preset for prefab '{preset.prefab.name}'");
        }

        /// <summary>
        /// Removes all presets for a specific prefab.
        /// </summary>
        /// <param name="prefab">The prefab to remove presets for.</param>
        /// <returns>Number of presets removed.</returns>
        public int RemovePresets(GameObject prefab)
        {
            if (presets == null || prefab == null)
            {
                return 0;
            }

            int removed = presets.RemoveAll(p => p.prefab == prefab);

            if (removed > 0)
            {
                PoolLog.Debug(
                    $"PoolingManager: Removed {removed} presets for prefab '{prefab.name}'"
                );
            }

            return removed;
        }

        /// <summary>
        /// Gets all current presets as a read-only list.
        /// </summary>
        /// <returns>Read-only list of current presets.</returns>
        public IReadOnlyList<PoolRequest> GetPresets()
        {
            return presets?.AsReadOnly() ?? new List<PoolRequest>().AsReadOnly();
        }

        #endregion

        #region Spawning API

        /// <summary>
        /// Spawns an object from its pool at the specified position. Automatically detects component type.
        /// </summary>
        /// <param name="prefab">Prefab to spawn.</param>
        /// <param name="position">World position</param>
        /// <param name="rotation">World rotation</param>
        /// <param name="parent">Optional parent transform</param>
        /// <returns>Spawned GameObject, or null if spawn failed</returns>
        public GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null
        )
        {
            if (prefab == null)
            {
                PoolLog.Warn("PoolingManager.Spawn: prefab is null");
                return null;
            }

            // Try to get existing pool
            if (pools.TryGetValue(prefab, out var pool))
            {
                if (pool is IPoolControl poolControl)
                {
                    return poolControl.Spawn(position, rotation, parent);
                }
            }

            // No existing pool - try to create one by detecting component type
            var poolable = prefab.GetComponent<IPoolable>();

            // Create default pool request
            var request = PoolRequest.Create(prefab);

            // If prefab doesn't implement IPoolable, fall back to a non-generic GameObjectPool.
            // This enables code-based pooling for plain prefabs without requiring components.
            if (poolable == null)
            {
                var goPool = new GameObjectPool(prefab, request, transform, request.poolId);
                pools[prefab] = goPool;
                poolsById[goPool.PoolId] = goPool;
                createdPools.Add(prefab);
                commandBuffersById[goPool.PoolId] = new PoolCommandBuffer();
                PoolingEvents.PublishPoolCreated(goPool.PoolId, prefab);
                PoolLog.Info(
                    $"PoolingManager: Created GameObjectPool '{goPool.PoolId}' for prefab '{prefab.name}'"
                );
                return goPool.Spawn(position, rotation, parent);
            }

            // Otherwise use the type-safe generic pool.
            var componentType = poolable.GetType();
            var constructor = GetOrCreateConstructor(componentType);
            pool = constructor(request);

            if (pool is IPoolControl newPoolControl)
            {
                return newPoolControl.Spawn(position, rotation, parent);
            }

            PoolLog.Error($"PoolingManager.Spawn: Failed to create pool for prefab '{prefab.name}'");
            return null;
        }

        /// <summary>
        /// Spawn an object from its pool at the origin.
        /// </summary>
        /// <param name="prefab">Prefab to spawn</param>
        /// <returns>Spawned GameObject, or null if spawn failed</returns>
        public GameObject Spawn(GameObject prefab)
        {
            return Spawn(prefab, Vector3.zero, Quaternion.identity, null);
        }

        /// <summary>
        /// Spawn an object from its pool at the specified position.
        /// </summary>
        /// <param name="prefab">Prefab to spawn</param>
        /// <param name="position">World position</param>
        /// <returns>Spawned GameObject, or null if spawn failed</returns>
        public GameObject Spawn(GameObject prefab, Vector3 position)
        {
            return Spawn(prefab, position, Quaternion.identity, null);
        }

        #endregion

        #region Despawning API

        /// <summary>
        /// Return an object to its pool using the fast path via PooledMarker.
        /// Falls back to searching for the appropriate pool if marker is missing.
        /// </summary>
        /// <param name="instance">GameObject to return to pool</param>
        /// <returns>True if successfully returned, false otherwise</returns>
        public bool Despawn(GameObject instance)
        {
            if (instance == null)
            {
                return false;
            }

            // FAST PATH: Use PooledMarker for direct pool access
            if (instance.TryGetComponent(out PooledMarker marker) && marker.ParentPool != null)
            {
                return marker.ParentPool.Despawn(instance);
            }

            // FALLBACK PATH: Search through all pools (slower)
            foreach (var pool in pools.Values)
            {
                if (pool.Despawn(instance))
                {
                    return true;
                }
            }

            PoolLog.Warn(
                $"PoolingManager.Despawn: Could not find pool for object '{instance.name}'"
            );
            return false;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get statistics about all managed pools.
        /// </summary>
        /// <returns>Dictionary of pool statistics keyed by PoolId</returns>
        public Dictionary<string, PoolMetrics> GetAllPoolMetrics()
        {
            var metrics = new Dictionary<string, PoolMetrics>();

            foreach (var kvp in pools)
            {
                var pool = kvp.Value;

                if (pool is IPoolControl poolControl)
                {
                    metrics[pool.PoolId] = poolControl.Metrics;
                }
            }

            return metrics;
        }

        /// <summary>
        /// Clear all pools managed by this manager.
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in pools.Values)
            {
                if (pool is IPoolControl poolControl)
                {
                    poolControl.Clear();
                }
            }

            PoolLog.Info("PoolingManager: Cleared all pools");
        }

        /// <summary>
        /// Get total number of active objects across all pools.
        /// </summary>
        public int TotalActiveObjects
        {
            get
            {
                int total = 0;
                foreach (var pool in pools.Values)
                {
                    total += pool.ActiveCount;
                }
                return total;
            }
        }

        /// <summary>
        /// Get total number of inactive objects across all pools.
        /// </summary>
        public int TotalInactiveObjects
        {
            get
            {
                int total = 0;
                foreach (var pool in pools.Values)
                {
                    total += pool.InactiveCount;
                }
                return total;
            }
        }

        /// <summary>
        /// Culls unused pools that have been inactive for the specified duration.
        /// Useful for memory management during long-running sessions or after scene transitions.
        /// </summary>
        /// <param name="inactiveDurationSeconds">Minimum time in seconds a pool must be inactive to be culled.</param>
        /// <returns>Number of pools destroyed.</returns>
        public int CullUnusedPools(float inactiveDurationSeconds)
        {
            if (inactiveDurationSeconds <= 0)
            {
                PoolLog.Warn(
                    "PoolingManager.CullUnusedPools: inactiveDurationSeconds must be positive"
                );
                return 0;
            }

            var poolsToCull = CollectionPool.GetList<GameObject>();
            float currentTime = Time.time;

            try
            {
                foreach (var kvp in pools)
                {
                    var prefab = kvp.Key;
                    var pool = kvp.Value;

                    if (pool is IPoolControl ctrl)
                    {
                        var metrics = ctrl.Metrics;

                        // Check if pool has been inactive (no spawns/despawns) for the specified duration
                        // Use LastActivityTime which tracks spawn, despawn, expansion, and cull operations
                        float inactiveDuration = currentTime - metrics.LastActivityTime;

                        // Only cull if pool has no active objects and has been inactive long enough
                        if (pool.ActiveCount == 0 && inactiveDuration >= inactiveDurationSeconds)
                        {
                            poolsToCull.Add(prefab);
                        }
                    }
                }

                // Destroy the identified pools
                int culledCount = 0;
                foreach (var prefab in poolsToCull)
                {
                    if (DestroyPool(prefab))
                    {
                        culledCount++;
                    }
                }

                if (culledCount > 0)
                {
                    PoolLog.Info(
                        $"PoolingManager: Culled {culledCount} unused pools (inactive > {inactiveDurationSeconds}s)"
                    );
                }

                return culledCount;
            }
            finally
            {
                CollectionPool.Return(poolsToCull);
            }
        }

        /// <summary>
        /// Take a snapshot of a specific pool's statistics.
        /// Useful for monitoring individual pool performance.
        /// </summary>
        /// <param name="poolId">Pool identifier to snapshot</param>
        /// <returns>Snapshot of the specified pool, or empty snapshot if not found</returns>
        public PoolSnapshot TakeSnapshot(string poolId)
        {
            var pool = GetPool(poolId);
            if (pool != null && pool is IPoolControl ctrl)
            {
                return new PoolSnapshot(
                    totalPools: 1,
                    totalActive: pool.ActiveCount,
                    totalInactive: pool.InactiveCount,
                    poolBreakdown: new Dictionary<string, PoolMetrics> { { poolId, ctrl.Metrics } }
                );
            }

            // Return empty snapshot if pool not found
            return new PoolSnapshot(
                totalPools: 0,
                totalActive: 0,
                totalInactive: 0,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );
        }

        /// <summary>
        /// Get a lightweight snapshot of all pool statistics.
        /// Useful for real-time monitoring and diagnostics.
        /// </summary>
        /// <returns>Snapshot of current pool state</returns>
        public PoolSnapshot GetSnapshot()
        {
            int totalActive = 0;
            int totalInactive = 0;
            int totalPools = pools.Count;
            var poolBreakdown = new Dictionary<string, PoolMetrics>();

            foreach (var kvp in pools)
            {
                var pool = kvp.Value;

                totalActive += pool.ActiveCount;
                totalInactive += pool.InactiveCount;

                if (pool is IPoolControl poolControl)
                {
                    // Use PoolId instead of prefab.name to avoid key collisions
                    // when multiple pools reference prefabs with the same name
                    poolBreakdown[pool.PoolId] = poolControl.Metrics;
                }
            }

            return new PoolSnapshot(totalPools, totalActive, totalInactive, poolBreakdown);
        }

        #endregion
    }
}
