// ============================================================================
// PoolMaster - Object Pooling System for Unity
// Copyright (c) 2026 Max Thomas Coates
// https://github.com/mistyuk/PoolMaster
// Licensed under MIT License (see LICENSE file for details)
// ============================================================================

using UnityEngine;

namespace PoolMaster.NoCode
{
    /// <summary>
    /// Automatically returns this object to the pool when certain conditions are met.
    /// Add this to your pooled prefab to make it return automatically.
    /// </summary>
    [AddComponentMenu("PoolMaster/PoolMaster Return To Pool")]
    public class PoolMasterReturnToPool : MonoBehaviour
    {
        [Header("When to Return")]
        [Tooltip("Conditions that trigger returning this object to the pool.")]
        [SerializeField]
        private ReturnCondition returnCondition = ReturnCondition.AfterTime;

        [Header("Time Settings")]
        [Tooltip("How many seconds before returning to pool (when using 'After Time').")]
        [SerializeField]
        private float lifetimeSeconds = 2f;

        [Header("Particle Settings")]
        [Tooltip("Stop particle systems when returning to pool.")]
        [SerializeField]
        private bool stopParticlesOnReturn = true;

        [Tooltip("Clear particle systems when returning to pool.")]
        [SerializeField]
        private bool clearParticlesOnReturn = true;

        [Header("Physics Settings")]
        [Tooltip("Reset Rigidbody velocity when returning to pool.")]
        [SerializeField]
        private bool resetRigidbodyOnReturn = true;

        [Tooltip("Reset Rigidbody2D velocity when returning to pool.")]
        [SerializeField]
        private bool resetRigidbody2DOnReturn = true;

        [Header("Audio Settings")]
        [Tooltip("Stop audio when returning to pool.")]
        [SerializeField]
        private bool stopAudioOnReturn = true;

        private float spawnTime;
        private ParticleSystem[] particleSystems;
        private Rigidbody rb;
        private Rigidbody2D rb2d;
        private AudioSource audioSource;
        private bool componentsChecked;

        void OnEnable()
        {
            spawnTime = Time.time;

            if (returnCondition == ReturnCondition.OnDisable)
            {
                // Will return when this gets disabled
            }
        }

        void Update()
        {
            // Check lifetime
            if (returnCondition == ReturnCondition.AfterTime)
            {
                if (Time.time - spawnTime >= lifetimeSeconds)
                {
                    ReturnNow();
                    return;
                }
            }

            // Check if particles finished
            if (returnCondition == ReturnCondition.OnParticleFinished)
            {
                if (AreParticlesFinished())
                {
                    ReturnNow();
                    return;
                }
            }
        }

        void OnDisable()
        {
            if (returnCondition == ReturnCondition.OnDisable)
            {
                // Object was disabled, we'll return it
                CancelInvoke();
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (returnCondition == ReturnCondition.OnTriggerExit)
            {
                ReturnNow();
            }
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (returnCondition == ReturnCondition.OnTriggerExit)
            {
                ReturnNow();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (returnCondition == ReturnCondition.OnCollision)
            {
                ReturnNow();
            }
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (returnCondition == ReturnCondition.OnCollision)
            {
                ReturnNow();
            }
        }

        /// <summary>
        /// Returns this object to the pool immediately. Call this from other scripts or UI buttons.
        /// </summary>
        public void ReturnNow()
        {
            // Prepare for return
            PrepareForReturn();

            // Return to pool
            if (PoolMasterManager.Instance != null)
            {
                PoolMasterManager.Instance.ReturnToPool(gameObject);
            }
            else
            {
                // Fallback: just disable
                gameObject.SetActive(false);
            }
        }

        private void PrepareForReturn()
        {
            EnsureComponentsCached();

            // Stop particles
            if (stopParticlesOnReturn && particleSystems != null)
            {
                foreach (var ps in particleSystems)
                {
                    if (ps != null)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        if (clearParticlesOnReturn)
                        {
                            ps.Clear();
                        }
                    }
                }
            }

            // Reset rigidbody
            if (resetRigidbodyOnReturn && rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Reset rigidbody2D
            if (resetRigidbody2DOnReturn && rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }

            // Stop audio
            if (stopAudioOnReturn && audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        private bool AreParticlesFinished()
        {
            EnsureComponentsCached();

            if (particleSystems == null || particleSystems.Length == 0)
                return true;

            foreach (var ps in particleSystems)
            {
                if (ps != null && ps.IsAlive(true))
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureComponentsCached()
        {
            if (componentsChecked)
                return;

            particleSystems = GetComponentsInChildren<ParticleSystem>();
            rb = GetComponent<Rigidbody>();
            rb2d = GetComponent<Rigidbody2D>();
            audioSource = GetComponent<AudioSource>();
            componentsChecked = true;
        }
    }

    /// <summary>
    /// Conditions that trigger returning to the pool.
    /// </summary>
    public enum ReturnCondition
    {
        [Tooltip("Return after a set amount of time.")]
        AfterTime,

        [Tooltip("Return when the GameObject is disabled.")]
        OnDisable,

        [Tooltip("Return when all particle systems finish playing.")]
        OnParticleFinished,

        [Tooltip("Return when leaving a trigger collider.")]
        OnTriggerExit,

        [Tooltip("Return when colliding with something.")]
        OnCollision,
    }
}
