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
    /// Generic pool implementation for managing GameObjects with IPoolable components.
    /// Operates entirely on the main thread with automatic instantiation, component caching,
    /// integrated diagnostics, event notifications, and compile-time logging.
    ///
    /// Thread-Safety: This class is NOT thread-safe. All methods must be called from Unity's main thread.
    /// For background thread operations, use PoolCommandBuffer to enqueue commands for main-thread execution.
    /// </summary>
    /// <typeparam name="T">Component type that implements IPoolable.</typeparam>
    public sealed class Pool<T> : IPool, IPoolControl, IPoolInternal
        where T : Component, IPoolable
    {
        private readonly GameObject prefab;
        private readonly Transform poolParent;
        private readonly PoolRequest request;
        private readonly string poolId;

        private readonly Stack<GameObject> inactivePool = new Stack<GameObject>();
        private int activeCount = 0; // Track active count directly, no HashSet needed
        private PoolMetricsTracker metricsTracker;

        /// <summary>
        /// Gets the prefab managed by this pool.
        /// </summary>
        public GameObject Prefab => prefab;

        /// <summary>
        /// Gets the number of currently active (spawned) objects in this pool.
        /// </summary>
        public int ActiveCount => activeCount;

        /// <summary>
        /// Gets the number of inactive (pooled) objects ready for reuse.
        /// </summary>
        public int InactiveCount => inactivePool.Count;

        /// <summary>
        /// Gets the total capacity of this pool (active + inactive objects).
        /// </summary>
        public int Capacity => ActiveCount + InactiveCount;

        /// <summary>
        /// Gets the total capacity of this pool. Alias for Capacity.
        /// </summary>
        public int TotalCount => Capacity;

        /// <summary>
        /// Gets the unique identifier for this pool instance.
        /// </summary>
        public string PoolId => poolId;

        /// <summary>
        /// Gets a read-only snapshot of current pool metrics.
        /// </summary>
        public PoolMetrics Metrics => metricsTracker.ToReadOnly();

        /// <summary>
        /// Creates a new pool for the specified prefab with configurable behavior.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate (must have component T).</param>
        /// <param name="request">Pool configuration settings.</param>
        /// <param name="poolParent">Optional parent transform for pooled objects.</param>
        /// <param name="poolId">Optional custom pool identifier.</param>
        public Pool(
            GameObject prefab,
            PoolRequest request,
            Transform poolParent = null,
            string poolId = null
        )
        {
            this.prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            this.request = request;
            // Pool ID precedence: explicit poolGuid (cross-scene determinism) wins,
            // then the ctor-arg poolId, then the request's poolId, finally a
            // hash-based fallback. Without this chain, PoolingManager.GetOrCreatePool<T>
            // ignored both request.poolId and request.poolGuid because it doesn't pass
            // a ctor poolId argument — so users setting either field would find that
            // PoolingManager.GetPool(yourId) returned null.
            this.poolId =
                !string.IsNullOrEmpty(request.poolGuid) ? request.poolGuid
                : !string.IsNullOrEmpty(poolId) ? poolId
                : !string.IsNullOrEmpty(request.poolId) ? request.poolId
                : $"{prefab.name}_Pool_{GetHashCode()}";

            // Create a named container whenever usePoolContainer is true. If a
            // poolParent was passed in (e.g. PoolingManager.transform), the new
            // container is parented under it so the hierarchy reads as
            // "PoolingManager → [containerName] → pooled instances". When poolParent
            // is null the container sits at scene root.
            //
            // Previously this only ran when poolParent was null, which made
            // usePoolContainer / containerName silently no-op for any pool created
            // through PoolingManager (the typical entry point).
            if (request.usePoolContainer)
            {
                var containerName = string.IsNullOrEmpty(request.containerName)
                    ? $"{prefab.name}_Pool"
                    : request.containerName;
                var container = new GameObject(containerName);
                if (poolParent != null)
                    container.transform.SetParent(poolParent, worldPositionStays: false);
                this.poolParent = container.transform;
            }
            else
            {
                this.poolParent = poolParent;
            }

            // Initialize metrics
            metricsTracker = new PoolMetricsTracker(Time.time);

            // Validate that prefab has the required component
            if (prefab.GetComponent<T>() == null)
            {
                throw new ArgumentException(
                    $"Prefab '{prefab.name}' does not have component '{typeof(T).Name}'",
                    nameof(prefab)
                );
            }

            PoolLog.Info(
                $"Created pool '{this.poolId}' for prefab '{prefab.name}' with request: {request}"
            );

            // Pre-warm pool if requested. Previously this prewarmed unconditionally and
            // ignored request.initializationTiming, which silently broke Lazy/NextFrame
            // configurations (instances allocated at ctor time even for Lazy pools).
            // GameObjectPool already honored the timing modes; Pool<T> now matches.
            if (request.shouldPrewarm && request.initialPoolSize > 0)
            {
                switch (request.initializationTiming)
                {
                    case PoolInitializationTiming.Lazy:
                        // Lazy: do nothing now; SpawnInternal expands on demand.
                        break;
                    case PoolInitializationTiming.NextFrame:
                        if (PoolingManager.Instance != null)
                            PoolingManager.Instance.StartCoroutine(PrewarmNextFrame(request.initialPoolSize));
                        else
                            PrewarmPool(request.initialPoolSize);
                        break;
                    case PoolInitializationTiming.Immediate:
                    case PoolInitializationTiming.OnAwake:
                    case PoolInitializationTiming.OnStart:
                    case PoolInitializationTiming.OnEvent:
                    default:
                        PrewarmPool(request.initialPoolSize);
                        break;
                }
            }
        }

        private System.Collections.IEnumerator PrewarmNextFrame(int count)
        {
            yield return null;
            PrewarmPool(count);
        }

        /// <summary>
        /// Spawns an object from the pool at the specified position and rotation.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return SpawnInternal(position, rotation, parent);
        }

        /// <summary>
        /// Spawns an object at the origin with default rotation.
        /// </summary>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        public GameObject Spawn()
        {
            return SpawnInternal(Vector3.zero, Quaternion.identity, null);
        }

        /// <summary>
        /// Spawns an object at the specified position with default rotation.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        public GameObject Spawn(Vector3 position)
        {
            return SpawnInternal(position, Quaternion.identity, null);
        }

        /// <summary>
        /// Spawn an object from this pool at a specific position and rotation.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <returns>Spawned GameObject, or null if spawn failed</returns>
        public GameObject Spawn(Vector3 position, Quaternion rotation)
        {
            return SpawnInternal(position, rotation, null);
        }

        /// <summary>
        /// Spawn an object from this pool at a specific position, rotation, and parent.
        /// This overload explicitly implements the IPoolControl interface for command buffer support.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <param name="parent">Parent transform for the spawned object</param>
        /// <returns>Spawned GameObject, or null if spawn failed</returns>
        GameObject IPoolControl.Spawn(Vector3 position, Quaternion rotation, Transform parent)
        {
            return SpawnInternal(position, rotation, parent);
        }

        /// <summary>
        /// Try to spawn an object from this pool at the default position.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        public bool TrySpawn(out GameObject instance)
        {
            instance = SpawnInternal(Vector3.zero, Quaternion.identity, null);
            return instance != null;
        }

        /// <summary>
        /// Try to spawn an object from this pool at a specific position.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        public bool TrySpawn(Vector3 position, out GameObject instance)
        {
            instance = SpawnInternal(position, Quaternion.identity, null);
            return instance != null;
        }

        /// <summary>
        /// Try to spawn an object from this pool at a specific position and rotation.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        public bool TrySpawn(Vector3 position, Quaternion rotation, out GameObject instance)
        {
            instance = SpawnInternal(position, rotation, null);
            return instance != null;
        }

        /// <summary>
        /// Try to spawn an object from this pool at a specific position, rotation, and parent.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <param name="parent">Parent transform for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        public bool TrySpawn(
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out GameObject instance
        )
        {
            instance = SpawnInternal(position, rotation, parent);
            return instance != null;
        }

        /// <summary>
        /// Return an object to the pool for reuse.
        /// Alias for Despawn to maintain backward compatibility with IPool interface.
        /// </summary>
        /// <param name="obj">GameObject to return (must be from this pool)</param>
        public void ReturnToPool(GameObject obj)
        {
            Despawn(obj);
        }

        /// <summary>
        /// Returns an object to the pool for reuse.
        /// </summary>
        /// <param name="instance">GameObject to return (must be from this pool).</param>
        /// <returns>True if successfully returned, false otherwise.</returns>
        public bool Despawn(GameObject instance)
        {
            if (instance == null)
            {
                PoolLog.Warn($"Pool '{poolId}': Attempted to despawn null object");
                return false;
            }

            // Check if this object is actually spawned from this pool using marker
            if (
                !instance.TryGetComponent(out PooledMarker marker)
                || !marker.IsSpawnedFromPool
                || marker.ParentPool != this
            )
            {
                PoolLog.Warn(
                    $"Pool '{poolId}': Attempted to despawn object '{instance.name}' that is not from this pool"
                );
                return false;
            }

            // Check capacity limits
            if (request.maxPoolSize > 0 && InactiveCount >= request.maxPoolSize)
            {
                if (request.cullExcessObjects)
                {
                    PoolLog.Debug(
                        $"Pool '{poolId}': Pool at max capacity ({request.maxPoolSize}), destroying object '{instance.name}'"
                    );
                    DestroyInstance(instance);

                    // Fire pool culled event
                    PoolingEvents.PublishPoolCulled(poolId, 1);

                    return true;
                }
                // else: still push to inactive; optional: log once about exceeding target
                PoolLog.Debug(
                    $"Pool '{poolId}': Pool exceeds target capacity ({request.maxPoolSize}) but culling disabled, keeping object"
                );
            }

            // Get cached component from marker
            T component = marker.CachedPoolableComponent as T;
            if (component == null)
            {
                PoolLog.Error(
                    $"Pool '{poolId}': No cached component for object '{instance.name}' during despawn"
                );
                return false;
            }

            try
            {
                // Notify component of despawn while still active (stops sounds/FX/coroutines)
                component.OnDespawned();

                // Reparent to pool container (but don't force transform reset)
                instance.transform.SetParent(poolParent);

                // Mark as no longer spawned from pool BEFORE deactivating
                // This prevents re-entrancy issues if OnDisable tries to interact with the pool
                if (instance.TryGetComponent(out PooledMarker pooledMarker))
                {
                    pooledMarker.IsSpawnedFromPool = false;
                }

                // Decrement active count (guard against underflow from external destroys)
                if (activeCount > 0)
                    activeCount--;
                else
                    PoolLog.Warn($"Pool '{poolId}': Despawn called but activeCount was already 0 — possible double-despawn or external destroy");

                // Deactivate the object (prevents "half-alive" state)
                instance.SetActive(false);

                // Reset component to default state after deactivation (object decides transform reset)
                component.PoolReset();

                // Move to inactive pool
                inactivePool.Push(instance);

                // Update metrics
                metricsTracker.RecordDespawn();

                // Fire event
                PoolingEvents.PublishObjectDespawned(instance, poolId);

                PoolLog.Debug(
                    $"Pool '{poolId}': Despawned object '{instance.name}' (Active: {ActiveCount}, Inactive: {InactiveCount})"
                );
                return true;
            }
            catch (Exception e)
            {
                PoolLog.Error($"Pool '{poolId}': Error during despawn of '{instance.name}': {e}");
                // Fallback: destroy the problematic object
                DestroyInstance(instance);
                return false;
            }
        }

        /// <summary>
        /// Pre-warms the pool by creating the specified number of inactive objects.
        /// </summary>
        /// <param name="count">Number of objects to create.</param>
        public void PrewarmPool(int count)
        {
            if (count <= 0)
                return;

            PoolLog.Info($"Pool '{poolId}': Pre-warming with {count} objects");

            for (int i = 0; i < count; i++)
            {
                GameObject instance = CreateNewInstance();
                if (instance != null)
                {
                    // Call OnDespawned() to properly reset poolable state before deactivating
                    // Use cached component from marker
                    if (
                        instance.TryGetComponent(out PooledMarker marker)
                        && marker.CachedPoolableComponent is T poolable
                    )
                    {
                        poolable.OnDespawned();
                    }

                    instance.SetActive(false);
                    inactivePool.Push(instance);
                }
            }

            PoolLog.Debug($"Pool '{poolId}': Pre-warming complete (Inactive: {InactiveCount})");

            // Fire pool prewarmed event
            PoolingEvents.PublishPoolPrewarmed(poolId, count);
        }

        /// <summary>
        /// Clears all inactive objects from the pool by destroying them.
        /// Active (spawned) objects are not affected and remain in the scene.
        /// Use this to free memory from pooled objects that are no longer needed.
        /// </summary>
        public void ClearPool()
        {
            PoolLog.Info($"Pool '{poolId}': Clearing {InactiveCount} inactive objects");

            int culledCount = InactiveCount;

            while (inactivePool.Count > 0)
            {
                GameObject instance = inactivePool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            metricsTracker.RecordCull();

            // Fire pool culled event if objects were destroyed
            if (culledCount > 0)
            {
                PoolingEvents.PublishPoolCulled(poolId, culledCount);
            }

            PoolLog.Debug($"Pool '{poolId}': Pool cleared");
        }

        /// <summary>
        /// Clear all objects from the pool (both active and inactive).
        /// Alias for ClearPool to implement IPoolControl interface.
        /// </summary>
        public void Clear()
        {
            ClearPool();
        }

        /// <summary>
        /// Trim inactive cache down to a target size (or zero).
        /// Useful for memory pressure events like menu loads or scene transitions.
        /// </summary>
        /// <param name="targetInactive">Target number of inactive objects to keep (default: 0)</param>
        public void ShrinkInactive(int targetInactive = 0)
        {
            if (targetInactive < 0)
            {
                PoolLog.Warn(
                    $"Pool '{poolId}': ShrinkInactive called with negative target ({targetInactive}), using 0"
                );
                targetInactive = 0;
            }

            int toRemove = inactivePool.Count - targetInactive;
            if (toRemove <= 0)
            {
                return; // Already at or below target
            }

            PoolLog.Info(() =>
                $"Pool '{poolId}': Shrinking inactive objects from {inactivePool.Count} to {targetInactive} (removing {toRemove})"
            );

            for (int i = 0; i < toRemove; i++)
            {
                if (inactivePool.Count == 0)
                    break; // Safety check

                var obj = inactivePool.Pop();

                // Null check: object may have been destroyed externally
                if (obj == null)
                {
                    PoolLog.Debug($"Pool '{poolId}': Skipped null object during ShrinkInactive");
                    continue; // Skip null without throwing
                }

                // Destroy the GameObject
#if UNITY_EDITOR
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(obj);
                else
                    UnityEngine.Object.DestroyImmediate(obj);
#else
                UnityEngine.Object.Destroy(obj);
#endif
            }

            // Update metrics
            for (int j = 0; j < toRemove; j++)
            {
                metricsTracker.RecordCull();
            }
        }

        /// <summary>
        /// Check if a specific GameObject belongs to this pool.
        /// </summary>
        /// <param name="instance">The GameObject to check</param>
        /// <returns>True if the object belongs to this pool, false otherwise</returns>
        public bool ContainsObject(GameObject instance)
        {
            // Check if object has our marker and belongs to this pool
            return instance != null
                && instance.TryGetComponent(out PooledMarker marker)
                && marker.ParentPool == this;
        }

        /// <summary>
        /// Destroys the pool and all its objects (active and inactive). Do not use this pool after calling this method.
        /// </summary>
        public void DestroyPool()
        {
            PoolLog.Info($"Pool '{poolId}': Destroying pool with {Capacity} total objects");

            // Destroy inactive objects
            while (inactivePool.Count > 0)
            {
                GameObject instance = inactivePool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            // Note: We can't iterate active objects anymore (no HashSet)
            // Active objects will be cleaned up via OnDestroy hooks when they're destroyed
            // or manually by calling code. Reset active count.
            activeCount = 0;

            PoolLog.Debug($"Pool '{poolId}': Pool destroyed");
        }

        /// <summary>
        /// Flushes and rebuilds the pool. Force-despawns every active instance found via
        /// PooledMarker, destroys all inactive instances, and optionally re-prewarms.
        /// Use after editing the source prefab at runtime — stale clones are replaced
        /// with fresh ones on the next Spawn().
        /// </summary>
        public void Reseed(bool rePrewarm = true)
        {
            PoolLog.Info($"Pool '{poolId}': Reseed requested (rePrewarm={rePrewarm})");

            // 1. Force-despawn every active instance by walking PooledMarkers in the scene.
            //    O(n) over all PooledMarkers — only invoked on explicit user action so
            //    the allocation/scan cost is acceptable.
            var allMarkers = UnityEngine.Object.FindObjectsByType<PooledMarker>(FindObjectsSortMode.None);
            int despawnedActive = 0;
            for (int i = 0; i < allMarkers.Length; i++)
            {
                var marker = allMarkers[i];
                if (marker != null
                    && ReferenceEquals(marker.ParentPool, this)
                    && marker.IsSpawnedFromPool)
                {
                    if (Despawn(marker.gameObject))
                        despawnedActive++;
                }
            }

            // 2. Destroy all inactive instances — they hold stale prefab state.
            int destroyedInactive = inactivePool.Count;
            Clear();

            // 3. Optionally re-prewarm to the original initial size.
            int reCreated = 0;
            if (rePrewarm && request.shouldPrewarm && request.initialPoolSize > 0)
            {
                int beforeCount = TotalCount;
                PrewarmPool(request.initialPoolSize);
                reCreated = TotalCount - beforeCount;
            }

            PoolLog.Info(
                $"Pool '{poolId}': Reseed complete — despawned {despawnedActive} active, " +
                $"destroyed {destroyedInactive} inactive, re-prewarmed {reCreated}"
            );
        }

        /// <summary>
        /// Get diagnostic information about the pool state.
        /// </summary>
        public override string ToString()
        {
            return $"Pool<{typeof(T).Name}> '{poolId}' {{ Active: {ActiveCount}, Inactive: {InactiveCount}, Metrics: {Metrics} }}";
        }

        /// <summary>
        /// Internal notification that an object was destroyed externally.
        /// Removes dead references from tracking structures to prevent poisoned pool counts.
        /// </summary>
        /// <param name="instance">The destroyed GameObject instance.</param>
        void IPoolInternal.NotifyObjectDestroyed(GameObject instance)
        {
            if (instance == null)
                return;

            // Check if object was active via marker before it was destroyed
            if (instance.TryGetComponent(out PooledMarker marker))
            {
                bool wasActive = marker.IsSpawnedFromPool;
                if (wasActive && activeCount > 0)
                {
                    activeCount--;
                    PoolLog.Debug(
                        $"Pool '{poolId}': Cleaned up externally destroyed active object '{instance.name}'"
                    );
                }
            }
        }

        // Internal spawn implementation
        private GameObject SpawnInternal(Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject instance = null;
            bool isExpansion = false; // Track if this spawn represents pool expansion

            // Try to reuse from pool first - use while loop to avoid recursion/stack overflow
            while (inactivePool.Count > 0)
            {
                instance = inactivePool.Pop();

                // Validate pooled object
                if (instance != null)
                {
                    break; // Found valid instance
                }

                // Null found, log and continue popping
                PoolLog.Warn($"Pool '{poolId}': Found null object in inactive pool, skipping");
            }

            // If no valid instance found, try to create new one
            if (instance == null)
            {
                // Check if we can expand or if pool is exhausted
                if (
                    request.maxPoolSize > 0
                    && TotalCount >= request.maxPoolSize
                    && !request.allowDynamicExpansion
                )
                {
                    PoolLog.Warn(
                        $"Pool '{poolId}': Pool exhausted at max capacity ({request.maxPoolSize}) and expansion not allowed"
                    );

                    // Fire pool exhausted event
                    PoolingEvents.PublishPoolExhausted(poolId, request.maxPoolSize);

                    return null;
                }

                // Create new instance if pool is empty
                instance = CreateNewInstance();
                if (instance == null)
                {
                    PoolLog.Error($"Pool '{poolId}': Failed to create new instance");
                    return null;
                }

                // Track that this is a new expansion (will record after adding to active)
                isExpansion = TotalCount >= request.initialPoolSize;
            }

            // Get cached component from marker
            if (
                !instance.TryGetComponent(out PooledMarker instanceMarker)
                || instanceMarker.CachedPoolableComponent == null
            )
            {
                PoolLog.Error(
                    $"Pool '{poolId}': No marker or cached component for object '{instance.name}' during spawn"
                );
                return null;
            }
            T component = instanceMarker.CachedPoolableComponent as T;

            try
            {
                // Setup transform
                instance.transform.position = position;
                instance.transform.rotation = rotation;
                instance.transform.SetParent(parent);

                // Increment active count and mark as spawned BEFORE activating
                // This prevents re-entrancy issues if OnEnable tries to interact with the pool
                activeCount++;

                // Record expansion metrics now that object is counted as active
                // This ensures TotalCount is accurate when recording
                if (isExpansion)
                {
                    metricsTracker.RecordExpansion();
                    PoolingEvents.PublishPoolExpanded(poolId, TotalCount - 1, TotalCount);
                }

                // Mark as spawned from pool for O(1) state checks
                if (instance.TryGetComponent(out PooledMarker marker))
                {
                    marker.IsSpawnedFromPool = true;
                }

                // Activate the object (triggers OnEnable on components)
                instance.SetActive(true);

                // Initialize component
                component.OnSpawned();

                // Update metrics
                metricsTracker.RecordSpawn();

                // Fire event
                PoolingEvents.PublishObjectSpawned(instance, poolId);

                PoolLog.Debug(
                    $"Pool '{poolId}': Spawned object '{instance.name}' at {position} (Active: {ActiveCount}, Inactive: {InactiveCount})"
                );
                return instance;
            }
            catch (Exception e)
            {
                PoolLog.Error($"Pool '{poolId}': Error during spawn at {position}: {e}");

                // Cleanup failed spawn to prevent orphaned active objects
                // Clear marker flag if it was set
                if (instance != null && instance.TryGetComponent(out PooledMarker marker))
                {
                    if (marker.IsSpawnedFromPool)
                    {
                        marker.IsSpawnedFromPool = false;
                        activeCount--; // Decrement if it was counted
                    }
                }

                // Destroy the instance to prevent orphaned active objects in the scene
                // This is safer than trying to rollback to inactive pool
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                    PoolLog.Debug($"Pool '{poolId}': Destroyed failed spawn instance");
                }

                return null;
            }
        }

        // Create a new instance from prefab
        private GameObject CreateNewInstance()
        {
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, poolParent);
                instance.name = $"{prefab.name}(Pool:{poolId})";

                // Get component for caching
                T component = instance.GetComponent<T>();
                if (component == null)
                {
                    PoolLog.Error(
                        $"Pool '{poolId}': Instantiated prefab '{prefab.name}' is missing component '{typeof(T).Name}'"
                    );
                    UnityEngine.Object.Destroy(instance);
                    return null;
                }

                // Bind pool relationships (one-time at creation)
                component.ParentPool = this;

                // Ensure fast-path marker exists and cache the component there
                var marker =
                    instance.GetComponent<PooledMarker>() ?? instance.AddComponent<PooledMarker>();
                marker.ParentPool = this;
                marker.CachedPoolableComponent = component; // Cache component on marker for O(1) access
                marker.IsSpawnedFromPool = false; // Initialize to inactive state

                metricsTracker.RecordCreation();

                PoolLog.Debug($"Pool '{poolId}': Created new instance '{instance.name}'");

                // Fire object created event
                PoolingEvents.PublishObjectCreated(instance, poolId);

                return instance;
            }
            catch (Exception e)
            {
                PoolLog.Error(
                    $"Pool '{poolId}': Failed to create instance from prefab '{prefab.name}': {e}"
                );
                return null;
            }
        }

        // Destroy an instance and clean up references
        private void DestroyInstance(GameObject instance)
        {
            if (instance == null)
                return;

            // Decrement active count if object was active
            if (instance.TryGetComponent(out PooledMarker marker))
            {
                if (marker.IsSpawnedFromPool)
                {
                    activeCount--;
                    marker.IsSpawnedFromPool = false;
                }
            }

            UnityEngine.Object.Destroy(instance);
            metricsTracker.RecordDestruction();

            PoolLog.Debug($"Pool '{poolId}': Destroyed instance '{instance.name}'");
        }
    }
}
