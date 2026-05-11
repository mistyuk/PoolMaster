// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using System.Collections.Generic;
using NUnit.Framework;

namespace PoolMaster.Tests
{
    /// <summary>
    /// Tests for PoolSnapshot - global aggregation logic.
    /// </summary>
    public class PoolSnapshotTests
    {
        [Test]
        public void TotalObjects_SumsActiveAndInactive()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 2,
                totalActive: 50,
                totalInactive: 30,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            Assert.AreEqual(80, snapshot.TotalObjects);
        }

        [Test]
        public void GlobalUtilization_CalculatesCorrectly()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 1,
                totalActive: 40,
                totalInactive: 60,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            // 40 active / 100 total = 40%
            Assert.AreEqual(40f, snapshot.GlobalUtilization, 0.001f);
        }

        [Test]
        public void GlobalUtilization_ZeroObjects_ReturnsZero()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 0,
                totalActive: 0,
                totalInactive: 0,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            Assert.AreEqual(0f, snapshot.GlobalUtilization);
        }

        [Test]
        public void GlobalUtilization_AllActive_Returns100()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 1,
                totalActive: 100,
                totalInactive: 0,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            Assert.AreEqual(100f, snapshot.GlobalUtilization, 0.001f);
        }

        [Test]
        public void Single_CreatesSnapshotWithOnePool()
        {
            var metrics = CreateMetrics(totalSpawned: 50, totalDespawned: 10);

            var snapshot = PoolSnapshot.Single("TestPool", metrics, inactiveCount: 20);

            Assert.AreEqual(1, snapshot.TotalPools);
            Assert.AreEqual(40, snapshot.TotalActiveObjects); // 50 - 10
            Assert.AreEqual(20, snapshot.TotalInactiveObjects);
            Assert.AreEqual(1, snapshot.PoolBreakdown.Count);
            Assert.IsTrue(snapshot.PoolBreakdown.ContainsKey("TestPool"));
        }

        [Test]
        public void UtcCapturedAt_IsSet()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 0,
                totalActive: 0,
                totalInactive: 0,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            // Should be roughly current time (±15s for slow CI)
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.That(snapshot.UtcCapturedAt, Is.InRange(now - 15, now + 15));
        }

        [Test]
        public void PoolBreakdown_NullBecomesEmptyDictionary()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 0,
                totalActive: 0,
                totalInactive: 0,
                poolBreakdown: null // Pass null
            );

            Assert.IsNotNull(snapshot.PoolBreakdown);
            Assert.AreEqual(0, snapshot.PoolBreakdown.Count);
        }

        [Test]
        public void ToString_ContainsKeyMetrics()
        {
            var snapshot = new PoolSnapshot(
                totalPools: 3,
                totalActive: 100,
                totalInactive: 50,
                poolBreakdown: new Dictionary<string, PoolMetrics>()
            );

            var str = snapshot.ToString();

            Assert.That(str, Does.Contain("3 pools"));
            Assert.That(str, Does.Contain("Active=100"));
            Assert.That(str, Does.Contain("Inactive=50"));
            Assert.That(str, Does.Contain("Total=150"));
        }

        // Helper to create metrics (using InternalsVisibleTo - no reflection)
        private PoolMetrics CreateMetrics(
            long totalSpawned = 0,
            long totalDespawned = 0,
            long totalCreated = 0
        )
        {
            return new PoolMetrics(
                totalSpawned,
                totalDespawned,
                totalCreated,
                0L,
                0,
                0,
                0f,
                0f,
                0f,
                0f
            );
        }
    }
}
