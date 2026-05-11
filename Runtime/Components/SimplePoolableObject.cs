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
    /// Simple poolable object for testing and basic use cases.
    /// </summary>
    public class SimplePoolableObject : PoolableMonoBehaviour
    {
        [Header("Simple Poolable Settings")]
        [SerializeField]
        private float lifetime = 5f;

        [SerializeField]
        private bool autoDestroy = true;

        private float spawnTime;

        public override void OnSpawned()
        {
            base.OnSpawned();
            spawnTime = Time.time;

            // Optional: Add some simple behavior
            if (autoDestroy && lifetime > 0)
            {
                Invoke(nameof(ReturnToPool), lifetime);
            }
        }

        public override void OnDespawned()
        {
            CancelInvoke();
            base.OnDespawned();
        }

        private void ReturnToPool()
        {
            if (IsPooled)
            {
                ParentPool?.Despawn(gameObject);
            }
        }

        // Optional: Show lifetime in inspector during play
        void OnDrawGizmos()
        {
            if (Application.isPlaying && IsPooled && lifetime > 0)
            {
                float elapsed = Time.time - spawnTime;
                float remaining = lifetime - elapsed;

                if (remaining > 0)
                {
                    Gizmos.color = Color.Lerp(Color.red, Color.green, remaining / lifetime);
                    Gizmos.DrawWireSphere(transform.position, 0.2f);
                }
            }
        }
    }
}
