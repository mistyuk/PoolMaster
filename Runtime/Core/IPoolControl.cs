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
    /// Defines advanced pool operations for pool managers and systems.
    /// Extends IPool with additional management, control, and diagnostic capabilities.
    /// </summary>
    public interface IPoolControl : IPool
    {
        /// <summary>
        /// Gets the total capacity (active + inactive objects) of this pool.
        /// </summary>
        int TotalCount { get; }

        /// <summary>
        /// Gets a read-only snapshot of current pool metrics.
        /// </summary>
        PoolMetrics Metrics { get; }

        /// <summary>
        /// Spawns an object at the origin with default rotation.
        /// </summary>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        GameObject Spawn();

        /// <summary>
        /// Spawns an object at the specified position with default rotation.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        GameObject Spawn(Vector3 position);

        /// <summary>
        /// Spawns an object at the specified position and rotation.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        GameObject Spawn(Vector3 position, Quaternion rotation);

        /// <summary>
        /// Spawns an object at the specified position, rotation, and parent.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        /// <param name="parent">Parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawn failed.</returns>
        GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent);

        /// <summary>
        /// Attempts to spawn an object at the origin with default rotation.
        /// </summary>
        /// <param name="instance">The spawned GameObject if successful.</param>
        /// <returns>True if spawn succeeded, false otherwise.</returns>
        bool TrySpawn(out GameObject instance);

        /// <summary>
        /// Try to spawn an object from this pool at a specific position.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        bool TrySpawn(Vector3 position, out GameObject instance);

        /// <summary>
        /// Try to spawn an object from this pool at a specific position and rotation.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        bool TrySpawn(Vector3 position, Quaternion rotation, out GameObject instance);

        /// <summary>
        /// Try to spawn an object from this pool at a specific position, rotation, and parent.
        /// Avoids null checks and allocations at call-sites.
        /// </summary>
        /// <param name="position">World position for the spawned object</param>
        /// <param name="rotation">World rotation for the spawned object</param>
        /// <param name="parent">Parent transform for the spawned object</param>
        /// <param name="instance">The spawned GameObject if successful</param>
        /// <returns>True if spawn succeeded, false otherwise</returns>
        bool TrySpawn(
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out GameObject instance
        );

        /// <summary>
        /// Pre-warms the pool by creating inactive objects to avoid runtime instantiation hitches.
        /// </summary>
        /// <param name="count">Number of objects to create.</param>
        void PrewarmPool(int count);

        /// <summary>
        /// Clears all inactive objects from the pool. Active objects are not affected.
        /// </summary>
        void Clear();

        /// <summary>
        /// Trims the inactive cache to a target size. Useful for managing memory during scene transitions.
        /// </summary>
        /// <param name="targetInactive">Target number of inactive objects to keep (default: 0).</param>
        void ShrinkInactive(int targetInactive = 0);

        /// <summary>
        /// Destroys the pool and all its objects (active and inactive). Do not use this pool after calling this method.
        /// </summary>
        void DestroyPool();

        /// <summary>
        /// Flushes and rebuilds the pool. Force-despawns every active instance, destroys
        /// all inactive instances, and optionally re-prewarms back to the original initial
        /// size. Use this when the prefab has been edited at runtime — existing pooled
        /// instances carry stale state and need to be replaced with fresh clones from the
        /// updated prefab.
        /// </summary>
        /// <param name="rePrewarm">If true and the original request had shouldPrewarm=true with initialPoolSize&gt;0, re-prewarm to that size after flushing.</param>
        void Reseed(bool rePrewarm = true);

        /// <summary>
        /// Checks whether a GameObject belongs to this pool.
        /// </summary>
        /// <param name="instance">The GameObject to check.</param>
        /// <returns>True if the object belongs to this pool, false otherwise.</returns>
        bool ContainsObject(GameObject instance);
    }
}
