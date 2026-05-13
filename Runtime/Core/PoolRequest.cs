// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using UnityEngine;

namespace PoolMaster
{
    /// <summary>
    /// Defines when a pool should be initialized and pre-warmed.
    /// </summary>
    public enum PoolInitializationTiming
    {
        /// <summary>
        /// Creates the pool on request but delays pre-warming until first use. Most memory-efficient option.
        /// </summary>
        Lazy = 0,

        /// <summary>
        /// Creates and pre-warms the pool immediately when requested. Prevents runtime hitches.
        /// </summary>
        Immediate = 1,

        /// <summary>
        /// Creates the pool immediately but defers pre-warming to the next frame. Balances responsiveness with avoiding hitches.
        /// </summary>
        NextFrame = 2,

        /// <summary>
        /// Creates and pre-warms the pool during Unity's Awake phase. Use for pools required before gameplay starts.
        /// </summary>
        OnAwake = 3,

        /// <summary>
        /// Creates and pre-warms the pool during Unity's Start phase. Use when dependencies must initialize first.
        /// </summary>
        OnStart = 4,

        /// <summary>
        /// Creates and pre-warms the pool when a specific event is triggered. Requires eventId to be specified.
        /// </summary>
        OnEvent = 5,
    }

    /// <summary>
    /// Configuration data for creating and managing an object pool.
    /// Lightweight and serializable for inspector integration.
    /// </summary>
    [System.Serializable]
    public struct PoolRequest
    {
        [Header("Required Settings")]
        [Tooltip("The prefab to pool. Must not be null.")]
        public GameObject prefab;

        [Tooltip(
            "Unique identifier for this pool. If empty, will be auto-generated from prefab name. For cross-scene determinism, use a GUID string."
        )]
        public string poolId;

        [Tooltip(
            "Optional GUID for deterministic pool identification across sessions. Takes priority over poolId if set."
        )]
        public string poolGuid;

        [Header("Pool Size Configuration")]
        [Tooltip("Number of objects to pre-instantiate when the pool is created.")]
        [Range(0, 1000)]
        public int initialPoolSize;

        [Tooltip("Maximum number of objects this pool can contain. 0 = unlimited.")]
        [Range(0, 10000)]
        public int maxPoolSize;

        [Tooltip("If true, objects will be pre-instantiated based on initialPoolSize.")]
        public bool shouldPrewarm;

        [Header("Pool Behavior")]
        [Tooltip("When this pool should be initialized and prewarmed.")]
        public PoolInitializationTiming initializationTiming;

        [Tooltip(
            "Event ID for OnEvent initialization timing. Only used when initializationTiming is OnEvent."
        )]
        public string eventId;

        [Tooltip("If true, the pool will automatically expand when empty (up to maxPoolSize).")]
        public bool allowDynamicExpansion;

        [Tooltip("If true, inactive objects will be destroyed when the pool exceeds maxPoolSize.")]
        public bool cullExcessObjects;

        [Header("Performance & Debugging")]
        [Tooltip("If true, pooled objects will be parented under a container for organization.")]
        public bool usePoolContainer;

        [Tooltip("Name for the pool container GameObject (if usePoolContainer is true).")]
        public string containerName;

        [Tooltip(
            "Currently inactive. PoolLog uses the compile-time ENABLE_POOL_LOGS define " +
            "for performance, so this per-pool flag is not consulted at runtime. May be " +
            "wired up to gate logging in a future release."
        )]
        [System.Obsolete(
            "enableDebugLogging is currently a no-op: PoolLog uses the compile-time " +
            "ENABLE_POOL_LOGS define rather than this per-pool flag. The field is kept " +
            "for serialization compatibility and may be wired up in a future release.",
            error: false)]
        public bool enableDebugLogging;

        [Tooltip(
            "Optional tag for categorizing pools (e.g., 'Projectiles', 'Effects', 'Enemies')."
        )]
        public string category;

        /// <summary>
        /// Creates a basic pool request with sensible defaults.
        /// </summary>
        /// <param name="prefab">The prefab to pool.</param>
        /// <param name="initialSize">Number of objects to pre-warm (default: 10).</param>
        /// <param name="shouldPrewarm">Whether to pre-warm the pool (default: true).</param>
        /// <returns>A configured PoolRequest ready for use.</returns>
        public static PoolRequest Create(
            GameObject prefab,
            int initialSize = 10,
            bool shouldPrewarm = true
        )
        {
            if (prefab == null)
            {
                Debug.LogError("PoolRequest.Create: prefab cannot be null!");
                return default;
            }

            // Ensure valid size constraints
            if (initialSize < 0)
            {
                Debug.LogWarning(
                    $"PoolRequest.Create: initialSize ({initialSize}) cannot be negative, setting to 0"
                );
                initialSize = 0;
            }

#pragma warning disable CS0618 // enableDebugLogging is obsolete; factory still sets the default for back-compat
            return new PoolRequest
            {
                prefab = prefab,
                poolId = $"{prefab.name}_Pool",
                initialPoolSize = initialSize,
                maxPoolSize = initialSize * 5, // Allow 5x expansion by default
                shouldPrewarm = shouldPrewarm,
                initializationTiming = PoolInitializationTiming.Immediate,
                allowDynamicExpansion = true,
                cullExcessObjects = false,
                usePoolContainer = true,
                containerName = $"{prefab.name} Pool Container",
                enableDebugLogging = false,
                category = "Default",
            };
#pragma warning restore CS0618
        }

        /// <summary>
        /// Creates a high-performance pool request optimized for frequently spawned objects.
        /// </summary>
        /// <param name="prefab">The prefab to pool</param>
        /// <param name="initialSize">Number of objects to prewarm</param>
        /// <param name="maxSize">Maximum pool size</param>
        /// <param name="category">Pool category for organization</param>
        /// <returns>A performance-optimized PoolRequest</returns>
        public static PoolRequest CreateHighPerformance(
            GameObject prefab,
            int initialSize = 50,
            int maxSize = 200,
            string category = "HighFrequency"
        )
        {
            // Validate size constraints
            if (maxSize > 0 && initialSize > maxSize)
            {
                Debug.LogWarning(
                    $"PoolRequest.CreateHighPerformance: initialSize ({initialSize}) exceeds maxPoolSize ({maxSize}), clamping to maxSize"
                );
                initialSize = maxSize;
            }

            var request = Create(prefab, initialSize, true);
            request.maxPoolSize = maxSize;
            request.initializationTiming = PoolInitializationTiming.OnAwake;
            request.allowDynamicExpansion = true;
            request.cullExcessObjects = true;
            request.category = category;
#pragma warning disable CS0618
            request.enableDebugLogging = false; // Currently a no-op; kept for back-compat
#pragma warning restore CS0618
            return request;
        }

        /// <summary>
        /// Creates a memory-efficient pool request for rarely used objects.
        /// </summary>
        /// <param name="prefab">The prefab to pool</param>
        /// <param name="category">Pool category for organization</param>
        /// <returns>A memory-optimized PoolRequest</returns>
        public static PoolRequest CreateMemoryEfficient(
            GameObject prefab,
            string category = "LowFrequency"
        )
        {
            var request = Create(prefab, 0, false); // No prewarming
            request.maxPoolSize = 20; // Small max size
            request.initializationTiming = PoolInitializationTiming.Lazy;
            request.allowDynamicExpansion = true;
            request.cullExcessObjects = true;
            request.category = category;
            return request;
        }

        /// <summary>
        /// Creates a pool request that will be bootstrapped when a specific event occurs.
        /// </summary>
        /// <param name="prefab">The prefab to pool</param>
        /// <param name="eventId">Event ID that triggers pool creation</param>
        /// <param name="initialSize">Number of objects to prewarm when event occurs</param>
        /// <param name="category">Pool category for organization</param>
        /// <returns>An event-triggered PoolRequest</returns>
        public static PoolRequest CreateForEvent(
            GameObject prefab,
            string eventId,
            int initialSize = 10,
            string category = "Event"
        )
        {
            var request = Create(prefab, initialSize, true);
            request.initializationTiming = PoolInitializationTiming.OnEvent;
            request.eventId = eventId;
            request.category = category;
            return request;
        }

        /// <summary>
        /// Validates that this pool request has valid configuration.
        /// </summary>
        /// <returns>True if the request is valid, false otherwise</returns>
        public bool IsValid()
        {
            if (prefab == null)
            {
                Debug.LogError("PoolRequest validation failed: prefab is null");
                return false;
            }

            if (initialPoolSize < 0)
            {
                Debug.LogError("PoolRequest validation failed: initialPoolSize cannot be negative");
                return false;
            }

            if (maxPoolSize > 0 && initialPoolSize > maxPoolSize)
            {
                Debug.LogError(
                    $"PoolRequest validation failed: initialPoolSize ({initialPoolSize}) cannot exceed maxPoolSize ({maxPoolSize})"
                );
                return false;
            }

            if (string.IsNullOrEmpty(poolId))
            {
                // Auto-generate pool ID if not provided
                return true; // This will be fixed during pool creation
            }

            return true;
        }

        /// <summary>
        /// Gets a sanitized pool ID, preferring GUID for deterministic identification.
        /// Priority: poolGuid > poolId > auto-generated from prefab
        /// </summary>
        /// <returns>A valid pool ID string</returns>
        public string GetPoolId()
        {
            // First priority: Use poolGuid if available (for cross-scene determinism)
            if (!string.IsNullOrEmpty(poolGuid))
            {
                return poolGuid;
            }

            // Second priority: Use explicit poolId if available
            if (!string.IsNullOrEmpty(poolId))
            {
                return poolId;
            }

            // Fallback: Auto-generate from prefab (includes hash for uniqueness in session)
            if (prefab != null)
            {
                return $"{prefab.name}_Pool_{prefab.GetHashCode()}";
            }

            // Last resort: Random ID
            return $"UnknownPool_{System.Guid.NewGuid().ToString("N")[..8]}";
        }

        /// <summary>
        /// Gets a display name for debugging and logging.
        /// </summary>
        /// <returns>A human-readable name for this pool</returns>
        public string GetDisplayName()
        {
            string baseName = prefab != null ? prefab.name : "Unknown";
            string categoryTag = !string.IsNullOrEmpty(category) ? $"[{category}]" : "";
            return $"{categoryTag} {baseName} Pool";
        }

        /// <summary>
        /// Returns a detailed string representation for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"PoolRequest {{ "
                + $"Prefab: {(prefab ? prefab.name : "null")}, "
                + $"ID: {GetPoolId()}, "
                + $"InitialSize: {initialPoolSize}, "
                + $"MaxSize: {maxPoolSize}, "
                + $"Prewarm: {shouldPrewarm}, "
                + $"Timing: {initializationTiming}, "
                + $"Category: {category} }}";
        }
    }
}
