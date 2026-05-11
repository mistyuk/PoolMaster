# PoolMaster — Demo

A self-contained, zero-asset demo that exercises the full PoolMaster API.

## How to run

1. Make sure the package is added to your project (Package Manager → `com.poolmaster.core`).
2. Open `Packages/PoolMaster/Examples/DemoScene.unity`.
3. Press Play.

The scene contains a single `PoolMaster Demo Bootstrap` GameObject. On `Awake`, `DemoBootstrap.cs` builds everything programmatically:

- A camera, directional light, and floor plane
- Four pool "template" GameObjects (Cube, Sphere, Projectile, ParticleBurst) registered with `PoolingManager`
- A UI Toolkit HUD that switches between five demo modes

No external assets, no prefabs, no UXML/USS — the entire demo lives in one `.cs` file (~600 lines) plus the scene wrapper.

## Demo modes

| Mode | What it shows | Key types |
| --- | --- | --- |
| **Basic Spawn** | Single-prefab `Spawn` / `Despawn` round-trip | `Pool<T>`, `SimplePoolableObject` |
| **Batch Spawn** | Lay down a regular grid; older grids auto-recycle | manual loop + tracking |
| **Projectiles** | Continuous 25 Hz fire with lifetime-based auto-despawn | `PooledProjectile`, `LaunchWithVelocity` |
| **Fireworks** | Pooled `ParticleSystem` bursts in 8 different colours | `PooledVfx`, `MaterialPropertyBlock` |
| **GO Pool** | Plain prefab (no `IPoolable`) — proves any prefab can be pooled | `PoolingManager.GetOrCreateGameObjectPool` |
| **Stress Test** | Burst 500–5000 cubes; watch reuse efficiency climb | Same `_cubePool`, hammered |
| **Metrics** | Live per-pool active/inactive/reuse %; diagnostics shortcut | `IPoolControl.Metrics`, `PoolingManager` |

The Fireworks tab has a **Reseed burst pool** button — force-flushes every active firework and rebuilds the pool. Same operation is on every pool row in **Window → PoolMaster → Diagnostics**.

The HUD also has buttons for `PoolingManager.CullUnusedPools(60f)` and `ClearAllPools()` (Metrics tab) so you can watch state move in real time.

## GPU warmup

At Awake, the bootstrap spawns one instance of each pool below the floor (`y = -500`) for one frame. This forces the D3D12 backend to allocate vertex / instance buffers up front instead of paying the `CreateCommittedResourceWithTag` cost (~60ms hitch) on the user's first interaction. Particle bursts in particular benefit — without warmup the first burst is visibly janky.

For a richer view, open **Window → PoolMaster → Diagnostics** while the demo is running.

## Rendering

The demo uses `Universal Render Pipeline/Lit` for opaque geometry and `Universal Render Pipeline/Particles/Unlit` for the bursts, with falls back to `Standard` / `Particles/Standard Unlit` if URP isn't installed. Either pipeline renders correctly.

## Customizing

Open `Examples/Scripts/DemoBootstrap.cs`. The structure is split by region — each demo mode has its own short method (`BasicSpawnOne`, `BatchSpawnGrid`, `FireStormProjectile`, `PopParticleBurst`). Copy any of these directly into your own code as a starting point.

If you want to hide the HUD for screen recording, tick `Hide Hud` on the `PoolMaster Demo Bootstrap` GameObject in the Inspector.
