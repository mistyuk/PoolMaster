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
    /// Tests for PoolRequest - configuration validation and factory methods.
    /// </summary>
    public class PoolRequestTests
    {
        private GameObject testPrefab;

        [SetUp]
        public void Setup()
        {
            testPrefab = new GameObject("TestPrefab");
        }

        [TearDown]
        public void TearDown()
        {
            if (testPrefab != null)
                Object.DestroyImmediate(testPrefab);
        }

        [Test]
        public void Create_WithValidPrefab_ReturnsConfiguredRequest()
        {
            var request = PoolRequest.Create(testPrefab, 20, true);

            Assert.AreEqual(testPrefab, request.prefab);
            Assert.AreEqual(20, request.initialPoolSize);
            Assert.GreaterOrEqual(
                request.maxPoolSize,
                request.initialPoolSize,
                "maxPoolSize should be >= initialPoolSize"
            );
            Assert.IsTrue(request.shouldPrewarm);
            Assert.AreEqual(PoolInitializationTiming.Immediate, request.initializationTiming);
        }

        [Test]
        public void Create_WithNullPrefab_ReturnsDefault()
        {
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "PoolRequest.Create: prefab cannot be null!"
            );

            var request = PoolRequest.Create(null);

            Assert.AreEqual(default(PoolRequest), request);
        }

        [Test]
        public void Create_WithNegativeSize_ClampsToZero()
        {
            // Logging will warn but should clamp to 0
            var request = PoolRequest.Create(testPrefab, -5);

            Assert.AreEqual(0, request.initialPoolSize);
        }

        [Test]
        public void CreateHighPerformance_SetsCorrectDefaults()
        {
            var request = PoolRequest.CreateHighPerformance(testPrefab, 50, 200);

            Assert.AreEqual(50, request.initialPoolSize);
            Assert.AreEqual(200, request.maxPoolSize);
            Assert.GreaterOrEqual(
                request.maxPoolSize,
                request.initialPoolSize,
                "maxPoolSize should be >= initialPoolSize"
            );
            Assert.AreEqual(PoolInitializationTiming.OnAwake, request.initializationTiming);
            Assert.IsTrue(request.allowDynamicExpansion);
            Assert.IsTrue(request.cullExcessObjects);
#pragma warning disable CS0618
            Assert.IsFalse(request.enableDebugLogging); // Currently a no-op but factory still defaults to false
#pragma warning restore CS0618
        }

        [Test]
        public void CreateHighPerformance_InitialExceedsMax_ClampsInitial()
        {
            // Should clamp initial to max when initial > max
            var request = PoolRequest.CreateHighPerformance(testPrefab, 300, 200);

            Assert.LessOrEqual(
                request.initialPoolSize,
                request.maxPoolSize,
                "initialPoolSize should be clamped to maxPoolSize"
            );
            Assert.LessOrEqual(
                request.initialPoolSize,
                200,
                "initialPoolSize should not exceed provided max"
            );
        }

        [Test]
        public void CreateMemoryEfficient_SetsLazyInitialization()
        {
            var request = PoolRequest.CreateMemoryEfficient(testPrefab);

            Assert.AreEqual(0, request.initialPoolSize);
            Assert.IsFalse(request.shouldPrewarm);
            Assert.AreEqual(PoolInitializationTiming.Lazy, request.initializationTiming);
            Assert.Greater(request.maxPoolSize, 0, "maxPoolSize should be positive");
            Assert.IsTrue(request.cullExcessObjects);
        }

        [Test]
        public void CreateForEvent_SetsEventTiming()
        {
            var request = PoolRequest.CreateForEvent(testPrefab, "LevelStart", 15);

            Assert.AreEqual(PoolInitializationTiming.OnEvent, request.initializationTiming);
            Assert.AreEqual("LevelStart", request.eventId);
            Assert.AreEqual(15, request.initialPoolSize);
        }

        [Test]
        public void PoolId_GeneratedFromPrefabName()
        {
            var request = PoolRequest.Create(testPrefab);

            Assert.That(request.poolId, Does.Contain("TestPrefab"));
            Assert.That(request.poolId, Does.Contain("Pool"));
        }

        [Test]
        public void UsePoolContainer_EnabledByDefault()
        {
            var request = PoolRequest.Create(testPrefab);

            Assert.IsTrue(request.usePoolContainer);
            Assert.IsNotEmpty(request.containerName);
        }

        [Test]
        public void AllowDynamicExpansion_EnabledForStandardPools()
        {
            var request = PoolRequest.Create(testPrefab);

            Assert.IsTrue(request.allowDynamicExpansion);
        }

        [Test]
        public void Category_SetCorrectlyByFactoryMethods()
        {
            var standard = PoolRequest.Create(testPrefab);
            var highPerf = PoolRequest.CreateHighPerformance(testPrefab);
            var memEfficient = PoolRequest.CreateMemoryEfficient(testPrefab);

            Assert.AreEqual("Default", standard.category);
            Assert.AreEqual("HighFrequency", highPerf.category);
            Assert.AreEqual("LowFrequency", memEfficient.category);
        }
    }
}
