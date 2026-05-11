// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using System.Collections.Concurrent;
using NUnit.Framework;
using UnityEngine;

namespace PoolMaster.Tests
{
    /// <summary>
    /// Tests for PoolCommandBuffer - thread-safe command enqueueing.
    /// Uses a fake pool to verify behavior without Unity objects.
    /// </summary>
    public class PoolCommandBufferTests
    {
        private PoolCommandBuffer buffer;
        private FakePool fakePool;

        [SetUp]
        public void Setup()
        {
            buffer = new PoolCommandBuffer();
            fakePool = new FakePool();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up any GameObjects spawned by FakePool
            fakePool?.DestroySpawnedObjects();
        }

        #region Enqueue Tests

        [Test]
        public void EnqueueSpawn_IncreasesPendingCount()
        {
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);

            Assert.AreEqual(1, buffer.PendingSpawnCount);
            Assert.IsTrue(buffer.HasPendingOperations);
        }

        [Test]
        public void EnqueueReturn_IncreasesPendingCount()
        {
            var obj = new GameObject("Test");
            buffer.EnqueueReturn(obj);

            Assert.AreEqual(1, buffer.PendingReturnCount);
            Assert.IsTrue(buffer.HasPendingOperations);

            Object.DestroyImmediate(obj);
        }

        [Test]
        public void EnqueueSpawnBatch_IncreasesBatchCount()
        {
            var positions = new Vector3[] { Vector3.zero, Vector3.one };
            var rotations = new Quaternion[] { Quaternion.identity, Quaternion.identity };

            buffer.EnqueueSpawnBatch(positions, rotations);

            Assert.AreEqual(1, buffer.PendingBatchCount);
            Assert.IsTrue(buffer.HasPendingOperations);
        }

        [Test]
        public void EnqueueSpawnBatch_NullPositions_DoesNotEnqueue()
        {
            buffer.EnqueueSpawnBatch(null, null);

            Assert.AreEqual(0, buffer.PendingBatchCount);
            Assert.IsFalse(buffer.HasPendingOperations);
        }

        [Test]
        public void EnqueueSpawnBatch_EmptyPositions_DoesNotEnqueue()
        {
            buffer.EnqueueSpawnBatch(new Vector3[0], new Quaternion[0]);

            Assert.AreEqual(0, buffer.PendingBatchCount);
            Assert.IsFalse(buffer.HasPendingOperations);
        }

        [Test]
        public void EnqueueReturn_NullObject_DoesNotEnqueue()
        {
            buffer.EnqueueReturn(null);

            Assert.AreEqual(0, buffer.PendingReturnCount);
        }

        #endregion

        #region HasPendingOperations Tests

        [Test]
        public void HasPendingOperations_InitiallyFalse()
        {
            Assert.IsFalse(buffer.HasPendingOperations);
        }

        [Test]
        public void HasPendingOperations_TrueWithSpawns()
        {
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);
            Assert.IsTrue(buffer.HasPendingOperations);
        }

        [Test]
        public void HasPendingOperations_TrueWithReturns()
        {
            var obj = new GameObject("TestObj");
            buffer.EnqueueReturn(obj);
            Assert.IsTrue(buffer.HasPendingOperations);
            Object.DestroyImmediate(obj);
        }

        [Test]
        public void HasPendingOperations_TrueWithBatches()
        {
            buffer.EnqueueSpawnBatch(new[] { Vector3.zero }, new[] { Quaternion.identity });
            Assert.IsTrue(buffer.HasPendingOperations);
        }

        #endregion

        #region Flush Tests

        [Test]
        public void FlushTo_ProcessesSpawnCommands()
        {
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);
            buffer.EnqueueSpawn(Vector3.one, Quaternion.identity);

            int processed = buffer.FlushTo(fakePool);

            Assert.AreEqual(2, processed);
            Assert.AreEqual(2, fakePool.SpawnCallCount);
            Assert.AreEqual(0, buffer.PendingSpawnCount);
        }

        [Test]
        public void FlushTo_ProcessesReturnCommands()
        {
            var obj1 = new GameObject();
            var obj2 = new GameObject();

            buffer.EnqueueReturn(obj1);
            buffer.EnqueueReturn(obj2);

            int processed = buffer.FlushTo(fakePool);

            Assert.AreEqual(2, processed);
            Assert.AreEqual(2, fakePool.DespawnCallCount);
            Assert.AreEqual(0, buffer.PendingReturnCount);

            Object.DestroyImmediate(obj1);
            Object.DestroyImmediate(obj2);
        }

        [Test]
        public void FlushTo_ProcessesBatchCommands()
        {
            var positions = new Vector3[] { Vector3.zero, Vector3.one, Vector3.up };

            buffer.EnqueueSpawnBatch(positions, null);

            int processed = buffer.FlushTo(fakePool);

            Assert.AreEqual(3, processed); // Batch returned 3
            // SpawnBatch is an extension method that calls Spawn() internally
            Assert.AreEqual(3, fakePool.SpawnCallCount); // 3 Spawn() calls from extension method
            Assert.AreEqual(0, buffer.PendingBatchCount);
        }

        [Test]
        public void FlushTo_ProcessesReturnsFirst()
        {
            // Order: returns should be processed before spawns
            var obj = new GameObject();
            buffer.EnqueueReturn(obj);
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);

            buffer.FlushTo(fakePool);

            // Check order: despawn called before spawn
            Assert.AreEqual(1, fakePool.DespawnCallCount);
            Assert.AreEqual(1, fakePool.SpawnCallCount);
            Assert.That(fakePool.CallOrder[0], Does.Contain("Despawn"));
            Assert.That(fakePool.CallOrder[1], Does.Contain("Spawn"));

            Object.DestroyImmediate(obj);
        }

        [Test]
        public void FlushTo_NullPool_ReturnsZero()
        {
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);

            int processed = buffer.FlushTo(null);

            Assert.AreEqual(0, processed);
        }

        [Test]
        public void Clear_RemovesAllPendingOperations()
        {
            var obj = new GameObject("ToClear");
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);
            buffer.EnqueueReturn(obj);
            buffer.EnqueueSpawnBatch(new[] { Vector3.zero }, null);

            buffer.Clear();

            Assert.IsFalse(buffer.HasPendingOperations);
            Assert.AreEqual(0, buffer.PendingSpawnCount);
            Assert.AreEqual(0, buffer.PendingReturnCount);
            Assert.AreEqual(0, buffer.PendingBatchCount);

            Object.DestroyImmediate(obj);
        }

        [Test]
        public void TotalPendingCount_SumsAllQueues()
        {
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);
            buffer.EnqueueSpawn(Vector3.one, Quaternion.identity);

            var obj = new GameObject();
            buffer.EnqueueReturn(obj);

            buffer.EnqueueSpawnBatch(new[] { Vector3.zero }, null);

            Assert.AreEqual(4, buffer.TotalPendingCount); // 2 spawns + 1 return + 1 batch

            Object.DestroyImmediate(obj);
        }

        #endregion

        #region ToString Test

        [Test]
        public void ToString_ContainsCounts()
        {
            var obj = new GameObject("TestToString");
            buffer.EnqueueSpawn(Vector3.zero, Quaternion.identity);
            buffer.EnqueueReturn(obj);

            var str = buffer.ToString();

            Assert.That(str, Does.Contain("Spawns:"));
            Assert.That(str, Does.Contain("Returns:"));

            Object.DestroyImmediate(obj);
        }

        #endregion
    }

    /// <summary>
    /// Fake pool for testing command buffer without Unity dependencies.
    /// </summary>
    internal class FakePool : IPoolControl
    {
        public int SpawnCallCount { get; private set; }
        public int DespawnCallCount { get; private set; }
        public int BatchSpawnCallCount { get; private set; }
        public System.Collections.Generic.List<string> CallOrder { get; } =
            new System.Collections.Generic.List<string>();
        private System.Collections.Generic.List<GameObject> spawnedObjects =
            new System.Collections.Generic.List<GameObject>();

        public GameObject Spawn()
        {
            SpawnCallCount++;
            CallOrder.Add("Spawn()");
            var obj = new GameObject("FakePooled");
            spawnedObjects.Add(obj);
            return obj;
        }

        public GameObject Spawn(Vector3 position)
        {
            SpawnCallCount++;
            CallOrder.Add($"Spawn({position})");
            var obj = new GameObject("FakePooled");
            spawnedObjects.Add(obj);
            return obj;
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation)
        {
            SpawnCallCount++;
            CallOrder.Add($"Spawn({position}, {rotation})");
            var obj = new GameObject("FakePooled");
            spawnedObjects.Add(obj);
            return obj;
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent)
        {
            SpawnCallCount++;
            CallOrder.Add($"Spawn({position}, {rotation}, {parent})");
            var obj = new GameObject("FakePooled");
            spawnedObjects.Add(obj);
            return obj;
        }

        public bool Despawn(GameObject instance)
        {
            DespawnCallCount++;
            CallOrder.Add($"Despawn({instance?.name})");
            return true;
        }

        // Batch spawn returns count
        public int SpawnBatch(Vector3[] positions, Quaternion[] rotations, Transform parent)
        {
            BatchSpawnCallCount++;
            CallOrder.Add($"SpawnBatch(count={positions?.Length})");
            return positions?.Length ?? 0;
        }

        // IPool interface
        public void ReturnToPool(GameObject obj) => Despawn(obj);

        public GameObject Prefab => null;
        public string PoolId => "FakePool";
        public int ActiveCount => 0;
        public int InactiveCount => 0;
        public int Capacity => 0;

        // IPoolControl interface
        public int TotalCount => 0;
        public PoolMetrics Metrics => default;

        public bool TrySpawn(out GameObject instance)
        {
            instance = Spawn();
            return true;
        }

        public bool TrySpawn(Vector3 position, out GameObject instance)
        {
            instance = Spawn(position);
            return true;
        }

        public bool TrySpawn(Vector3 position, Quaternion rotation, out GameObject instance)
        {
            instance = Spawn(position, rotation);
            return true;
        }

        public bool TrySpawn(
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out GameObject instance
        )
        {
            instance = Spawn(position, rotation, parent);
            return true;
        }

        public void PrewarmPool(int count) { }

        public void Clear() { }

        public void ShrinkInactive(int targetInactive = 0) { }

        public void DestroyPool() { }

        public void Reseed(bool rePrewarm = true) { }

        public bool ContainsObject(GameObject instance) => false;

        public void DestroySpawnedObjects()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            spawnedObjects.Clear();
        }
    }
}
