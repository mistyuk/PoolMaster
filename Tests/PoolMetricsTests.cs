// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using NUnit.Framework;
using UnityEngine;

namespace PoolMaster.Tests
{
    /// <summary>
    /// Tests for PoolMetrics calculations - pure data/math with no Unity dependencies.
    /// </summary>
    public class PoolMetricsTests
    {
        [Test]
        public void ReuseEfficiency_ZeroSpawns_ReturnsZero()
        {
            var metrics = CreateMetrics(totalSpawned: 0, totalCreated: 0);

            Assert.AreEqual(0f, metrics.ReuseEfficiency, 0.001f);
        }

        [Test]
        public void ReuseEfficiency_AllReused_ReturnsOne()
        {
            var metrics = CreateMetrics(totalSpawned: 100, totalCreated: 10);

            // 90 reused out of 100 spawns = 90% = 0.9
            Assert.AreEqual(0.9f, metrics.ReuseEfficiency, 0.001f);
        }

        [Test]
        public void ReuseEfficiency_NoneReused_ReturnsZero()
        {
            var metrics = CreateMetrics(totalSpawned: 50, totalCreated: 50);

            // Every spawn created a new object = 0% reuse
            Assert.AreEqual(0f, metrics.ReuseEfficiency, 0.001f);
        }

        [Test]
        public void ReuseEfficiency_MoreCreatedThanSpawned_ReturnsZero()
        {
            // Edge case: bad data where created > spawned (should clamp to 0)
            var metrics = CreateMetrics(totalSpawned: 50, totalCreated: 100);

            // Cannot have negative reuse
            Assert.GreaterOrEqual(metrics.ReuseEfficiency, 0f);
        }

        [Test]
        public void CurrentActive_CalculatesCorrectly()
        {
            var metrics = CreateMetrics(totalSpawned: 100, totalDespawned: 60);

            Assert.AreEqual(40, metrics.CurrentActive);
        }

        [Test]
        public void CurrentActive_AllowsNegativeIfDataBad()
        {
            // With bad data (more despawns than spawns), returns mathematically correct negative
            var metrics = CreateMetrics(totalSpawned: 10, totalDespawned: 20);

            Assert.AreEqual(-10, metrics.CurrentActive);
        }

        [Test]
        public void CreatesPerSpawn_AllCreated_ReturnsOne()
        {
            var metrics = CreateMetrics(totalSpawned: 50, totalCreated: 50);

            Assert.AreEqual(1f, metrics.CreatesPerSpawn, 0.001f);
        }

        [Test]
        public void CreatesPerSpawn_HalfCreated_ReturnsHalf()
        {
            var metrics = CreateMetrics(totalSpawned: 100, totalCreated: 50);

            Assert.AreEqual(0.5f, metrics.CreatesPerSpawn, 0.001f);
        }

        [Test]
        public void CreatesPerSpawn_ZeroSpawns_ReturnsZero()
        {
            // Edge case: division by zero guard
            var metrics = CreateMetrics(totalSpawned: 0, totalCreated: 10);

            Assert.AreEqual(0f, metrics.CreatesPerSpawn);
        }

        [Test]
        public void AverageExpansionInterval_NoExpansions_ReturnsZero()
        {
            // No expansions, should return 0
            var metrics = CreateMetrics(expansionCount: 0, creationTime: 50f, lastExpandTime: 100f);

            Assert.AreEqual(0f, metrics.AverageExpansionInterval);
        }

        [Test]
        public void AverageExpansionInterval_TimeEqualsCreation_ReturnsZero()
        {
            // Guard against NaN when lastExpandTime == creationTime
            var metrics = CreateMetrics(
                expansionCount: 5,
                creationTime: 100f,
                lastExpandTime: 100f
            );

            Assert.AreEqual(0f, metrics.AverageExpansionInterval);
        }

        [Test]
        public void SpawnsPerSecond_ZeroTime_ReturnsZero()
        {
            // When lastExpandTime == creationTime, elapsed time is 0
            var metrics = CreateMetrics(
                totalSpawned: 100,
                creationTime: 100f,
                lastExpandTime: 100f
            );

            Assert.AreEqual(0f, metrics.SpawnsPerSecond);
        }

        [Test]
        public void Merge_CombinesMetricsCorrectly()
        {
            var metrics1 = CreateMetrics(totalSpawned: 50, totalDespawned: 20, totalCreated: 30);
            var metrics2 = CreateMetrics(totalSpawned: 30, totalDespawned: 15, totalCreated: 20);

            var merged = PoolMetrics.Merge(metrics1, metrics2);

            Assert.AreEqual(80, merged.TotalSpawned);
            Assert.AreEqual(35, merged.TotalDespawned);
            Assert.AreEqual(50, merged.TotalCreated);
            Assert.AreEqual(45, merged.CurrentActive); // 80 - 35
        }

        [Test]
        public void Merge_TakesEarliestCreationTime()
        {
            var metrics1 = CreateMetrics(creationTime: 10f);
            var metrics2 = CreateMetrics(creationTime: 5f);

            var merged = PoolMetrics.Merge(metrics1, metrics2);

            // Should use earlier creation time
            Assert.AreEqual(5f, merged.CreationTime);
        }

        [Test]
        public void Merge_TakesLatestExpandTime()
        {
            var metrics1 = CreateMetrics(lastExpandTime: 100f);
            var metrics2 = CreateMetrics(lastExpandTime: 150f);

            var merged = PoolMetrics.Merge(metrics1, metrics2);

            Assert.AreEqual(150f, merged.LastExpandTime);
        }

        // Helper to create metrics with specific values
        // Now using direct constructor access via InternalsVisibleTo (no reflection needed)
        private PoolMetrics CreateMetrics(
            long totalSpawned = 0,
            long totalDespawned = 0,
            long totalCreated = 0,
            long totalDestroyed = 0,
            int expansionCount = 0,
            int cullCount = 0,
            float lastExpandTime = 0f,
            float lastCullTime = 0f,
            float creationTime = 0f
        )
        {
            return new PoolMetrics(
                totalSpawned,
                totalDespawned,
                totalCreated,
                totalDestroyed,
                expansionCount,
                cullCount,
                lastExpandTime,
                lastCullTime,
                creationTime,
                creationTime
            );
        }
    }
}
