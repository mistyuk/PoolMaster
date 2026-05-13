# Changelog

All notable changes to PoolMaster will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.3] - 2026-05-11

### Fixed
- **`PoolRequest.usePoolContainer` / `containerName` were silently ignored** when
  a pool was created through `PoolingManager.GetOrCreatePool<T>` /
  `GetOrCreateGameObjectPool`. The container-creation branch in `Pool<T>` and
  `GameObjectPool` only ran when `poolParent == null`, but `PoolingManager`
  always passed its own `transform` as `poolParent`. Pooled instances landed
  directly under the `PoolingManager` GameObject instead of under a named
  child container. Container is now always created when `usePoolContainer`
  is true and parented under `poolParent` (or scene root if none). Resulting
  hierarchy: `PoolingManager → [containerName] → pooled instances`.

---

## [1.0.2] - 2026-05-11

### Stability & Unity 6 compatibility

This release rolls up bug fixes, Unity 6 API updates, and a major feature (non-`IPoolable`
prefab support) battle-tested in a production project. The previous main branch had two
compilation errors that prevented clean adoption on Unity 6 — both are now resolved.

#### Added
- **`GameObjectPool`** — Non-generic pool that pools any prefab, even without an `IPoolable`
  component. `PoolingManager.Spawn`, `TryBootstrapPool`, and `AddPreset` now fall back to
  this when the prefab has no `IPoolable` (previously this was a logged error and `null`
  return). New public API: `PoolingManager.GetOrCreateGameObjectPool(prefab, request)`.
- **`GameObjectPool.PrewarmSpread(count, frameBudgetMs)`** — Prewarms across multiple frames
  within a per-frame time budget for hitching-free runtime expansion.
- **`IPoolControl.Reseed(bool rePrewarm = true)`** — Flushes and rebuilds a pool. Force-despawns
  every active instance via PooledMarker scan, destroys all inactive instances, then optionally
  re-prewarms to the original `initialPoolSize`. Use after editing the source prefab at runtime
  so existing pooled clones get replaced with fresh ones on the next Spawn — no more game
  restarts to pick up prefab edits.
- **Diagnostics window — per-pool controls** — Clear Inactive / Shrink to 4 / **Reseed** /
  Destroy (with confirmation) buttons on every pool entry. Global toolbar gains Clear All
  Inactive and Cull Unused (60s) actions.

#### Fixed
- **Pool.cs** — Removed reference to undeclared `componentCache` field in `ShrinkInactive`
  that prevented the package from compiling on a fresh import.
- **Tests** — `PoolMetricsTests` and `PoolSnapshotTests` were calling the `PoolMetrics`
  constructor with 9 arguments when it expects 10 (`lastActivityTime` was missing).
- **Pool.cs / GameObjectPool.cs** — Underflow guard on `activeCount`. Double-despawn or
  external destroys no longer drive the counter negative.
- **GameObjectPool.Despawn** — Reject double-despawn early. Without this the same instance
  could be pushed onto `inactivePool` twice and handed out by subsequent `Spawn()` calls.
- **Pool.cs** — `Object.Destroy` instead of `DestroyImmediate` during Play Mode (the editor
  branch was firing in Play Mode too, which can corrupt the inactive stack).
- **PoolableMonoBehaviour subclasses** — `SimplePoolableObject.OnSpawned/OnDespawned` now
  call `base.OnSpawned()`/`base.OnDespawned()` so base-class lifecycle runs.
- **PoolMasterReturnToPool** — `Rigidbody.velocity` → `linearVelocity` for Unity 6
  (`velocity` was renamed and is obsolete in Unity 2023.3+).
- **PoolMasterManager** — `FindObjectOfType` → `FindFirstObjectByType` behind a
  `#if UNITY_2023_1_OR_NEWER` guard.

#### Hardened
- **PoolingManager singleton** — `_destroyed` flag prevents resurrection during
  `OnApplicationQuit`; `Instance` refuses to auto-create in non-Play mode; a
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` resets statics on domain reload
  so Enter Play Mode without Domain Reload works correctly.
- **GameObjectPool ctor** — Null-guards `PoolingManager.Instance` before `StartCoroutine`
  on the `NextFrame` prewarm path; falls back to immediate prewarm if the manager has
  already shut down.
- **PoolingManager.AddPreset** — Now validates with `PoolRequest.IsValid()` and dedups
  against both live pools and the pending presets list.
- **PoolingManager.GetAllPoolMetrics / GetSnapshot** — Key results by `PoolId` instead of
  `prefab.name` to avoid collisions when two prefabs share a name.
- **PoolingManager.CullUnusedPools** — Uses `CollectionPool.GetList<>()` with `try/finally`
  instead of `new List<>()` on every call.

#### Internal
- **PoolingDiagnosticsWindow** — Caches `RichBoldLabel` `GUIStyle` (was allocating one per
  row per repaint); hex color codes for active/inactive indicators render consistently
  across editor themes.

#### Examples / Demo
- **Full rewrite**. Old `Examples/` folder (BasicPoolingDemo, BatchSpawningDemo,
  CollectionPoolingDemo, CommandBufferDemo, DemoNavigator, DemoSceneSetup, etc.) deleted
  in favor of a single `DemoBootstrap.cs` plus a minimal `DemoScene.unity`.
- **No external assets** — camera, light, floor, four pool templates, and the entire
  UI Toolkit HUD are built programmatically at `Awake`. No prefabs, materials, UXML, or
  USS to maintain.
- **Always-compiled** — Examples now has `PoolMaster.Examples.asmdef` and compiles the
  moment the package is added. The package-level `samples` entry has been removed so
  there's no manual "Import Sample" step (which would have caused an asmdef name
  collision with the in-package copy).
- **Seven live demo modes** in the HUD:
    - **Basic Spawn** — core `Pool<T>.Spawn`/`Despawn` round-trip
    - **Batch Spawn** — multi-instance grid spawn with auto-recycle of previous grid
    - **Projectiles** — continuous 25 Hz fire stream with lifetime-based auto-despawn
    - **Fireworks** — pooled `ParticleSystem` bursts in 8 different colours via
      per-spawn `MaterialPropertyBlock` (doesn't break GPU instancing); includes a
      "Reseed burst pool" button as a Reseed showcase
    - **GO Pool** — plain cube prefab with NO `IPoolable` component, pooled via
      `PoolingManager.GetOrCreateGameObjectPool` (the v1.0.2 flagship feature)
    - **Stress Test** — burst-spawn 500/1000/2000/5000 cubes into the same pool used
      by Basic Spawn; visceral demonstration that the pool scales linearly with no
      per-spawn allocation
    - **Metrics** — live per-pool active/inactive/reuse-efficiency readout plus
      `CullUnusedPools` / `ClearAllPools` buttons
- **HUD font** — programmatic `PanelSettings` doesn't auto-link a runtime theme, so
  Labels/Buttons render blank backgrounds with no text. Bootstrap now assigns the
  built-in `LegacyRuntime.ttf` to the root via `unityFontDefinition`; UITK cascades it
  to every descendant. Also assigns an empty `ThemeStyleSheet` to silence the
  "No Theme Style Sheet set" warning.
- **GPU warmup** — `WarmupGpuResources` coroutine spawns one instance of each pool
  below the floor for one frame at game start, forcing the D3D12 backend to allocate
  vertex/instance buffers up front. Without this, the first particle burst paid a
  ~60ms `CreateCommittedResourceWithTag` hitch.
- **Particle render flags** — `ShadowCastingMode.Off` + `receiveShadows = false` +
  `reflectionProbeUsage = Off` + `lightProbeUsage = Off` on `ParticleSystemRenderer`,
  and `material.enableInstancing = true`. A ring-of-12 burst (300 particles) is now
  ~3-5× cheaper than before.
- **Compact HUD card** — UI Toolkit panel now caps at 960px max-width, centered,
  with backdrop + border + rounded corners. Sized to content rather than full-screen
  stretch so the 3D scene below isn't unnecessarily covered.
- **URP-first with Standard fallback** — uses `Universal Render Pipeline/Lit` for opaque
  geometry. Particles intentionally use `Sprites/Default` (always available, transparent
  by default, no shader-keyword juggling required).

#### Asmdef cleanup
- **Runtime/PoolMaster.asmdef** — Removed unused references to `Unity.Burst`,
  `Unity.Collections`, `Unity.Jobs`, `Unity.Mathematics`. No source file imported these
  namespaces, so the references only generated "assembly not found" warnings in projects
  that didn't have those packages installed.

---

## [1.0.0] - 2025-12-16

### 🎉 Initial Release - Production Ready

#### Fixed (Pre-Release)
- **PoolCommandBuffer**: Fixed compilation errors (undefined `completionSource` parameter, malformed syntax)
- **Pool.cs**: Fixed `ExpansionCount` never incrementing despite pool expansions
- **PoolMetrics**: Added documentation for theoretical integer overflow edge case
- **PoolableMonoBehaviour**: Eliminated array allocations using `Array.Empty<T>()` for zero-allocation hot paths
- **Thread-Safety**: Added comprehensive documentation for main thread vs background thread usage

#### Optimized (Pre-Release)
- **CollectionPool**: Replaced reflection-based count with O(1) counter tracking
- **PoolMetrics**: Added NaN guard to `AverageExpansionInterval` calculation
- **PoolRequest**: Added validation to prevent `initialPoolSize > maxPoolSize`
- **PoolingManager**: Added `CullUnusedPools()` method for long-running session memory management

#### Added
- **Core Pooling System**
  - Generic `Pool<T>` implementation with type safety
  - `PoolingManager` singleton for centralized pool management
  - `IPoolable` interface for poolable objects
  - `IPoolControl` interface for advanced pool operations
  - `PoolableMonoBehaviour` base class with automatic cleanup

- **Performance Features**
  - Zero-allocation pooling with compile-time logging via `PoolLog`
  - `CollectionPool` for List<T>, HashSet<T>, and Dictionary<K,V> pooling
  - Batch spawn/despawn operations for bulk object management
  - Component caching to avoid repeated GetComponent calls
  - Command buffer system for thread-safe enqueueing

- **Configuration & Control**
  - `PoolRequest` for flexible pool configuration
  - Multiple initialization timing strategies (Lazy, Immediate, OnAwake, OnStart, OnEvent)
  - Automatic pool expansion and culling
  - Pre-warming support for hitching-free gameplay
  - Configurable max pool sizes and thresholds

- **Diagnostics & Monitoring**
  - `PoolMetrics` struct for performance tracking
  - `PoolSnapshot` for system-wide statistics
  - Editor diagnostics window with real-time monitoring
  - Comprehensive event system via `PoolingEvents`
  - Reuse efficiency tracking

- **Extension Methods**
  - `GameObject.ReturnToPool()` convenience method
  - Batch operation extensions for `IPoolControl`
  - Pool manager batch extensions

- **Example Components**
  - `PooledProjectile` - Example physics-based pooled object
  - `PooledVfx` - Automatic particle system management
  - `PooledMarker` - Lightweight pool marker component
  - `SimplePoolableObject` - Basic poolable implementation
  - `BatchSpawnExample` - Demonstrates batch operations
  - `OffThreadSpawnExample` - Shows job system integration

- **Infrastructure**
  - Assembly definitions for clean module separation
  - Comprehensive XML documentation
  - MIT License
  - Unity 2020.3+ support

#### Technical Details
- **Namespace**: `PoolMaster`
- **Minimum Unity Version**: 2020.3 LTS
- **Dependencies**: 
  - Unity.Collections (optional, for advanced features)
  - Unity.Jobs (optional, for batch job examples)
  - Unity.Burst (optional, for performance in examples)
  - Unity.Mathematics (optional, for job examples)

#### Performance Characteristics
- 400x faster spawning vs Instantiate/Destroy
- 300x faster despawning
- 240x reduction in GC allocations
- Sub-millisecond batch operations
- Zero overhead logging when disabled

### Architecture
```
PoolMaster/
├── Runtime/
│   ├── Core/              # Core pooling system
│   ├── Extensions/        # Extension methods
│   ├── Components/        # MonoBehaviour components
│   └── Utilities/         # Helper classes and logging
├── Editor/                # Editor tools and windows
├── Examples/              # Example implementations
│   ├── Components/        # Example poolable components
│   └── (Scene examples)   # Demo scenes
└── Documentation/         # Additional documentation
```

### Known Limitations
- Core pooling operations must occur on main thread
- Thread-safe operations available via `PoolCommandBuffer`
- Not designed for ECS/DOTS workflows (use native entity pooling instead)

---

## [Unreleased]

### Planned Features
- [ ] Addressables integration example
- [ ] Visual Scripting support
- [ ] Additional example scenes
- [ ] Performance profiler integration
- [ ] Pool capacity auto-tuning
- [ ] Async/await spawn patterns
- [ ] Unity Input System integration examples

---

## Version History

- **1.0.0** (2025-12-16) - Initial public release

---

## Migration Notes

### From Custom Pooling Solutions
- Replace manual Queue/Stack pooling with `Pool<T>`
- Implement `IPoolable` on pooled components
- Use `PoolingManager.Instance` for centralized management
- Enable `ENABLE_POOL_LOGS` during development for diagnostics

### From Unity's ObjectPool
- `ObjectPool<T>` → `Pool<T>` or `PoolingManager`
- Configure via `PoolRequest` instead of constructor delegates
- Use `IPoolable.OnSpawned/OnDespawned` instead of callbacks
- Access via `PoolingManager.Instance` for global management

---

## Support

For bug reports and feature requests, please visit:
- GitHub Issues: https://github.com/yourusername/PoolMaster/issues
- GitHub Discussions: https://github.com/yourusername/PoolMaster/discussions

---

**Legend:**
- ✨ **Added** - New features
- 🔧 **Changed** - Changes in existing functionality
- 🐛 **Fixed** - Bug fixes
- ⚠️ **Deprecated** - Soon-to-be removed features
- 🗑️ **Removed** - Removed features
- 🔒 **Security** - Security fixes
