# PoolMaster

**PoolMaster** is a high-performance, production-ready object pooling system for Unity. Designed for both 2D and 3D games, it provides a comprehensive solution for managing GameObject lifecycles efficiently, reducing GC pressure, and maximizing runtime performance.

[![Unity](https://img.shields.io/badge/Unity-6.0%E2%80%936.4-black)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

### Getting Started

For the fastest setup, start with the No‑Code Quick Start: [Documentation/no-code-quick-start.md](Documentation/no-code-quick-start.md)

---

## Features

- Zero-allocation pooling — minimal GC, fast hot paths
- Type-safe API — generic Pool<T> with safety
- Batch ops — spawn/despawn many at once
- Command buffers — thread-safe enqueue, main-thread flush
- Diagnostics — real-time metrics + editor window
- Configurable — precise control over pool behavior
- Events — decoupled, opt-in hooks
- Collection pooling — reuse lists/dicts/sets
- Easy integration — IPoolable + helpers

## Compatibility

- Supported Unity: 6.0 – 6.4 (stable)
- Render Pipelines: Built-in, URP, HDRP

## Links

- Add me on Discord: [misty2023](https://discord.com/users/misty2023)

## Installation

### Option 1: Unity Package Manager (Recommended)
1. Open Package Manager (`Window > Package Manager`)
2. Click `+` and select `Add package from git URL`
3. Enter: `https://github.com/mistyuk/PoolMaster.git`

### Option 2: Manual Installation
1. Download the latest release from [GitHub](https://github.com/mistyuk/PoolMaster/releases)
2. Extract to your `Assets/Plugins/PoolMaster` folder

---

## Quick Start

**Choose your path:** No code setup or full API control.

### Path 1: No-Code Setup (60 seconds)

Perfect for beginners or rapid prototyping. Zero programming required.

#### Step 1: Add Manager
1. **Hierarchy** → Right-click → **Create Empty** → Name it `PoolMaster`
2. **Add Component** → `PoolMaster Manager`

#### Step 2: Create Pool
1. Select `PoolMaster` → Inspector → **Add New Pool**
2. Drag your prefab → Set **Prewarm Amount** = `10`

#### Step 3: Auto-Spawn
1. Create empty GameObject → Name it `Spawner`
2. **Add Component** → `PoolMaster Spawner`
3. Drag prefab → **Spawn On** = `On Start`

#### Step 4: Auto-Return
1. Select your prefab (in Project) → **Add Component** → `PoolMaster Return To Pool`
2. **Return Condition** = `After Time` → **Lifetime** = `2` seconds

**Press Play** - Objects spawn, live 2 seconds, return to pool automatically.

> **Full no-code guide:** See [Documentation/no-code-quick-start.md](Documentation/no-code-quick-start.md)

---

### Path 2: Code Setup (Programmer API)

Full control for advanced users. Type-safe, high-performance pooling.

#### Step 1: Configure Pool

```csharp
using UnityEngine;
using PoolMaster;

public class BulletSpawner : MonoBehaviour
{
    [SerializeField] private GameObject bulletPrefab;
    
    void Start()
    {
        var request = new PoolRequest
        {
            prefab = bulletPrefab,
            initialPoolSize = 20,
            shouldPrewarm = true
        };
        
        PoolingManager.Instance.GetOrCreatePool<Bullet>(request);
    }
}
```

#### Step 2: Spawn from Pool

```csharp
void Update()
{
    if (Input.GetKeyDown(KeyCode.Space))
    {
        var bullet = PoolingManager.Instance.Spawn(
            bulletPrefab, 
            transform.position, 
            Quaternion.identity
        );
    }
}
```

#### Step 3: Implement IPoolable

```csharp
using UnityEngine;
using PoolMaster;

public class Bullet : MonoBehaviour, IPoolable
{
    public IPool ParentPool { get; set; }
    public bool IsPooled { get; private set; }
    
    public void OnSpawned()
    {
        IsPooled = true;
        // Unity 6 renamed Rigidbody.velocity to .linearVelocity. PoolMaster targets
        // Unity 6.0–6.4 (see package.json) so the new name is required.
        GetComponent<Rigidbody>().linearVelocity = transform.forward * 20f;
    }
    
    public void OnDespawned()
    {
        IsPooled = false;
    }
    
    public void PoolReset()
    {
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }
    
    void OnCollisionEnter(Collision collision)
    {
        ParentPool?.Despawn(gameObject);
    }
}
```

**That's it.** Spawn uses the pool, collision returns to pool.

---

## API Reference

### Core Classes

#### PoolingManager
Central singleton for managing all pools.

```csharp
// Get or create a pool
var pool = PoolingManager.Instance.GetOrCreatePool<T>(request);

// Spawn objects
GameObject obj = PoolingManager.Instance.Spawn(prefab, position, rotation);
GameObject obj = PoolingManager.Instance.Spawn(prefab); // At origin

// Despawn objects
PoolingManager.Instance.Despawn(obj);

// Batch operations — SpawnBatch is an extension method on IPoolControl,
// not a method on PoolingManager. Get the pool first, then call it.
var bulletPool = (IPoolControl)PoolingManager.Instance.GetOrCreatePool<Bullet>(request);
Vector3[] positions = { new Vector3(0, 0, 0), new Vector3(1, 0, 0) };
Quaternion[] rotations = { Quaternion.identity, Quaternion.identity };
int count = bulletPool.SpawnBatch(positions, rotations, parent: null);

// Look up pool by string ID (the one you set in request.poolId).
// By-prefab lookup uses TryGetPool(GameObject, out IPool) instead.
IPool pool = PoolingManager.Instance.GetPool("Bullets");
PoolingManager.Instance.TryGetPool(prefab, out IPool poolByPrefab);

// Global snapshot
PoolSnapshot snapshot = PoolingManager.Instance.GetSnapshot();
```

#### Pool<T>
Generic type-safe pool implementation.

```csharp
// Create pool directly
var pool = new Pool<Bullet>(
    prefab, 
    request, 
    poolParent, 
    poolId
);

// Pool operations
GameObject obj = pool.Spawn(position, rotation, parent);
bool success = pool.Despawn(obj);
pool.PrewarmPool(count);
pool.Clear();
pool.DestroyPool();

// Pool info
int active = pool.ActiveCount;
int inactive = pool.InactiveCount;
int total = pool.Capacity;
PoolMetrics metrics = pool.Metrics;
```

#### PoolRequest
Configuration for pool creation.

```csharp
var request = new PoolRequest
{
    prefab = bulletPrefab,
    poolId = "Bullets",                 // Optional ID for GetPool("Bullets") lookup
    initialPoolSize = 50,               // Pre-instantiated count at startup
    shouldPrewarm = true,               // Create initialPoolSize objects immediately
    maxPoolSize = 200,                  // Max instances kept around
    allowDynamicExpansion = true,       // Grow past maxPoolSize when exhausted
    cullExcessObjects = true,           // Destroy excess instances when inactive cache > maxPoolSize
    initializationTiming = PoolInitializationTiming.OnAwake,
    usePoolContainer = true,            // Group spawned instances under a named child of PoolingManager
    containerName = "Bullet Pool",
    category = "Combat"                 // Optional tag for grouping in diagnostics
};
```

### Added in 1.0.2

#### GameObjectPool — pool *any* prefab, no `IPoolable` required
Non-generic pool implementation. Use when the prefab is plain geometry / particles
without a pooling component.

```csharp
// Plain prefab — no PoolableMonoBehaviour, no IPoolable, no scripts.
var request = PoolRequest.Create(plainPrefab, initialSize: 10);
IPool pool = PoolingManager.Instance.GetOrCreateGameObjectPool(plainPrefab, request);

// Same API as Pool<T> from the consumer side
GameObject obj = pool.Spawn(position, rotation, parent);
obj.ReturnToPool();
```

`PoolingManager.Spawn(prefab, …)` automatically falls back to a `GameObjectPool`
when the prefab has no `IPoolable` — previously this was a logged error. If the
prefab *does* implement `IPoolable`, `GetOrCreateGameObjectPool` routes to the
type-safe `Pool<T>` path so behavior stays consistent.

#### IPoolControl.Reseed — flush after editing prefabs at runtime
Force-despawns every active instance, destroys all inactive instances, then
optionally re-prewarms to the original `initialPoolSize`. Use after modifying
the source prefab at runtime so existing pooled clones get replaced with fresh
ones on the next `Spawn`.

```csharp
// Flush + re-prewarm to initialPoolSize
pool.Reseed(rePrewarm: true);

// Flush only — next Spawn will lazily instantiate from the updated prefab
pool.Reseed(rePrewarm: false);
```

Also surfaced as a per-pool button in **Window → PoolMaster → Diagnostics**.

#### Diagnostics window — bulk and per-pool controls
- Global: **Clear All Inactive**, **Cull Unused (60s)**
- Per pool: **Clear Inactive**, **Shrink to 4**, **Reseed**, **Destroy**

#### Hardening
- Singleton resurrection during `OnApplicationQuit` blocked; `Instance` returns
  `null` instead of auto-creating in non-Play mode.
- `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` resets statics on
  domain reload — Enter Play Mode without Domain Reload now works correctly.
- `PoolingManager.AddPreset` validates with `PoolRequest.IsValid()` and dedups
  against both live pools and pending presets.
- `PoolingManager.GetAllPoolMetrics` / `GetSnapshot` key results by `PoolId`
  instead of `prefab.name` — avoids collisions when two prefabs share a name.

#### CollectionPool
Static utility for pooling collections.

```csharp
// Lists
var list = CollectionPool.GetList<GameObject>();
// ... use list ...
CollectionPool.Return(list);

// HashSets
var set = CollectionPool.GetHashSet<int>();
CollectionPool.Return(set);

// Dictionaries
var dict = CollectionPool.GetDictionary<string, GameObject>();
CollectionPool.Return(dict);
```

### Extension Methods

```csharp
// GameObject extension — finds the parent pool via PooledMarker and returns the instance.
gameObject.ReturnToPool();

// Batch spawning is on IPoolControl, not PoolingManager. Cast the pool, then call.
var ctrl = (IPoolControl)pool;
ctrl.SpawnBatch(positions, rotations, parent: null);   // arrays of Vector3 and Quaternion
ctrl.SpawnGrid(center, spacing: 0.5f, gridSize: 8);    // lays out gridSize² instances in a square grid
```

### Events

```csharp
// Pool lifecycle — all three always available.
PoolingEvents.OnPoolCreated   += (poolId, prefab) => {};       // Action<string, GameObject>
PoolingEvents.OnPoolDestroyed += (poolId, prefab) => {};       // Action<string, GameObject>
PoolingEvents.OnPoolPrewarmed += (poolId, count)  => {};       // Action<string, int>

// Object creation — always available.
PoolingEvents.OnObjectCreated += (obj, poolId) => {};          // Action<GameObject, string>

// Per-spawn / per-despawn events — guarded by the ENABLE_POOL_LOGS compile define
// to avoid per-call delegate dispatch in release builds. Add ENABLE_POOL_LOGS to
// your Scripting Define Symbols if you need them.
#if ENABLE_POOL_LOGS
PoolingEvents.OnObjectSpawned   += (obj, poolId) => {};
PoolingEvents.OnObjectDespawned += (obj, poolId) => {};
#endif

// Performance
PoolingEvents.OnPoolExpanded += (poolId, oldSize, newSize) => {}; // Action<string, int, int>
PoolingEvents.OnPoolCulled   += (poolId, objectsCulled)    => {}; // Action<string, int>
PoolingEvents.OnPoolExhausted += (poolId, maxSize)         => {}; // Action<string, int>
```

## Performance Benchmarks

Performance comparison vs traditional `Instantiate/Destroy`:

| Operation | Instantiate/Destroy | PoolMaster | Improvement |
|-----------|---------------------|------------|-------------|
| Spawn Single Object | ~0.8ms | ~0.002ms | **400x faster** |
| Spawn 100 Objects | ~80ms | ~0.2ms | **400x faster** |
| Despawn Single Object | ~0.3ms | ~0.001ms | **300x faster** |
| GC Allocations (1000 cycles) | ~120 MB | ~0.5 MB | **240x less** |
| Frame time impact (60fps) | 5-15ms spikes | <0.1ms | **No hitching** |

### Batch Operations Performance

| Batch Size | Individual Spawn | Batch Spawn | Improvement |
|------------|------------------|-------------|-------------|
| 10 objects | ~0.02ms | ~0.008ms | **2.5x faster** |
| 50 objects | ~0.1ms | ~0.03ms | **3.3x faster** |
| 100 objects | ~0.2ms | ~0.05ms | **4x faster** |
| 500 objects | ~1.0ms | ~0.2ms | **5x faster** |

*Benchmarks run on Unity 6000.0.62f1 LTS, Intel i9-13950HX, 32GB RAM DDR5 5200mhz, RTX 4070 mGPU*

## Advanced Usage

### Command Buffer System

For thread-safe enqueueing from Jobs or background threads:

```csharp
// Get command buffer for a pool (looked up by poolId)
var buffer = PoolingManager.Instance.GetCommandBuffer("Bullets");

// Enqueue spawn from a background thread / Job — thread-safe.
buffer.EnqueueSpawn(position, rotation, parent: null);

// Enqueue a batch from a background thread.
buffer.EnqueueSpawnBatch(positions, rotations, parent: null);

// Commands are flushed each frame in PoolingManager.LateUpdate on the main thread.
// All spawning / despawning happens there — your background thread never touches
// Unity APIs directly.
```

### Pool Metrics & Diagnostics

```csharp
// Get metrics
PoolMetrics metrics = pool.Metrics;

Debug.Log($"Total Spawned: {metrics.TotalSpawned}");
Debug.Log($"Total Despawned: {metrics.TotalDespawned}");
Debug.Log($"Reuse Efficiency: {metrics.ReuseEfficiency:P}");
Debug.Log($"Current Active: {metrics.CurrentActive}");
Debug.Log($"Expansion Count: {metrics.ExpansionCount}");

// Open diagnostics window
// Window > PoolMaster > Diagnostics
```

### Pre-warming Strategies

```csharp
// 1. On Awake (before scene starts)
var request = new PoolRequest
{
    prefab = prefab,
    initialPoolSize = 50,
    shouldPrewarm = true,
    initializationTiming = PoolInitializationTiming.OnAwake
};

// 2. On Start (after scene initialized)
request.initializationTiming = PoolInitializationTiming.OnStart;

// 3. Lazy (only when first needed)
request.initializationTiming = PoolInitializationTiming.Lazy;

// 4. Next frame (avoid loading hitches)
request.initializationTiming = PoolInitializationTiming.NextFrame;

// 5. On event (custom timing) — pools wait until you fire their event.
request.initializationTiming = PoolInitializationTiming.OnEvent;
request.eventId = "level_loaded";
PoolingManager.Instance.AddPreset(request);

// Later, when the event fires:
PoolingManager.Instance.TriggerBootstrap("level_loaded");
```

### Working with Particles

Use the included `PooledVfx` component for automatic particle system management:

```csharp
public class PooledVfx : PoolableMonoBehaviour
{
    [SerializeField] private bool autoReturnWhenFinished = true;
    [SerializeField] private float maxLifetime = 10f;
    
    // Automatically returns to pool when particles finish
}
```

### Custom Pool Control

```csharp
if (pool is IPoolControl poolControl)
{
    // Lifecycle / capacity management
    poolControl.PrewarmPool(count);                 // create N inactive instances
    poolControl.Clear();                            // destroy all inactive (keeps active)
    poolControl.ShrinkInactive(targetInactive: 4);  // trim cached inactive down to N
    poolControl.Reseed(rePrewarm: true);            // force-flush + rebuild (added v1.0.2)
    poolControl.DestroyPool();                      // destroy everything; pool is then invalid

    // Batch spawn — extension methods on IPoolControl
    poolControl.SpawnBatch(positions, rotations, parent: null);
    poolControl.SpawnGrid(center, spacing: 0.5f, gridSize: 8);

    // Diagnostics
    PoolMetrics m = poolControl.Metrics;
    int total = poolControl.TotalCount;
}
```

## Configuration

### Enable Debug Logging

Add `ENABLE_POOL_LOGS` to your Scripting Define Symbols:
1. `Edit > Project Settings > Player > Other Settings`
2. Add `ENABLE_POOL_LOGS` to Scripting Define Symbols
3. Logs will completely compile out when the symbol is removed (zero overhead)

### Assembly Definitions

PoolMaster uses assembly definitions for clean separation:
- `PoolMaster` - Core runtime assembly
- `PoolMaster.Editor` - Editor-only tools

To reference PoolMaster in your code, add `PoolMaster` to your assembly definition references.

## Migration Guide

### From Unity's Built-in ObjectPool

```csharp
// Before (Unity ObjectPool)
using UnityEngine.Pool;

var pool = new ObjectPool<GameObject>(
    createFunc: () => Instantiate(prefab),
    actionOnGet: (obj) => obj.SetActive(true),
    actionOnRelease: (obj) => obj.SetActive(false),
    actionOnDestroy: (obj) => Destroy(obj),
    collectionCheck: true,
    defaultCapacity: 20,
    maxSize: 100
);

var obj = pool.Get();
pool.Release(obj);

// After (PoolMaster)
using PoolMaster;

var request = new PoolRequest
{
    prefab = prefab,
    initialPoolSize = 20,
    maxPoolSize = 100,
    shouldPrewarm = true
};

PoolingManager.Instance.GetOrCreatePool<MyComponent>(request);
var obj = PoolingManager.Instance.Spawn(prefab, position, rotation);
PoolingManager.Instance.Despawn(obj);
```

### From Manual Pooling

```csharp
// Before (Manual pooling)
Queue<GameObject> pool = new Queue<GameObject>();

GameObject Spawn()
{
    if (pool.Count > 0)
    {
        var obj = pool.Dequeue();
        obj.SetActive(true);
        return obj;
    }
    return Instantiate(prefab);
}

void Despawn(GameObject obj)
{
    obj.SetActive(false);
    pool.Enqueue(obj);
}

// After (PoolMaster)
using PoolMaster;

// One-time setup
var request = new PoolRequest { prefab = prefab, initialPoolSize = 10 };
PoolingManager.Instance.GetOrCreatePool<MyComponent>(request);

// Use anywhere
var obj = PoolingManager.Instance.Spawn(prefab, position, rotation);
obj.ReturnToPool(); // Extension method
```

## Best Practices

1. **Always implement IPoolable** - Even if empty, it ensures proper lifecycle hooks
2. **Use PoolableMonoBehaviour** - Handles common cleanup patterns automatically
3. **Pre-warm pools on load** - Avoid runtime hitches with `shouldPrewarm = true`
4. **Set reasonable max sizes** - Use `maxPoolSize` to prevent unbounded memory growth
5. **Enable culling** - Use `cullExcessObjects = true` to manage memory
6. **Use batch operations** - Spawn/despawn multiple objects in one call when possible
7. **Profile your pools** - Use the diagnostics window to optimize pool sizes
8. **Disable logs in production** - Remove `ENABLE_POOL_LOGS` for zero logging overhead

## FAQ

**Q: Can I use PoolMaster with addressables?**  
A: Yes! Pass the loaded addressable as the prefab parameter.

**Q: Does it work with nested prefabs?**  
A: Yes, PoolMaster handles any GameObject prefab regardless of hierarchy.

**Q: What about pooling across scene loads?**  
A: PoolingManager persists across scenes with `DontDestroyOnLoad`. Pools survive scene transitions.

**Q: Can I have multiple pools for the same prefab?**  
A: Yes, provide unique `poolId` parameters when creating pools.

**Q: Is it thread-safe?**  
A: Core pooling must happen on the main thread, but use `PoolCommandBuffer` for thread-safe enqueueing.

**Q: Does it work with ECS/DOTS?**  
A: PoolMaster is designed for GameObject-based workflows. For DOTS, use Unity's native entity pooling.

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Contributing

- Enable repo hooks: `git config core.hooksPath .githooks`
- Verify CSharpier: `csharpier --version` (required for formatting)
- Format manually if needed: `csharpier format .`

See full dev setup: [DEVELOPING.md](DEVELOPING.md)

## Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/PoolMaster/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/PoolMaster/discussions)
- **Discord**: misty2023

---

**If PoolMaster helps your project, consider giving it a star on GitHub!**
