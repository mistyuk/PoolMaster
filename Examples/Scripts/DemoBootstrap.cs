// ============================================================================
// PoolMaster - Demo Bootstrap (Unity 6.4 / URP)
//
// One-file, zero-asset demo for PoolMaster. Drop the parent scene
// (DemoScene.unity) into your Hierarchy and press Play. This component
// programmatically builds:
//
//   • Camera, directional light, floor
//   • Four pool "template" GameObjects (registered with PoolingManager)
//   • UI Toolkit HUD with mode tabs, instructions, live metrics
//
// Five demo modes are bound to the HUD tabs:
//   1. Basic Spawn      — single-prefab Spawn/Despawn
//   2. Batch Spawn      — IPoolControl.SpawnBatch / SpawnGrid
//   3. Projectile Storm — continuous fire with auto-despawn via PooledProjectile
//   4. Particle Burst   — pooled ParticleSystem via PooledVfx
//   5. Metrics          — live pool stats + shortcut to Diagnostics window
//
// No external dependencies beyond Unity built-in modules + URP for the Lit
// shader (Standard is used as fallback if URP is absent).
// ============================================================================

using System.Collections.Generic;
using PoolMaster;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoolMaster.Examples
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class DemoBootstrap : MonoBehaviour
    {
        // ── Constants ──────────────────────────────────────────────────────
        private const string PoolIdCube = "Demo.Cube";
        private const string PoolIdSphere = "Demo.Sphere";
        private const string PoolIdProjectile = "Demo.Projectile";
        private const string PoolIdBurst = "Demo.Burst";
        private const string PoolIdPlainCube = "Demo.PlainCube";   // GameObjectPool demo

        private const float SpawnAreaRadius = 4f;
        private const float ProjectileSpawnHeight = 0.5f;

        // ── Demo mode ──────────────────────────────────────────────────────
        public enum Mode { BasicSpawn, BatchSpawn, ProjectileStorm, ParticleBurst, GameObjectPool, StressTest, Metrics }

        // ── Inspector ──────────────────────────────────────────────────────
        [Header("Layout")]
        [SerializeField, Tooltip("Hide the HUD (useful when capturing footage).")]
        private bool _hideHud = false;

        // ── Runtime state ──────────────────────────────────────────────────
        private GameObject _cubeTemplate;
        private GameObject _sphereTemplate;
        private GameObject _projectileTemplate;
        private GameObject _burstTemplate;
        private GameObject _plainCubeTemplate;   // no IPoolable — pooled via GameObjectPool

        // Stored as IPoolControl so we can hit Metrics, ShrinkInactive, Clear etc.
        // directly without runtime casts. Pool<T> implements both IPool and IPoolControl.
        private IPoolControl _cubePool;
        private IPoolControl _spherePool;
        private IPoolControl _projectilePool;
        private IPoolControl _burstPool;
        private IPoolControl _plainCubePool;     // backed by GameObjectPool

        // Tracks active instances for modes that need manual despawn buttons
        // (no auto-return component on the prefab).
        private readonly Stack<GameObject> _basicActiveStack = new Stack<GameObject>(64);
        private readonly Stack<GameObject> _plainActiveStack = new Stack<GameObject>(64);

        // Tracks the current sphere grid so we can recycle before laying down a new one.
        private readonly List<GameObject> _batchActive = new List<GameObject>(512);

        // Mode state
        private Mode _mode = Mode.BasicSpawn;
        private float _stormCooldown;
        private const float StormFireInterval = 0.04f; // 25 Hz

        // UI
        private UIDocument _uiDoc;
        private VisualElement _hudRoot;
        private VisualElement _modePanel;
        private Label _modeInstructions;
        private Label _metricsLabel;
        private Button[] _tabButtons;

        // ScriptableObjects created via CreateInstance — must be destroyed in OnDestroy
        // (they don't follow GameObject lifecycle, so they leak across Play sessions
        // in the editor otherwise).
        private PanelSettings _panelSettings;
        private ThemeStyleSheet _emptyTheme;

        // Reused StringBuilder for the metrics label so we don't allocate one every
        // 0.25 seconds. Pre-sized to typical content length (~5 pool lines).
        private readonly System.Text.StringBuilder _metricsSb = new System.Text.StringBuilder(256);

        // Metrics refresh cadence — slow enough that the readout doesn't flicker,
        // fast enough that pool state changes are visible.
        private float _metricsRefreshTimer;
        private const float MetricsRefreshInterval = 0.25f;

        // ══════════════════════════════════════════════════════════════════
        //  Unity Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            EnsurePoolingManager();
            BuildScene();
            BuildTemplates();
            RegisterPools();
            BuildHud();
            ApplyMode(Mode.BasicSpawn);
            // Kick off GPU warmup so the first burst doesn't pay the
            // CreateCommittedResource cost mid-interaction.
            StartCoroutine(WarmupGpuResources());
        }

        private void OnDestroy()
        {
            // Templates are children of the bootstrap's transform — Unity destroys them
            // automatically when the bootstrap GameObject is destroyed. PoolingManager
            // also clears its state on OnApplicationQuit via the singleton hardening.
            //
            // ScriptableObjects created via CreateInstance, however, do NOT follow the
            // GameObject lifecycle. We have to destroy them explicitly or they accumulate
            // across editor Play sessions.
            if (_panelSettings != null) Destroy(_panelSettings);
            if (_emptyTheme != null) Destroy(_emptyTheme);
        }

        private void Update()
        {
            switch (_mode)
            {
                case Mode.ProjectileStorm:
                    _stormCooldown -= Time.deltaTime;
                    if (_stormCooldown <= 0f)
                    {
                        FireStormProjectile();
                        _stormCooldown = StormFireInterval;
                    }
                    break;
            }

            _metricsRefreshTimer -= Time.deltaTime;
            if (_metricsRefreshTimer <= 0f)
            {
                RefreshMetricsLabel();
                _metricsRefreshTimer = MetricsRefreshInterval;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Scene Setup
        // ══════════════════════════════════════════════════════════════════

        private void EnsurePoolingManager()
        {
            // PoolingManager auto-creates itself on first Instance access in Play Mode.
            // Touching it here ensures the singleton exists before we register pools.
            _ = PoolingManager.Instance;
        }

        private void BuildScene()
        {
            // ── Camera ──
            if (Camera.main == null)
            {
                var camGo = new GameObject("Demo Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
                cam.fieldOfView = 60f;
                camGo.transform.position = new Vector3(0f, 6.5f, -10f);
                camGo.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
                camGo.AddComponent<AudioListener>();
            }

            // ── Directional light ──
            var lightGo = new GameObject("Demo Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.96f, 0.88f);
            lightGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

            // ── Floor ──
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Demo Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f); // 20×20 m
            var floorRenderer = floor.GetComponent<Renderer>();
            floorRenderer.sharedMaterial = CreateUrpMaterial(new Color(0.18f, 0.18f, 0.22f), metallic: 0f, smoothness: 0.1f);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Pool Template Construction
        // ══════════════════════════════════════════════════════════════════

        private void BuildTemplates()
        {
            // Templates live under this transform; they're disabled so they don't
            // appear in the scene as visible objects. PoolingManager will Instantiate()
            // copies from them on Spawn().
            var templatesRoot = new GameObject("Templates").transform;
            templatesRoot.SetParent(transform);
            templatesRoot.gameObject.SetActive(false);

            // Lifetimes are tuned for human readability — long enough to follow each
            // spawn visually and click "Despawn newest" before the lifetime timer fires.
            _cubeTemplate = BuildCubeTemplate(templatesRoot, new Color(0.30f, 0.85f, 1.00f), lifetime: 5.0f, name: "CubeTemplate");
            _sphereTemplate = BuildSphereTemplate(templatesRoot, new Color(1.00f, 0.85f, 0.30f), lifetime: 2.0f, name: "SphereTemplate");
            _projectileTemplate = BuildProjectileTemplate(templatesRoot, new Color(1.00f, 0.45f, 0.30f), lifetime: 4.0f);
            // Burst template is colour-agnostic — the actual hue is set per-spawn via
            // a MaterialPropertyBlock so each "firework" can be a different colour.
            _burstTemplate = BuildBurstTemplate(templatesRoot);
            // Plain cube has NO IPoolable / PoolableMonoBehaviour component — proves
            // that GameObjectPool can pool any prefab via the v1.0.2 fallback path.
            _plainCubeTemplate = BuildPlainCubeTemplate(templatesRoot, new Color(0.55f, 0.95f, 0.45f), "PlainCubeTemplate");
        }

        private GameObject BuildCubeTemplate(Transform parent, Color color, float lifetime, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localScale = Vector3.one * 0.6f;
            go.GetComponent<Renderer>().sharedMaterial = CreateUrpMaterial(color, metallic: 0.1f, smoothness: 0.6f);

            // Remove the default collider — we don't need physics interactions for the basic demo
            DestroyImmediate(go.GetComponent<Collider>());

            var poolable = go.AddComponent<SimplePoolableObject>();
            SetPrivateField(poolable, "lifetime", lifetime);
            SetPrivateField(poolable, "autoDestroy", true);

            return go;
        }

        private GameObject BuildSphereTemplate(Transform parent, Color color, float lifetime, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localScale = Vector3.one * 0.45f;
            go.GetComponent<Renderer>().sharedMaterial = CreateUrpMaterial(color, metallic: 0.2f, smoothness: 0.7f);
            DestroyImmediate(go.GetComponent<Collider>());

            var poolable = go.AddComponent<SimplePoolableObject>();
            SetPrivateField(poolable, "lifetime", lifetime);
            SetPrivateField(poolable, "autoDestroy", true);

            return go;
        }

        /// <summary>
        /// Builds a plain cube prefab with NO IPoolable / PoolableMonoBehaviour component.
        /// Pooled via the new v1.0.2 GameObjectPool path. The pool's PooledMarker fast-path
        /// still works for despawn — <c>gameObject.ReturnToPool()</c> handles the lookup —
        /// but there's no lifetime timer, so the user controls spawn/despawn manually.
        /// </summary>
        private GameObject BuildPlainCubeTemplate(Transform parent, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localScale = Vector3.one * 0.55f;
            go.GetComponent<Renderer>().sharedMaterial = CreateUrpMaterial(color, metallic: 0.1f, smoothness: 0.5f);
            DestroyImmediate(go.GetComponent<Collider>());
            // Intentionally NO component added. Plain GameObject with just a mesh + renderer.
            return go;
        }

        private GameObject BuildProjectileTemplate(Transform parent, Color color, float lifetime)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ProjectileTemplate";
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localScale = new Vector3(0.25f, 0.25f, 0.55f);
            var renderer = go.GetComponent<Renderer>();
            var mat = CreateUrpMaterial(color, metallic: 0.5f, smoothness: 0.85f);
            // Give projectiles a subtle emission so they stand out
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.6f);
            renderer.sharedMaterial = mat;

            // Remove the default collider (projectile demo uses lifetime-based despawn, not collision)
            DestroyImmediate(go.GetComponent<Collider>());

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            var proj = go.AddComponent<PooledProjectile>();
            SetPrivateField(proj, "lifetime", lifetime);
            SetPrivateField(proj, "autoReturnOnLifetime", true);
            SetPrivateField(proj, "autoReturnOnImpact", false);
            SetPrivateField(proj, "useGravity", false);
            SetPrivateField(proj, "initialSpeed", 0f); // we set velocity per-spawn via LaunchWithVelocity

            return go;
        }

        private GameObject BuildBurstTemplate(Transform parent)
        {
            var go = new GameObject("BurstTemplate");
            go.transform.SetParent(parent, worldPositionStays: false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 1.4f;
            main.startSpeed = 5.0f;
            main.startSize = 0.20f;
            main.startColor = Color.white;        // tinted per-spawn
            main.gravityModifier = 0.0f;          // particles burst outward and fade
            main.maxParticles = 60;
            main.stopAction = ParticleSystemStopAction.None;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;             // PooledVfx triggers Play() in OnSpawned

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // SetBurst(index, …) writes into an EXISTING slot — it does not grow the array.
            // A fresh EmissionModule has burstCount=0, so SetBurst(0, …) is rejected with
            // "burst will be ignored". SetBursts(array) replaces the whole array.
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 25),
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            // Shrink particles over their lifetime so the burst "fades" visually even
            // though we're using an opaque shader. Curve goes 1.0 → 0.0.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            // Mesh-mode rendering with the built-in sphere — proven shader path,
            // none of the URP transparent-variant keyword nonsense.
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetBuiltinSphereMesh();

            // ── Performance flags ──────────────────────────────────────────────
            // Particles don't need to cast or receive shadows. With shadows on, 12
            // bursts × 25 particles spawn ~300 shadowmap draws per click. Disabling
            // shadows + reflection/light probes drops cost 3-5× on the heaviest
            // demos.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // White base material — per-burst colour comes from a MaterialPropertyBlock
            // applied at spawn time. URP Lit honours _BaseColor and _EmissionColor
            // overrides via MPB without breaking GPU instancing for same-block batches.
            var burstMat = CreateUrpMaterial(Color.white, metallic: 0.2f, smoothness: 0.6f);
            burstMat.EnableKeyword("_EMISSION");
            burstMat.SetColor("_EmissionColor", Color.white); // gets overridden per-burst
            burstMat.enableInstancing = true;
            renderer.sharedMaterial = burstMat;

            var vfx = go.AddComponent<PooledVfx>();
            SetPrivateField(vfx, "autoReturnWhenFinished", true);
            SetPrivateField(vfx, "useMaxLifetime", true);
            SetPrivateField(vfx, "maxLifetime", 2.5f);
            SetPrivateField(vfx, "playOnSpawn", true);
            SetPrivateField(vfx, "stopOnDespawn", true);
            SetPrivateField(vfx, "clearOnDespawn", true);

            return go;
        }

        // ── Firework palette ──────────────────────────────────────────────
        // Eight saturated hues. Random pick for single bursts, indexed walk
        // for ring bursts so a ring of 12 cycles cleanly through the palette.
        private static readonly Color[] FireworkPalette =
        {
            new Color(1.00f, 0.30f, 0.30f), // red
            new Color(1.00f, 0.55f, 0.20f), // orange
            new Color(1.00f, 0.85f, 0.20f), // yellow
            new Color(0.45f, 0.95f, 0.35f), // lime green
            new Color(0.20f, 0.90f, 0.85f), // cyan
            new Color(0.35f, 0.55f, 1.00f), // royal blue
            new Color(0.80f, 0.40f, 1.00f), // violet
            new Color(1.00f, 0.45f, 0.85f), // hot pink
        };

        private MaterialPropertyBlock _burstMpb;

        /// <summary>
        /// Gets the built-in sphere mesh by spawning a primitive briefly and harvesting
        /// its sharedMesh reference. The mesh asset is global so destroying the temp
        /// GameObject does not invalidate the reference.
        /// </summary>
        private static Mesh _cachedSphereMesh;
        private static Mesh GetBuiltinSphereMesh()
        {
            if (_cachedSphereMesh != null) return _cachedSphereMesh;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cachedSphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            // Strip the collider before destroying so we don't trip physics warnings.
            var col = temp.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            Destroy(temp);
            return _cachedSphereMesh;
        }

        private void RegisterPools()
        {
            // Pool<T> implements IPoolControl, so the cast is safe.
            // Burst pool is prewarmed because the FIRST burst pays for D3D12 GPU buffer
            // allocation (CreateCommittedResourceWithTag, ~60ms hitch). Prewarming the
            // GameObjects + a follow-up warmup coroutine that briefly plays one burst
            // moves that allocation to game start, where the user expects loading time.
            _cubePool = (IPoolControl)PoolingManager.Instance.GetOrCreatePool<SimplePoolableObject>(
                BuildRequest(_cubeTemplate, PoolIdCube, maxSize: 128, prewarm: 8));

            _spherePool = (IPoolControl)PoolingManager.Instance.GetOrCreatePool<SimplePoolableObject>(
                BuildRequest(_sphereTemplate, PoolIdSphere, maxSize: 512));

            _projectilePool = (IPoolControl)PoolingManager.Instance.GetOrCreatePool<PooledProjectile>(
                BuildRequest(_projectileTemplate, PoolIdProjectile, maxSize: 256, prewarm: 12));

            _burstPool = (IPoolControl)PoolingManager.Instance.GetOrCreatePool<PooledVfx>(
                BuildRequest(_burstTemplate, PoolIdBurst, maxSize: 32, prewarm: 12));

            // GameObjectPool path — note the different API (GetOrCreateGameObjectPool)
            // because the prefab has no IPoolable component. This is the v1.0.2
            // headline feature: pool any prefab, no scripts required.
            _plainCubePool = (IPoolControl)PoolingManager.Instance.GetOrCreateGameObjectPool(
                _plainCubeTemplate,
                BuildRequest(_plainCubeTemplate, PoolIdPlainCube, maxSize: 128));
        }

        private static PoolRequest BuildRequest(GameObject prefab, string id, int maxSize, int prewarm = 0)
        {
            var req = PoolRequest.Create(prefab, initialSize: prewarm, shouldPrewarm: prewarm > 0);
            req.poolId = id;
            req.maxPoolSize = maxSize;
            req.allowDynamicExpansion = true;
            req.usePoolContainer = true;
            req.containerName = $"Pool_{id}";
            req.category = "Demo";
            req.initializationTiming = PoolInitializationTiming.Immediate;
            return req;
        }

        /// <summary>
        /// Spawns one instance of each pool below the floor for one frame to force GPU
        /// resource allocation (vertex buffers, instance data buffers, etc.) up front.
        /// Without this, the FIRST user interaction pays the cost — typically visible as
        /// a 50-100ms hitch on the first particle burst (CreateCommittedResourceWithTag
        /// in the D3D12 backend).
        /// </summary>
        private System.Collections.IEnumerator WarmupGpuResources()
        {
            // Skip one frame so the initial scene renders normally first.
            yield return null;

            var hidden = new Vector3(0f, -500f, 0f); // way below camera frustum

            if (_cubePool != null) { var g = _cubePool.Spawn(hidden, Quaternion.identity, null); if (g != null) g.ReturnToPool(); }
            if (_spherePool != null) { var g = _spherePool.Spawn(hidden, Quaternion.identity, null); if (g != null) g.ReturnToPool(); }
            if (_projectilePool != null) { var g = _projectilePool.Spawn(hidden, Quaternion.identity, null); if (g != null) g.ReturnToPool(); }
            if (_plainCubePool != null) { var g = _plainCubePool.Spawn(hidden, Quaternion.identity, null); if (g != null) g.ReturnToPool(); }

            // For the burst we need the particle system to actually PLAY for one frame
            // — that's when the GPU instance buffer gets allocated. The PooledVfx will
            // auto-return after its lifetime; we don't track the ref.
            if (_burstPool != null) SpawnFireworkBurst(hidden, Color.white);

            // One more frame to let the burst's first emission complete on the GPU.
            yield return null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI Toolkit HUD
        // ══════════════════════════════════════════════════════════════════

        private void BuildHud()
        {
            if (_hideHud) return;

            // UIDocument lives on a child GameObject so we can position/clean it
            // independently of the bootstrap.
            var uiGo = new GameObject("Demo HUD");
            uiGo.transform.SetParent(transform);

            _uiDoc = uiGo.AddComponent<UIDocument>();

            // Programmatic PanelSettings — no asset required. Stored as fields so we
            // can destroy them in OnDestroy (ScriptableObjects don't auto-cleanup).
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "Demo Panel Settings";
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            _panelSettings.match = 0.5f;
            _panelSettings.sortingOrder = 0f;
            // Empty ThemeStyleSheet silences the "No Theme Style Sheet set" warning.
            // We don't want its rules — every element is styled inline — but UIDocument
            // logs noise if the slot is null.
            _emptyTheme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _emptyTheme.name = "Demo Empty Theme";
            _panelSettings.themeStyleSheet = _emptyTheme;
            _uiDoc.panelSettings = _panelSettings;

            var root = _uiDoc.rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            // Center the HUD horizontally on the cross axis (column flex → cross is X).
            root.style.alignItems = Align.Center;
            root.pickingMode = PickingMode.Ignore;

            // Without a runtime theme, UITK has no font registered and every Label/Button
            // renders blank backgrounds. Programmatic PanelSettings doesn't auto-link the
            // default theme. We solve it by assigning a built-in font on the root — UITK
            // cascades font through descendants, so every text element inherits it.
            var font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
                root.style.unityFontDefinition = new StyleFontDefinition(font);
            else
                Debug.LogWarning("[DemoBootstrap] Built-in runtime font not found — HUD text may not render.");

            // Compact centered HUD: caps at 960px wide on big screens, shrinks
            // gracefully on narrower aspect ratios. No vertical stretch — sizes
            // to its content so the 3D scene below isn't unnecessarily covered.
            _hudRoot = new VisualElement { name = "hud-root" };
            _hudRoot.style.flexDirection = FlexDirection.Column;
            _hudRoot.style.alignSelf = Align.Center;
            _hudRoot.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _hudRoot.style.maxWidth = 960;
            _hudRoot.style.marginTop = 16;
            _hudRoot.style.paddingLeft = 20; _hudRoot.style.paddingRight = 20;
            _hudRoot.style.paddingTop = 16;  _hudRoot.style.paddingBottom = 16;
            // Subtle backdrop card so the HUD reads as a distinct block, not as
            // floating elements on top of the 3D scene.
            _hudRoot.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.08f, 0.78f));
            _hudRoot.style.borderTopLeftRadius = 10; _hudRoot.style.borderTopRightRadius = 10;
            _hudRoot.style.borderBottomLeftRadius = 10; _hudRoot.style.borderBottomRightRadius = 10;
            _hudRoot.style.borderLeftWidth = 1; _hudRoot.style.borderRightWidth = 1;
            _hudRoot.style.borderTopWidth = 1; _hudRoot.style.borderBottomWidth = 1;
            var hudBorder = new StyleColor(new Color(0.16f, 0.18f, 0.22f));
            _hudRoot.style.borderLeftColor = hudBorder; _hudRoot.style.borderRightColor = hudBorder;
            _hudRoot.style.borderTopColor = hudBorder;  _hudRoot.style.borderBottomColor = hudBorder;
            root.Add(_hudRoot);

            BuildHeader();
            BuildModeTabs();
            BuildModePanel();
            BuildFooter();
        }

        private void BuildHeader()
        {
            // Stacked column so subtitle never gets pushed off-screen at narrow aspect
            // ratios, and both are centered for a tighter visual focal point.
            var header = new VisualElement { name = "header" };
            header.style.flexDirection = FlexDirection.Column;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 14;

            var title = new Label("PoolMaster — Demo");
            title.style.fontSize = 30;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(Color.white);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.letterSpacing = 1f;
            header.Add(title);

            var subtitle = new Label("Five live pools  •  zero per-frame allocations on the hot path");
            subtitle.style.fontSize = 13;
            subtitle.style.unityFontStyleAndWeight = FontStyle.Normal;
            subtitle.style.color = new StyleColor(new Color(0.62f, 0.66f, 0.74f));
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            subtitle.style.marginTop = 4;
            header.Add(subtitle);

            _hudRoot.Add(header);
        }

        private void BuildModeTabs()
        {
            var tabs = new VisualElement { name = "tabs" };
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 10;

            // Shortened "Projectile Storm" → "Projectiles" and "Particle Burst" → "Fireworks"
            // so the 7-tab row fits comfortably at 960px-wide HUD.
            var labels = new[] { "Basic Spawn", "Batch Spawn", "Projectiles", "Fireworks", "GO Pool", "Stress Test", "Metrics" };
            _tabButtons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var btn = new Button(() => ApplyMode((Mode)captured)) { text = labels[i] };
                btn.style.flexGrow = 1f;
                btn.style.flexBasis = 0;        // equal width regardless of label length
                btn.style.marginRight = i == labels.Length - 1 ? 0 : 6;
                btn.style.marginLeft = 0;
                StyleTabButton(btn, isActive: false);
                _tabButtons[i] = btn;
                tabs.Add(btn);
            }
            _hudRoot.Add(tabs);
        }

        private void BuildModePanel()
        {
            _modePanel = new VisualElement { name = "mode-panel" };
            _modePanel.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.13f, 0.92f));
            _modePanel.style.paddingLeft = 18; _modePanel.style.paddingRight = 18;
            _modePanel.style.paddingTop = 14; _modePanel.style.paddingBottom = 14;
            _modePanel.style.borderTopLeftRadius = 8; _modePanel.style.borderTopRightRadius = 8;
            _modePanel.style.borderBottomLeftRadius = 8; _modePanel.style.borderBottomRightRadius = 8;
            _modePanel.style.borderLeftWidth = 1; _modePanel.style.borderRightWidth = 1;
            _modePanel.style.borderTopWidth = 1; _modePanel.style.borderBottomWidth = 1;
            var borderColor = new StyleColor(new Color(0.18f, 0.20f, 0.26f));
            _modePanel.style.borderLeftColor = borderColor; _modePanel.style.borderRightColor = borderColor;
            _modePanel.style.borderTopColor = borderColor; _modePanel.style.borderBottomColor = borderColor;
            _modePanel.style.marginBottom = 10;

            _modeInstructions = new Label();
            _modeInstructions.style.whiteSpace = WhiteSpace.Normal;
            _modeInstructions.style.fontSize = 14;
            _modeInstructions.style.color = new StyleColor(new Color(0.90f, 0.91f, 0.94f));
            _modeInstructions.style.marginBottom = 14;
            _modePanel.Add(_modeInstructions);

            // Per-mode action row (populated by ApplyMode)
            var actionRow = new VisualElement { name = "action-row" };
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.flexWrap = Wrap.Wrap;
            actionRow.style.justifyContent = Justify.FlexStart;
            _modePanel.Add(actionRow);

            _hudRoot.Add(_modePanel);
        }

        private void BuildFooter()
        {
            var footer = new VisualElement { name = "footer" };
            footer.style.flexDirection = FlexDirection.Column;
            footer.style.alignItems = Align.Center;
            footer.style.marginTop = 6;

            _metricsLabel = new Label();
            _metricsLabel.style.fontSize = 12;
            _metricsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _metricsLabel.style.color = new StyleColor(new Color(0.86f, 0.88f, 0.92f));
            _metricsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _metricsLabel.style.whiteSpace = WhiteSpace.Normal; // allow wrap when 5 pools listed
            footer.Add(_metricsLabel);

            var hint = new Label("Open  Window  →  PoolMaster  →  Diagnostics  for a live editor view");
            hint.style.fontSize = 11;
            hint.style.color = new StyleColor(new Color(0.50f, 0.54f, 0.62f));
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.marginTop = 4;
            footer.Add(hint);

            _hudRoot.Add(footer);
        }

        private static void StyleTabButton(Button btn, bool isActive)
        {
            btn.style.paddingTop = 12; btn.style.paddingBottom = 12;
            btn.style.fontSize = 14;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.color = new StyleColor(isActive ? Color.white : new Color(0.72f, 0.76f, 0.84f));
            btn.style.backgroundColor = new StyleColor(isActive
                ? new Color(0.22f, 0.50f, 0.92f)
                : new Color(0.13f, 0.15f, 0.19f));
            btn.style.borderTopWidth = 0; btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0; btn.style.borderRightWidth = 0;
            btn.style.borderTopLeftRadius = 6; btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6; btn.style.borderBottomRightRadius = 6;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode Switching
        // ══════════════════════════════════════════════════════════════════

        private void ApplyMode(Mode mode)
        {
            _mode = mode;

            for (int i = 0; i < _tabButtons.Length; i++)
                StyleTabButton(_tabButtons[i], isActive: i == (int)mode);

            var actionRow = _modePanel.Q<VisualElement>("action-row");
            actionRow.Clear();

            switch (mode)
            {
                case Mode.BasicSpawn:
                    _modeInstructions.text =
                        "Basic Spawn — the core API.\n" +
                        "Each click pulls a cube from the pool, places it at a random spot, and auto-returns it after 2 seconds via SimplePoolableObject's lifetime timer.\n" +
                        "First spawn triggers the pool to create instances on demand; subsequent spawns reuse them.";
                    AddActionButton(actionRow, "Spawn cube",       BasicSpawnOne);
                    AddActionButton(actionRow, "Spawn 10",         () => { for (int i = 0; i < 10; i++) BasicSpawnOne(); });
                    AddActionButton(actionRow, "Despawn newest",   BasicDespawnNewest);
                    AddActionButton(actionRow, "Despawn all",      BasicDespawnAll);
                    break;

                case Mode.BatchSpawn:
                    _modeInstructions.text =
                        "Batch Spawn — IPoolControlBatchExtensions.\n" +
                        "Uses SpawnGrid() to lay out spheres in a regular pattern via one call. Each sphere has a short lifetime and self-recycles, so the pool size stabilises after a few grids.";
                    AddActionButton(actionRow, "Grid 5×5 (25)",    () => BatchSpawnGrid(5));
                    AddActionButton(actionRow, "Grid 10×10 (100)", () => BatchSpawnGrid(10));
                    AddActionButton(actionRow, "Grid 16×16 (256)", () => BatchSpawnGrid(16));
                    AddActionButton(actionRow, "Shrink to 0",      () => _spherePool?.ShrinkInactive(0));
                    break;

                case Mode.ProjectileStorm:
                    _modeInstructions.text =
                        "Projectile Storm — PooledProjectile.\n" +
                        "A continuous 25 Hz fire stream. Each projectile launches with a randomized velocity and auto-despawns after its lifetime expires. Toggle below to pause.";
                    AddActionButton(actionRow, "Fire one",         FireStormProjectile);
                    AddActionButton(actionRow, "Pause storm",      () => _mode = Mode.BasicSpawn);
                    AddActionButton(actionRow, "Burst of 20",      () => { for (int i = 0; i < 20; i++) FireStormProjectile(); });
                    break;

                case Mode.ParticleBurst:
                    _modeInstructions.text =
                        "Fireworks — pooled bursts via PooledVfx.\n" +
                        "Each burst is one Spawn() returning a recycled ParticleSystem. Colour is set per-spawn via a MaterialPropertyBlock so every burst can be a different hue without breaking GPU instancing. PooledVfx auto-returns each GO when the emission finishes (~1.4s), so the active count stays near zero while the inactive count rises.\n\n" +
                        "Reseed force-flushes every active firework and rebuilds the pool from scratch — useful after editing the prefab at runtime.";
                    AddActionButton(actionRow, "Pop firework",        PopParticleBurst);
                    AddActionButton(actionRow, "Ring of 5",            () => PopBurstRing(count: 5));
                    AddActionButton(actionRow, "Ring of 8 (rainbow)",  () => PopBurstRing(count: 8));
                    AddActionButton(actionRow, "Ring of 12",           () => PopBurstRing(count: 12));
                    AddActionButton(actionRow, "Reseed burst pool",    () => _burstPool?.Reseed(rePrewarm: true));
                    break;

                case Mode.GameObjectPool:
                    _modeInstructions.text =
                        "GameObjectPool — pool ANY prefab, no scripts required.\n" +
                        "These green cubes have no IPoolable / PoolableMonoBehaviour component — they're plain primitives with a mesh + renderer. Registered via PoolingManager.GetOrCreateGameObjectPool(prefab, request), they pool exactly like IPoolable prefabs do, including despawn-via-PooledMarker. Manual despawn buttons because there's no lifetime timer.";
                    AddActionButton(actionRow, "Spawn one",        PlainCubeSpawnOne);
                    AddActionButton(actionRow, "Spawn 20",         () => { for (int i = 0; i < 20; i++) PlainCubeSpawnOne(); });
                    AddActionButton(actionRow, "Despawn newest",   PlainCubeDespawnNewest);
                    AddActionButton(actionRow, "Despawn all",      PlainCubeDespawnAll);
                    break;

                case Mode.StressTest:
                    _modeInstructions.text =
                        "Stress Test — same cube pool as Basic Spawn, hammered.\n" +
                        "Each click spawns N cubes at random positions in a 10×10 m area; they auto-despawn via SimplePoolableObject's 5-second lifetime timer. After the first wave fills the pool, subsequent waves reuse existing inactive instances — watch ReuseEfficiency climb toward 100%. The frame spike on the very first 5000-click is Instantiate() doing GameObject allocation, not the pool itself.\n\n" +
                        "Cull idle (30s) destroys any pool that has been idle for ≥30s — for the demo we re-register the affected pools immediately afterwards so subsequent demos still work.";
                    AddActionButton(actionRow, "Burst 500",       () => StressSpawn(500));
                    AddActionButton(actionRow, "Burst 1000",      () => StressSpawn(1000));
                    AddActionButton(actionRow, "Burst 2000",      () => StressSpawn(2000));
                    AddActionButton(actionRow, "Burst 5000",      () => StressSpawn(5000));
                    AddActionButton(actionRow, "Cull idle (30s)", () => CullIdlePoolsAndReregister(30f));
                    break;

                case Mode.Metrics:
                    _modeInstructions.text =
                        "Metrics — what PoolingManager exposes for diagnostics.\n" +
                        "Below: per-pool active/inactive/total and ReuseEfficiency. For a richer real-time view (graphs, per-pool culling), open Window → PoolMaster → Diagnostics. The buttons below stress the cube pool so you can watch numbers move.\n\n" +
                        "Clear All Inactive destroys cached instances (Active count untouched). Cull idle destroys whole pools that have been idle for ≥60s — the demo re-registers them immediately so subsequent demos keep working.";
                    AddActionButton(actionRow, "Spawn 20 cubes",     () => { for (int i = 0; i < 20; i++) BasicSpawnOne(); });
                    AddActionButton(actionRow, "Despawn all cubes",  BasicDespawnAll);
                    AddActionButton(actionRow, "Clear all inactive", () => PoolingManager.Instance.ClearAllPools());
                    AddActionButton(actionRow, "Cull idle (60s)",    () => CullIdlePoolsAndReregister(60f));
                    break;
            }

            RefreshMetricsLabel();
        }

        private static void AddActionButton(VisualElement row, string label, System.Action onClick)
        {
            var btn = new Button(onClick) { text = label };
            btn.style.marginRight = 8;
            btn.style.marginBottom = 8;
            btn.style.marginLeft = 0;
            btn.style.marginTop = 0;
            btn.style.paddingTop = 9; btn.style.paddingBottom = 9;
            btn.style.paddingLeft = 16; btn.style.paddingRight = 16;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new StyleColor(new Color(0.22f, 0.24f, 0.30f));
            btn.style.color = new StyleColor(new Color(0.96f, 0.97f, 1.00f));
            btn.style.borderTopWidth = 0; btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0; btn.style.borderRightWidth = 0;
            btn.style.borderTopLeftRadius = 6; btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6; btn.style.borderBottomRightRadius = 6;
            row.Add(btn);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode 1 — Basic Spawn
        // ══════════════════════════════════════════════════════════════════

        private void BasicSpawnOne()
        {
            if (_cubePool == null) return;

            var pos = RandomPointInSpawnArea(y: 0.3f);
            var go = _cubePool.Spawn(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), null);
            if (go != null)
                _basicActiveStack.Push(go);
        }

        private void BasicDespawnNewest()
        {
            while (_basicActiveStack.Count > 0)
            {
                var go = _basicActiveStack.Pop();
                if (go != null && go.activeInHierarchy)
                {
                    go.ReturnToPool();
                    return;
                }
            }
        }

        private void BasicDespawnAll()
        {
            while (_basicActiveStack.Count > 0)
            {
                var go = _basicActiveStack.Pop();
                if (go != null && go.activeInHierarchy)
                    go.ReturnToPool();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode — GameObjectPool (plain prefab, no IPoolable)
        // ══════════════════════════════════════════════════════════════════

        private void PlainCubeSpawnOne()
        {
            if (_plainCubePool == null) return;
            var pos = RandomPointInSpawnArea(y: 0.3f);
            var go = _plainCubePool.Spawn(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), null);
            if (go != null) _plainActiveStack.Push(go);
        }

        private void PlainCubeDespawnNewest()
        {
            while (_plainActiveStack.Count > 0)
            {
                var go = _plainActiveStack.Pop();
                if (go != null && go.activeInHierarchy)
                {
                    // gameObject.ReturnToPool() uses the PooledMarker fast-path that
                    // GameObjectPool attaches to each clone — same despawn API as the
                    // IPoolable path, even though this prefab has no IPoolable.
                    go.ReturnToPool();
                    return;
                }
            }
        }

        private void PlainCubeDespawnAll()
        {
            while (_plainActiveStack.Count > 0)
            {
                var go = _plainActiveStack.Pop();
                if (go != null && go.activeInHierarchy) go.ReturnToPool();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode — Stress Test
        // ══════════════════════════════════════════════════════════════════

        private void StressSpawn(int count)
        {
            if (_cubePool == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(
                    Random.Range(-9f, 9f),
                    Random.Range(0.4f, 3.5f),
                    Random.Range(-9f, 9f));
                _cubePool.Spawn(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), null);
            }
        }

        /// <summary>
        /// Demo helper that calls <see cref="PoolingManager.CullUnusedPools"/> and then
        /// re-registers any pools that got destroyed. Without the re-register, the demo's
        /// cached <see cref="IPoolControl"/> references go stale and subsequent button
        /// clicks become no-ops. The diagnostics window also stops listing the pools.
        ///
        /// Production code wouldn't need this; the demo only needs it because we want
        /// to <em>show</em> what CullUnusedPools does without breaking the play session.
        /// </summary>
        private void CullIdlePoolsAndReregister(float idleSeconds)
        {
            int culled = PoolingManager.Instance.CullUnusedPools(idleSeconds);
            if (culled > 0)
            {
                // RegisterPools is idempotent via GetOrCreatePool semantics — pools that
                // survived the cull are returned unchanged; destroyed ones are rebuilt.
                RegisterPools();
                Debug.Log($"[DemoBootstrap] Culled {culled} idle pool(s) and re-registered.");
            }
            else
            {
                Debug.Log("[DemoBootstrap] No pools were idle enough to cull.");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode 2 — Batch Spawn
        // ══════════════════════════════════════════════════════════════════

        private void BatchSpawnGrid(int sideCount)
        {
            if (_spherePool == null) return;

            // Recycle the previous grid first — rapid clicks would otherwise stack
            // spheres on top of each other before their lifetime timers expire.
            for (int i = 0; i < _batchActive.Count; i++)
            {
                var stale = _batchActive[i];
                if (stale != null && stale.activeInHierarchy)
                    stale.ReturnToPool();
            }
            _batchActive.Clear();

            // Manual loop instead of the SpawnGrid extension so we can collect refs
            // for tracking. The extension method on IPoolControl returns just a count.
            var center = new Vector3(0f, 0.4f, 0f);
            const float spacing = 0.6f;
            float halfGrid = (sideCount - 1) * spacing * 0.5f;

            for (int x = 0; x < sideCount; x++)
            {
                for (int z = 0; z < sideCount; z++)
                {
                    var pos = center + new Vector3(x * spacing - halfGrid, 0f, z * spacing - halfGrid);
                    var go = _spherePool.Spawn(pos, Quaternion.identity, null);
                    if (go != null) _batchActive.Add(go);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode 3 — Projectile Storm
        // ══════════════════════════════════════════════════════════════════

        private void FireStormProjectile()
        {
            if (_projectilePool == null) return;

            var origin = new Vector3(0f, ProjectileSpawnHeight, 0f);
            // Spawn at a small horizontal offset so all rounds don't appear coincident
            var spawnOffset = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            var rotation = Quaternion.Euler(
                Random.Range(-25f, 25f),
                Random.Range(0f, 360f),
                0f);

            var go = _projectilePool.Spawn(origin + spawnOffset, rotation, null);
            if (go != null && go.TryGetComponent<PooledProjectile>(out var proj))
            {
                var velocity = rotation * Vector3.forward * Random.Range(7f, 11f);
                velocity.y += 2.5f;
                proj.LaunchWithVelocity(velocity);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Mode 4 — Particle Burst
        // ══════════════════════════════════════════════════════════════════

        private void PopParticleBurst()
        {
            var pos = RandomPointInSpawnArea(y: 0.4f);
            var color = FireworkPalette[Random.Range(0, FireworkPalette.Length)];
            SpawnFireworkBurst(pos, color);
        }

        private void PopBurstRing(int count)
        {
            if (count <= 0) return;

            const float radius = 2.5f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                var pos = new Vector3(Mathf.Cos(angle) * radius, 0.4f, Mathf.Sin(angle) * radius);
                // Walk through the palette so successive bursts in a ring are
                // visually distinct — 12 bursts cycles through all 8 colors + 4 repeats.
                var color = FireworkPalette[i % FireworkPalette.Length];
                SpawnFireworkBurst(pos, color);
            }
        }

        /// <summary>
        /// Spawns one firework burst at a position, tinted with the given colour.
        /// Tints both via a per-renderer MaterialPropertyBlock (cheap, doesn't break
        /// GPU instancing for same-block batches) and the particle main.startColor
        /// (in case anything downstream uses vertex colour modulation).
        /// </summary>
        private void SpawnFireworkBurst(Vector3 pos, Color color)
        {
            if (_burstPool == null) return;
            var go = _burstPool.Spawn(pos, Quaternion.identity, null);
            if (go == null) return;

            if (go.TryGetComponent<ParticleSystemRenderer>(out var renderer))
            {
                if (_burstMpb == null) _burstMpb = new MaterialPropertyBlock();
                _burstMpb.Clear();
                _burstMpb.SetColor("_BaseColor", color);
                _burstMpb.SetColor("_Color", color);           // legacy fallback
                _burstMpb.SetColor("_EmissionColor", color * 2.5f);
                renderer.SetPropertyBlock(_burstMpb);
            }

            if (go.TryGetComponent<ParticleSystem>(out var ps))
            {
                var main = ps.main;
                main.startColor = color;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Metrics
        // ══════════════════════════════════════════════════════════════════

        private void RefreshMetricsLabel()
        {
            if (_metricsLabel == null) return;

            _metricsSb.Clear();
            AppendPoolLine(_metricsSb, "Cubes",       _cubePool);
            _metricsSb.Append("  •  ");
            AppendPoolLine(_metricsSb, "Spheres",     _spherePool);
            _metricsSb.Append("  •  ");
            AppendPoolLine(_metricsSb, "Projectiles", _projectilePool);
            _metricsSb.Append("  •  ");
            AppendPoolLine(_metricsSb, "Fireworks",   _burstPool);
            _metricsSb.Append("  •  ");
            AppendPoolLine(_metricsSb, "GO Pool",     _plainCubePool);

            _metricsLabel.text = _metricsSb.ToString();
        }

        private static void AppendPoolLine(System.Text.StringBuilder sb, string name, IPoolControl pool)
        {
            if (pool == null) { sb.Append(name).Append(": —"); return; }

            int active = pool.ActiveCount;
            int inactive = pool.InactiveCount;
            float eff = pool.Metrics.ReuseEfficiency;

            sb.Append(name).Append(": ")
              .Append(active).Append("a / ").Append(inactive).Append("i")
              .Append("  reuse ").Append((eff * 100f).ToString("F0")).Append("%");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Utilities
        // ══════════════════════════════════════════════════════════════════

        private static Vector3 RandomPointInSpawnArea(float y)
        {
            var c = Random.insideUnitCircle * SpawnAreaRadius;
            return new Vector3(c.x, y, c.y);
        }

        private static Material CreateUrpMaterial(Color baseColor, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");

            var mat = new Material(shader) { name = "DemoMat_" + baseColor.ToString() };

            // URP Lit uses _BaseColor; Standard uses _Color. Set both so we work either way.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            return mat;
        }

        /// <summary>
        /// Reflection helper to set [SerializeField] private fields on the pooled
        /// components we use as templates. We do this once at template-build time
        /// so each cloned instance inherits the configured values via Instantiate().
        /// </summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var t = target.GetType();
            while (t != null)
            {
                var f = t.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (f != null) { f.SetValue(target, value); return; }
                t = t.BaseType;
            }
            Debug.LogWarning($"[DemoBootstrap] Could not find field '{fieldName}' on {target.GetType().Name}");
        }
    }
}
