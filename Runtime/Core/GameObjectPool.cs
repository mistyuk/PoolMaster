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
    /// Non-generic pool implementation that can pool any prefab, even if it does not implement IPoolable.
    ///
    /// If spawned instances contain an IPoolable component, lifecycle hooks will be invoked.
    /// Otherwise, pooling still works via PooledMarker fast-path + activation/deactivation.
    /// </summary>
    public sealed class GameObjectPool : IPool, IPoolControl, IPoolInternal
    {
        private readonly GameObject prefab;
        private readonly Transform poolParent;
        private readonly PoolRequest request;
        private readonly string poolId;

        private readonly Stack<GameObject> inactivePool = new Stack<GameObject>();
        private int activeCount = 0;
        private PoolMetricsTracker metricsTracker;

        public GameObject Prefab => prefab;
        public int ActiveCount => activeCount;
        public int InactiveCount => inactivePool.Count;
        public int Capacity => ActiveCount + InactiveCount;
        public int TotalCount => Capacity;
        public string PoolId => poolId;
        public PoolMetrics Metrics => metricsTracker.ToReadOnly();

        public GameObjectPool(GameObject prefab, PoolRequest request, Transform poolParent = null, string poolId = null)
        {
            this.prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            this.request = request;

            // Prefer deterministic IDs when provided
            this.poolId =
                !string.IsNullOrEmpty(request.poolGuid)
                    ? request.poolGuid
                    : (!string.IsNullOrEmpty(poolId) ? poolId : (!string.IsNullOrEmpty(request.poolId) ? request.poolId : $"{prefab.name}_Pool_{GetHashCode()}"));

            if (request.usePoolContainer && poolParent == null)
            {
                var containerName = string.IsNullOrEmpty(request.containerName) ? $"{prefab.name}_Pool" : request.containerName;
                var container = new GameObject(containerName);
                this.poolParent = container.transform;
            }
            else
            {
                this.poolParent = poolParent;
            }

            metricsTracker = new PoolMetricsTracker(Time.time);

            PoolLog.Info($"Created GameObjectPool '{this.poolId}' for prefab '{prefab.name}' with request: {request}");

            if (request.shouldPrewarm && request.initialPoolSize > 0)
            {
                switch (request.initializationTiming)
                {
                    case PoolInitializationTiming.Immediate:
                    case PoolInitializationTiming.OnAwake:
                    case PoolInitializationTiming.OnStart:
                    case PoolInitializationTiming.OnEvent: // OnEvent pools are created by TriggerBootstrap — prewarm immediately at construction time
                        PrewarmPool(request.initialPoolSize);
                        break;
                    case PoolInitializationTiming.NextFrame:
                        // Instance can be null during app quit / non-Play mode per PoolingManager
                        // singleton hardening. Fall back to immediate prewarm in that case rather
                        // than NRE'ing on StartCoroutine.
                        var manager = PoolingManager.Instance;
                        if (manager != null)
                            manager.StartCoroutine(PrewarmNextFrame(request.initialPoolSize));
                        else
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
        /// Prewarms the pool across multiple frames to avoid frame spikes.
        /// Creates objects until the per-frame time budget is hit, then yields.
        /// </summary>
        /// <param name="count">Total number of objects to prewarm.</param>
        /// <param name="frameBudgetMs">Max milliseconds to spend per frame (default 2ms).</param>
        public System.Collections.IEnumerator PrewarmSpread(int count, float frameBudgetMs = 2f)
        {
            if (count <= 0)
                yield break;

            if (request.maxPoolSize > 0)
            {
                int remaining = Mathf.Max(0, request.maxPoolSize - TotalCount);
                count = Mathf.Min(count, remaining);
            }

            int created = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                var instance = CreateNewInstance();
                if (instance == null)
                    break;

                instance.SetActive(false);
                inactivePool.Push(instance);
                created++;

                // Yield if we've exceeded the per-frame budget
                if (sw.Elapsed.TotalMilliseconds >= frameBudgetMs)
                {
                    yield return null;
                    sw.Restart();
                }
            }

            if (created > 0)
            {
                PoolingEvents.PublishPoolPrewarmed(poolId, created);
            }
        }

        public void ReturnToPool(GameObject obj) => Despawn(obj);

        public bool Despawn(GameObject instance)
        {
            if (instance == null)
                return false;

            if (!instance.TryGetComponent(out PooledMarker marker) || marker.ParentPool != this)
                return false;

            // Reject double-despawn: if already inactive in this pool, bail before pushing again.
            // Without this check the same instance gets pushed onto inactivePool twice and Spawn()
            // hands out duplicates on subsequent calls.
            if (!marker.IsSpawnedFromPool)
            {
                PoolLog.Warn($"Pool '{poolId}': Despawn called on '{instance.name}' that is already inactive — ignoring");
                return false;
            }

            // Call poolable hooks if present
            try
            {
                if (marker.CachedPoolableComponent is IPoolable poolable)
                {
                    poolable.OnDespawned();
                }
            }
            catch (Exception e)
            {
                PoolLog.Warn($"Pool '{poolId}': Exception during OnDespawned for '{instance.name}': {e.Message}");
            }

            marker.IsSpawnedFromPool = false;

            // Deactivate and reparent
            instance.SetActive(false);
            instance.transform.SetParent(poolParent);

            activeCount = Mathf.Max(0, activeCount - 1);
            metricsTracker.RecordDespawn();
            PoolingEvents.PublishObjectDespawned(instance, poolId);

            // Cull-on-overflow: when the inactive cache is already at maxPoolSize and
            // cullExcessObjects is enabled, destroy this instance instead of pooling it.
            // Matches Pool<T>'s semantics — previous behavior unconditionally pushed then
            // called ShrinkInactive, which destroyed instances on every despawn once the
            // pool was full (defeating the point of pooling).
            if (request.cullExcessObjects && request.maxPoolSize > 0 && InactiveCount >= request.maxPoolSize)
            {
                UnityEngine.Object.Destroy(instance);
                metricsTracker.RecordDestruction();
                PoolingEvents.PublishPoolCulled(poolId, 1);
            }
            else
            {
                inactivePool.Push(instance);
            }

            return true;
        }

        public GameObject Spawn() => Spawn(Vector3.zero, Quaternion.identity, null);
        public GameObject Spawn(Vector3 position) => Spawn(position, Quaternion.identity, null);
        public GameObject Spawn(Vector3 position, Quaternion rotation) => Spawn(position, rotation, null);

        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent)
        {
            var instance = SpawnInternal(position, rotation, parent);
            return instance;
        }

        public bool TrySpawn(out GameObject instance) => TrySpawn(Vector3.zero, Quaternion.identity, null, out instance);
        public bool TrySpawn(Vector3 position, out GameObject instance) => TrySpawn(position, Quaternion.identity, null, out instance);
        public bool TrySpawn(Vector3 position, Quaternion rotation, out GameObject instance) => TrySpawn(position, rotation, null, out instance);

        public bool TrySpawn(Vector3 position, Quaternion rotation, Transform parent, out GameObject instance)
        {
            instance = SpawnInternal(position, rotation, parent);
            return instance != null;
        }

        private GameObject SpawnInternal(Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject instance = null;
            bool isExpansion = false;

            while (inactivePool.Count > 0)
            {
                instance = inactivePool.Pop();
                if (instance != null)
                    break;

                PoolLog.Warn($"Pool '{poolId}': Found null object in inactive pool, skipping");
            }

            if (instance == null)
            {
                if (request.maxPoolSize > 0 && TotalCount >= request.maxPoolSize && !request.allowDynamicExpansion)
                {
                    PoolLog.Warn($"Pool '{poolId}': Pool exhausted at max capacity ({request.maxPoolSize}) and expansion not allowed");
                    PoolingEvents.PublishPoolExhausted(poolId, request.maxPoolSize);
                    return null;
                }

                instance = CreateNewInstance();
                if (instance == null)
                {
                    PoolLog.Error($"Pool '{poolId}': Failed to create new instance");
                    return null;
                }

                isExpansion = TotalCount >= request.initialPoolSize;
            }

            // Ensure marker exists
            if (!instance.TryGetComponent(out PooledMarker instanceMarker))
            {
                instanceMarker = instance.AddComponent<PooledMarker>();
            }

            try
            {
                instance.transform.position = position;
                instance.transform.rotation = rotation;
                instance.transform.SetParent(parent);

                activeCount++;

                if (isExpansion)
                {
                    metricsTracker.RecordExpansion();
                    PoolingEvents.PublishPoolExpanded(poolId, TotalCount - 1, TotalCount);
                }

                instanceMarker.IsSpawnedFromPool = true;

                instance.SetActive(true);

                // Call poolable hooks if present
                if (instanceMarker.CachedPoolableComponent is IPoolable poolable)
                {
                    poolable.OnSpawned();
                }

                metricsTracker.RecordSpawn();
                PoolingEvents.PublishObjectSpawned(instance, poolId);

                return instance;
            }
            catch (Exception e)
            {
                PoolLog.Error($"Pool '{poolId}': Error during spawn at {position}: {e}");

                if (instance != null && instance.TryGetComponent(out PooledMarker marker))
                {
                    if (marker.IsSpawnedFromPool)
                    {
                        marker.IsSpawnedFromPool = false;
                        activeCount = Mathf.Max(0, activeCount - 1);
                    }
                }

                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }

                return null;
            }
        }

        public void PrewarmPool(int count)
        {
            if (count <= 0)
                return;

            // Clamp by maxPoolSize if configured
            if (request.maxPoolSize > 0)
            {
                int remaining = Mathf.Max(0, request.maxPoolSize - TotalCount);
                count = Mathf.Min(count, remaining);
            }

            int created = 0;
            for (int i = 0; i < count; i++)
            {
                var instance = CreateNewInstance();
                if (instance == null)
                    break;

                // Ensure inactive
                instance.SetActive(false);
                inactivePool.Push(instance);
                created++;
            }

            // Notify listeners that prewarm is complete (mirrors Pool<T>.PrewarmPool behaviour)
            if (created > 0)
            {
                PoolingEvents.PublishPoolPrewarmed(poolId, created);
            }
        }

        public void Clear()
        {
            while (inactivePool.Count > 0)
            {
                var instance = inactivePool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                    metricsTracker.RecordDestruction();
                }
            }

            metricsTracker.RecordCull();
        }

        public void ShrinkInactive(int targetInactive = 0)
        {
            targetInactive = Mathf.Max(0, targetInactive);

            while (inactivePool.Count > targetInactive)
            {
                var instance = inactivePool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                    metricsTracker.RecordDestruction();
                }
            }

            if (targetInactive == 0)
            {
                metricsTracker.RecordCull();
            }
        }

        public void DestroyPool()
        {
            PoolLog.Info($"Pool '{poolId}': Destroying pool with {Capacity} total objects");

            while (inactivePool.Count > 0)
            {
                var instance = inactivePool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            activeCount = 0;
            PoolLog.Debug($"Pool '{poolId}': Pool destroyed");
        }

        public void Reseed(bool rePrewarm = true)
        {
            PoolLog.Info($"Pool '{poolId}': Reseed requested (rePrewarm={rePrewarm})");

            // 1. Force-despawn every active instance by walking PooledMarkers in the scene.
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

        public bool ContainsObject(GameObject instance)
        {
            if (instance == null)
                return false;

            return instance.TryGetComponent(out PooledMarker marker) && marker.ParentPool == this;
        }

        void IPoolInternal.NotifyObjectDestroyed(GameObject instance)
        {
            if (instance == null)
                return;

            if (instance.TryGetComponent(out PooledMarker marker))
            {
                if (marker.IsSpawnedFromPool)
                {
                    activeCount = Mathf.Max(0, activeCount - 1);
                    PoolLog.Debug($"Pool '{poolId}': Cleaned up externally destroyed active object '{instance.name}'");
                }
            }
        }

        private GameObject CreateNewInstance()
        {
            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab, poolParent);
                instance.name = $"{prefab.name}(Pool:{poolId})";

                // Ensure marker exists and bind pool
                var marker = instance.GetComponent<PooledMarker>() ?? instance.AddComponent<PooledMarker>();
                marker.ParentPool = this;
                marker.IsSpawnedFromPool = false;

                // Cache poolable (if present) for fast path
                var poolable = instance.GetComponent<IPoolable>();
                marker.CachedPoolableComponent = poolable as Component;

                if (poolable != null)
                {
                    poolable.ParentPool = this;
                    try
                    {
                        poolable.PoolReset();
                    }
                    catch (Exception e)
                    {
                        PoolLog.Warn($"Pool '{poolId}': PoolReset threw for '{instance.name}': {e.Message}");
                    }
                }

                metricsTracker.RecordCreation();
                PoolingEvents.PublishObjectCreated(instance, poolId);

                return instance;
            }
            catch (Exception e)
            {
                PoolLog.Error($"Pool '{poolId}': Failed to create instance from prefab '{prefab.name}': {e}");
                return null;
            }
        }

        public override string ToString()
        {
            return $"GameObjectPool '{poolId}' {{ Active: {ActiveCount}, Inactive: {InactiveCount}, Metrics: {Metrics} }}";
        }
    }
}
