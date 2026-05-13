// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using PoolMaster;

namespace PoolMaster.Tests
{
    /// <summary>
    /// Regression tests for the v1.0.2 – v1.0.5 bug fixes. Each test name maps to
    /// the version that introduced the fix it guards. EditMode-only — none of
    /// these need a running scene; they construct pools directly and inspect state.
    /// </summary>
    [TestFixture]
    public class PoolBehaviorTests
    {
        private GameObject _prefab;
        private GameObject _plainPrefab;
        private Transform _poolParent;

        [SetUp]
        public void SetUp()
        {
            // Prefab with an IPoolable component (PoolableMonoBehaviour subclass).
            _prefab = new GameObject("TestPrefab");
            _prefab.AddComponent<SimplePoolableObject>();
            _prefab.SetActive(false);

            // Plain prefab — no IPoolable / pooling component at all.
            _plainPrefab = new GameObject("PlainPrefab");
            _plainPrefab.SetActive(false);

            _poolParent = new GameObject("TestPoolParent").transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null) Object.DestroyImmediate(_prefab);
            if (_plainPrefab != null) Object.DestroyImmediate(_plainPrefab);
            if (_poolParent != null) Object.DestroyImmediate(_poolParent.gameObject);
        }

        // ── v1.0.4: Pool<T> honors request.poolId / poolGuid ──────────────

        [Test]
        public void Pool_HonorsRequestPoolId()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            request.poolId = "MyExplicitId";

            var pool = new Pool<SimplePoolableObject>(_prefab, request, _poolParent);

            Assert.AreEqual("MyExplicitId", pool.PoolId,
                "Pool<T> must honor request.poolId — regressing this would make GetPool(yourId) silently fail.");
        }

        [Test]
        public void Pool_PoolGuidWinsOverPoolId()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            request.poolId = "RegularId";
            request.poolGuid = "guid-takes-precedence";

            var pool = new Pool<SimplePoolableObject>(_prefab, request, _poolParent);

            Assert.AreEqual("guid-takes-precedence", pool.PoolId,
                "request.poolGuid must win over request.poolId.");
        }

        [Test]
        public void Pool_CtorPoolIdParamWinsOverRequestPoolId()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            request.poolId = "RequestId";

            var pool = new Pool<SimplePoolableObject>(_prefab, request, _poolParent, poolId: "CtorId");

            Assert.AreEqual("CtorId", pool.PoolId,
                "Explicit ctor poolId must win over request.poolId (but lose to poolGuid).");
        }

        [Test]
        public void GameObjectPool_HonorsRequestPoolId()
        {
            var request = PoolRequest.Create(_plainPrefab, initialSize: 0, shouldPrewarm: false);
            request.poolId = "PlainPoolId";

            var pool = new GameObjectPool(_plainPrefab, request, _poolParent);

            Assert.AreEqual("PlainPoolId", pool.PoolId);
        }

        // ── v1.0.3: usePoolContainer creates a named child under poolParent ──

        [Test]
        public void Pool_UsePoolContainer_CreatesNamedChildUnderPoolParent()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            request.usePoolContainer = true;
            request.containerName = "MyContainer";

            var pool = new Pool<SimplePoolableObject>(_prefab, request, _poolParent);

            // Find the container under poolParent.
            Transform container = null;
            for (int i = 0; i < _poolParent.childCount; i++)
            {
                if (_poolParent.GetChild(i).name == "MyContainer")
                {
                    container = _poolParent.GetChild(i);
                    break;
                }
            }
            Assert.IsNotNull(container,
                "usePoolContainer=true must create a child named containerName under the supplied poolParent.");
        }

        [Test]
        public void GameObjectPool_UsePoolContainer_CreatesNamedChildUnderPoolParent()
        {
            var request = PoolRequest.Create(_plainPrefab, initialSize: 0, shouldPrewarm: false);
            request.usePoolContainer = true;
            request.containerName = "PlainContainer";

            var pool = new GameObjectPool(_plainPrefab, request, _poolParent);

            Transform container = null;
            for (int i = 0; i < _poolParent.childCount; i++)
            {
                if (_poolParent.GetChild(i).name == "PlainContainer")
                {
                    container = _poolParent.GetChild(i);
                    break;
                }
            }
            Assert.IsNotNull(container);
        }

        [Test]
        public void Pool_UsePoolContainerFalse_DoesNotCreateContainer()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            request.usePoolContainer = false;
            int childCountBefore = _poolParent.childCount;

            var pool = new Pool<SimplePoolableObject>(_prefab, request, _poolParent);

            Assert.AreEqual(childCountBefore, _poolParent.childCount,
                "With usePoolContainer=false the pool must not create any container under poolParent.");
        }

        // ── v1.0.2/v1.0.4: GameObjectPool can pool a plain (non-IPoolable) prefab ──

        [Test]
        public void GameObjectPool_NonIPoolablePrefab_SpawnsAndDespawns()
        {
            var request = PoolRequest.Create(_plainPrefab, initialSize: 0, shouldPrewarm: false);
            request.usePoolContainer = false;

            var pool = new GameObjectPool(_plainPrefab, request, _poolParent);

            var spawned = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            Assert.IsNotNull(spawned, "Spawn must return a non-null instance for plain prefabs.");
            Assert.AreEqual(1, pool.ActiveCount);
            Assert.IsTrue(spawned.activeInHierarchy);

            bool returned = pool.Despawn(spawned);
            Assert.IsTrue(returned, "Despawn must succeed for an instance this pool spawned.");
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, pool.InactiveCount);
            Assert.IsFalse(spawned.activeInHierarchy);

            // Cleanup the spawned instance the pool destroyed reference to.
            if (spawned != null) Object.DestroyImmediate(spawned);
        }

        // ── v1.0.2: Despawn must not destroy when cullExcessObjects=false ──

        [Test]
        public void GameObjectPool_Despawn_CullDisabled_KeepsExcessInstances()
        {
            var request = PoolRequest.Create(_plainPrefab, initialSize: 0, shouldPrewarm: false);
            request.maxPoolSize = 2;
            request.cullExcessObjects = false;
            request.allowDynamicExpansion = true;

            var pool = new GameObjectPool(_plainPrefab, request, _poolParent);

            var a = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            var b = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            var c = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            Assert.AreEqual(3, pool.ActiveCount);

            pool.Despawn(a);
            pool.Despawn(b);
            pool.Despawn(c);

            // All three should be in inactive (no destruction) because cullExcessObjects=false.
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(3, pool.InactiveCount,
                "With cullExcessObjects=false the despawned instances must be cached, even past maxPoolSize.");

            // Manual cleanup (pool keeps strong refs but they were instantiated by the pool).
            pool.DestroyPool();
        }

        // ── v1.0.2: When cullExcessObjects=true, despawn over capacity destroys ──

        [Test]
        public void GameObjectPool_Despawn_CullEnabled_DestroysOverflow()
        {
            var request = PoolRequest.Create(_plainPrefab, initialSize: 0, shouldPrewarm: false);
            request.maxPoolSize = 1;
            request.cullExcessObjects = true;
            request.allowDynamicExpansion = true;

            var pool = new GameObjectPool(_plainPrefab, request, _poolParent);

            var a = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            var b = pool.Spawn(Vector3.zero, Quaternion.identity, null);

            pool.Despawn(a);
            // Pool now has 1 inactive (a). InactiveCount == maxPoolSize.
            pool.Despawn(b);
            // b should be destroyed, not cached.

            Assert.AreEqual(1, pool.InactiveCount,
                "With cullExcessObjects=true and inactive at maxPoolSize, additional despawns must destroy rather than push.");

            pool.DestroyPool();
        }

        // ── v1.0.5: GameObjectPool.Despawn invokes PoolReset on IPoolable instances ──

        private class TestPoolableTracker : MonoBehaviour, IPoolable
        {
            public int PoolResetCallCount;
            public int OnSpawnedCallCount;
            public int OnDespawnedCallCount;
            public IPool ParentPool { get; set; }
            public bool IsPooled { get; private set; }

            public void OnSpawned() { OnSpawnedCallCount++; IsPooled = true; }
            public void OnDespawned() { OnDespawnedCallCount++; IsPooled = false; }
            public void PoolReset() { PoolResetCallCount++; }
        }

        [Test]
        public void GameObjectPool_DespawnCallsPoolResetOnIPoolable()
        {
            var prefab = new GameObject("TrackerPrefab");
            prefab.AddComponent<TestPoolableTracker>();
            prefab.SetActive(false);

            var request = PoolRequest.Create(prefab, initialSize: 0, shouldPrewarm: false);
            request.usePoolContainer = false;
            var pool = new GameObjectPool(prefab, request, _poolParent);

            var spawned = pool.Spawn(Vector3.zero, Quaternion.identity, null);
            var tracker = spawned.GetComponent<TestPoolableTracker>();
            int resetCountAfterCreate = tracker.PoolResetCallCount; // CreateNewInstance calls PoolReset once

            pool.Despawn(spawned);

            Assert.Greater(tracker.PoolResetCallCount, resetCountAfterCreate,
                "GameObjectPool.Despawn must invoke PoolReset on the IPoolable. Regressing this " +
                "would silently skip resetTransformOnDespawn / sleepRigidbodiesOnDespawn / etc. " +
                "for IPoolable prefabs pooled via GameObjectPool.");
            Assert.AreEqual(1, tracker.OnDespawnedCallCount, "OnDespawned must also fire exactly once.");

            pool.DestroyPool();
            Object.DestroyImmediate(prefab);
        }

        // ── v1.0.4: enableDebugLogging is properly marked [Obsolete] ──

#pragma warning disable CS0618 // Test reads the obsolete field intentionally
        [Test]
        public void EnableDebugLogging_IsObsolete_ButStillSerializable()
        {
            var request = PoolRequest.Create(_prefab, initialSize: 0, shouldPrewarm: false);
            Assert.IsFalse(request.enableDebugLogging,
                "Factory default for the obsolete enableDebugLogging field must remain false for serialization compatibility.");
        }
#pragma warning restore CS0618
    }
}
#endif
