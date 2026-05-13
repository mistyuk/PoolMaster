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
    /// Example pooled VFX implementation for particle systems with automatic return when finished.
    /// </summary>
    public class PooledVfx : PoolableMonoBehaviour
    {
        #region Configuration

        [Header("VFX Behavior")]
        [SerializeField]
        private bool autoReturnWhenFinished = true;

        [SerializeField]
        private float maxLifetime = 10f;

        [SerializeField]
        private bool useMaxLifetime = true;

        [SerializeField]
        private float checkInterval = 0.1f;

        [Header("Particle Control")]
        [SerializeField]
        private bool playOnSpawn = true;

        [SerializeField]
        private bool stopOnDespawn = true;

        [SerializeField]
        private bool clearOnDespawn = true;

        #endregion

        #region Runtime State

        private float spawnTime;
        private float nextCheckTime;
        private bool isPlaying;

        // Transient overrides set by PlayForDuration so we don't permanently mutate the
        // [SerializeField] defaults. When _hasDurationOverride is true, the Update loop
        // uses _overrideMaxLifetime instead of maxLifetime / useMaxLifetime / autoReturnWhenFinished.
        private bool _hasDurationOverride;
        private float _overrideMaxLifetime;

        #endregion

        #region Pooling Lifecycle

        public override void OnSpawned()
        {
            base.OnSpawned();

            spawnTime = Time.time;
            nextCheckTime = Time.time + checkInterval;
            isPlaying = false;

            // Start playing particles if configured
            if (playOnSpawn)
            {
                PlayVfx();
            }
        }

        public override void OnDespawned()
        {
            // Stop VFX before base cleanup
            if (stopOnDespawn)
            {
                StopVfx();
            }

            base.OnDespawned();

            isPlaying = false;
            // Clear any transient duration override so the next spawn from this pool
            // sees the original [SerializeField] settings again.
            _hasDurationOverride = false;
        }

        public override void PoolReset()
        {
            // Reset VFX-specific state
            spawnTime = 0f;
            nextCheckTime = 0f;
            isPlaying = false;

            // Call base reset last
            base.PoolReset();
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            // Transient override path: PlayForDuration was called this spawn. Single
            // exit condition (elapsed >= override) and we ignore the serialized flags.
            if (_hasDurationOverride)
            {
                if (Time.time - spawnTime >= _overrideMaxLifetime)
                {
                    OnMaxLifetimeReached();
                }
                return;
            }

            if (!autoReturnWhenFinished && !useMaxLifetime)
                return;

            float currentTime = Time.time;

            // Check max lifetime
            if (useMaxLifetime && currentTime - spawnTime >= maxLifetime)
            {
                OnMaxLifetimeReached();
                return;
            }

            // Periodic check for completion (avoid checking every frame)
            if (autoReturnWhenFinished && currentTime >= nextCheckTime)
            {
                nextCheckTime = currentTime + checkInterval;

                if (isPlaying && !IsAnyParticleSystemPlaying())
                {
                    OnVfxFinished();
                }
            }
        }

        #endregion

        #region VFX Control

        /// <summary>
        /// Start playing all particle systems.
        /// </summary>
        public void PlayVfx()
        {
            EnsureComponentsCached();

            // Start all particle systems using cached array
            var particles = CachedParticleSystems;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                {
                    particles[i].Play();
                }
            }

            isPlaying = true;
            OnVfxStarted();
        }

        /// <summary>
        /// Stop all particle systems.
        /// </summary>
        public void StopVfx()
        {
            if (clearOnDespawn)
            {
                StopAndClearParticles();
            }
            else
            {
                // Just stop without clearing using cached array
                var particles = CachedParticleSystems;
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null)
                    {
                        particles[i].Stop();
                    }
                }
            }

            isPlaying = false;
            OnVfxStopped();
        }

        /// <summary>
        /// Check if any particle system is currently playing.
        /// </summary>
        /// <returns>True if any particle system is active and playing</returns>
        private bool IsAnyParticleSystemPlaying()
        {
            var particles = CachedParticleSystems;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null && particles[i].isPlaying)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Called when VFX starts playing.
        /// Override this method to implement custom start behavior.
        /// </summary>
        protected virtual void OnVfxStarted()
        {
            // Default behavior: do nothing
            // Override in derived classes for audio, secondary effects, etc.
        }

        /// <summary>
        /// Called when VFX stops playing.
        /// Override this method to implement custom stop behavior.
        /// </summary>
        protected virtual void OnVfxStopped()
        {
            // Default behavior: do nothing
            // Override in derived classes for cleanup, callbacks, etc.
        }

        /// <summary>
        /// Called when all particle systems have finished playing.
        /// Override this method to implement custom completion behavior.
        /// </summary>
        protected virtual void OnVfxFinished()
        {
            ReturnToPool();
        }

        /// <summary>
        /// Called when the maximum lifetime is reached.
        /// Override this method to implement custom timeout behavior.
        /// </summary>
        protected virtual void OnMaxLifetimeReached()
        {
            ReturnToPool();
        }

        #endregion

        #region Public Interface

        /// <summary>
        /// Return this VFX to its pool.
        /// Safe to call multiple times.
        /// </summary>
        public void ReturnToPool()
        {
            if (IsPooled)
            {
                gameObject.ReturnToPool();
            }
        }

        /// <summary>
        /// Play the VFX, force-returning to the pool after the given duration regardless
        /// of the inspector-configured auto-return settings. The override is transient —
        /// it only applies to this spawn and resets on OnDespawned, so subsequent spawns
        /// from the same pool see the original serialized values.
        /// </summary>
        /// <param name="duration">Duration in seconds before forcing return to pool.</param>
        public void PlayForDuration(float duration)
        {
            PlayVfx();

            // Transient runtime override — does NOT mutate [SerializeField] fields.
            // Previously this method overwrote maxLifetime / useMaxLifetime /
            // autoReturnWhenFinished on the prefab instance, permanently changing
            // behavior for every future spawn that reused the same pooled GameObject.
            _hasDurationOverride = true;
            _overrideMaxLifetime = duration;
        }

        /// <summary>
        /// Whether the VFX is currently playing.
        /// </summary>
        public bool IsPlaying => isPlaying;

        /// <summary>
        /// Get the time this VFX has been active.
        /// </summary>
        public float ActiveTime => Time.time - spawnTime;

        /// <summary>
        /// Get the time remaining before max lifetime expires.
        /// </summary>
        public float TimeRemaining =>
            useMaxLifetime ? Mathf.Max(0f, maxLifetime - ActiveTime) : float.PositiveInfinity;

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get all particle systems in this VFX hierarchy.
        /// </summary>
        /// <returns>Array of all ParticleSystem components</returns>
        public ParticleSystem[] GetAllParticleSystems()
        {
            return CachedParticleSystems;
        }

        /// <summary>
        /// Set emission rate for all particle systems.
        /// </summary>
        /// <param name="rate">The emission rate to set</param>
        public void SetEmissionRate(float rate)
        {
            var particles = CachedParticleSystems;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                {
                    var emission = particles[i].emission;
                    emission.rateOverTime = rate;
                }
            }
        }

        /// <summary>
        /// Set start color for all particle systems.
        /// </summary>
        /// <param name="color">The color to set</param>
        public void SetStartColor(Color color)
        {
            var particles = CachedParticleSystems;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                {
                    var main = particles[i].main;
                    main.startColor = color;
                }
            }
        }

        #endregion
    }
}
