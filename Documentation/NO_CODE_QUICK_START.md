# PoolMaster - No-Code Quick Start
**60 seconds. No programming.**
## 🕷️ What You'll Build
- Auto-spawning objects  
- Auto-returning to pool  
- Zero garbage collection
## ⚒️ Step 1: Add Manager (10 sec)
## 🌑 Step 2: Create Pool (15 sec)
## 🔥 Step 3: Auto-Spawn (20 sec)
## 🧪 Step 4: Auto-Return (15 sec)
## 🎮 Press Play
**Optional cleanup:**
- Stop Particles On Return — Stops VFX
- Reset Rigidbody On Return — Stops physics
## 🗡️ Common Uses
## 🧱 Key Settings Explained
## 🛡️ Troubleshooting
### Nothing spawns
1. Prefab assigned in pool AND spawner?
2. PoolMaster Manager in scene?
3. Prewarm Amount > 0 OR "On Exhausted" = Expand?
### Objects spawn but never return
1. Return To Pool on PREFAB (not spawned copy)?
2. Return Condition set?
3. Lifetime Seconds > 0 if using After Time?
### Bad performance
1. Increase Prewarm Amount
2. Enable "Prewarm On Start"
3. Don't set Max Size too low
### Wrong spawn location
1. Check Position Mode
2. Check Spawn Offset values
3. Prefab transform at (0,0,0)?
## 🧭 Pro Tips
- Add Return To Pool to **prefab once**, not every copy  
- Use "On Key Press" (Space) for easy testing  
- Enable "Show Debug Info" to see what's happening  
- Select Spawner to see green spawn area in Scene view  
- Create "Pooled Prefabs" folder for organization
# PoolMaster - No-Code Quick Start

**60 seconds. No programming.**

## What You'll Build
💎 Auto-spawning objects  
🖤 Auto-returning to pool  
⛓️ Zero garbage collection

Supports Unity 6.0–6.4 (Built-in, URP, HDRP).

---

---

## Step 1: Add Manager (10 sec)

1. **Hierarchy** → Right-click → **Create Empty**
2. Name it `PoolMaster`
3. **Add Component** → Type `PoolMaster Manager`

Done. System active.

---

## Step 2: Create Pool (15 sec)

1. Select `PoolMaster` in Hierarchy
2. Inspector → **Pools** → Click **"Add New Pool"**
3. **Drag your prefab** into the **Prefab** field
4. **Prewarm Amount** = `10` ← Creates 10 at start
5. **Max Size** = `50` ← Optional limit

Done. Pool ready.

---

## Step 3: Auto-Spawn (20 sec)

1. **Hierarchy** → Right-click → **Create Empty**
2. Name it `Spawner`
3. **Add Component** → `PoolMaster Spawner`
4. **What to Spawn** → Drag same prefab
5. **Spawn On** → `On Start`
6. **Spawn Count** → `1`

**Want repeating?** Set **Repeat Interval** = `0.5` (spawns every 0.5 sec)  
**Want different position?** Set **Spawn Offset** Y = `5` (spawns 5 units up)

Done. Auto-spawning works.

---

## Step 4: Auto-Return (15 sec)

Make objects go back to pool automatically.

1. Find your **prefab** in Project window ← ORIGINAL prefab, not scene copy
2. Select it → **Add Component** → `PoolMaster Return To Pool`
3. **Return Condition** → `After Time`
4. **Lifetime Seconds** → `2` ← Returns after 2 seconds

**Optional cleanup:**
- ☑ **Stop Particles On Return** ← Stops VFX
- ☑ **Reset Rigidbody On Return** ← Stops physics

Done. Auto-return works.

---

## 🖤 Press Play

Objects spawn → live for 2 sec → return to pool → get reused.  
**No code. No garbage collection.**

---

---

## Common Uses

### Bullets
```
Pool: Bullet prefab, Prewarm: 20, Max: 100
Spawner: Attach to gun, Spawn On: On Key Press (Space)
Return: After Time (5 sec) OR On Collision
```

### Particles/VFX
```
Pool: Explosion prefab, Prewarm: 5, Max: 20
Spawner: Spawn On: On Collision
Return: On Particle Finished + After Time (10 sec backup)
```

### Enemies
```
Pool: Enemy prefab, Prewarm: 10, Max: 50
Spawner: Random In Area, Repeat Interval: 3 seconds
Return: Via script when dead, or On Trigger Exit (fell off map)
```

---

---

## Key Settings Explained

### Prewarm Amount
- **What:** How many objects to create at start
- **Rule:** Match your max simultaneous objects
- **Example:** 20 bullets on screen max? Set 20.

### Max Size
- **What:** Hard limit on total objects
- **Rule:** 0 = unlimited (safest for beginners)
- **When to set:** Only if memory matters

### On Exhausted (when pool full)
- **Expand** ← Safe default, creates more if needed
- **Reuse Oldest** ← For strict limits (max 50 bullets ever)
- **Return Null** ← For memory-critical situations
- **Warn** ← Shows warning, then expands

### Position Mode
- **At This Object** ← Spawns at spawner location
- **Random In Area** ← Shows green box in scene, random inside
- **At Target** ← Spawns at another GameObject

### Return Condition
- **After Time** ← Most common (bullets, effects)
- **On Particle Finished** ← VFX with particle systems
- **On Collision** ← Bullets hitting targets
- **On Trigger Exit** ← Leaving a zone
- **On Disable** ← When object deactivates

---

---

## Troubleshooting

### Nothing spawns
1. ✅ Prefab assigned in pool AND spawner?
2. ✅ PoolMaster Manager in scene?
3. ✅ Prewarm Amount > 0 OR "On Exhausted" = Expand?

### Objects spawn but never return
1. ✅ Return To Pool on PREFAB (not spawned copy)?
2. ✅ Return Condition set?
3. ✅ Lifetime Seconds > 0 if using After Time?

### Bad performance
1. ✅ Increase Prewarm Amount
2. ✅ Enable "Prewarm On Start"
3. ✅ Don't set Max Size too low

### Wrong spawn location
1. ✅ Check Position Mode
2. ✅ Check Spawn Offset values
3. ✅ Prefab transform at (0,0,0)?

---

## Pro Tips

✓ Add Return To Pool to **prefab once**, not every copy  
✓ Use "On Key Press" (Space) for easy testing  
✓ Enable "Show Debug Info" to see what's happening  
✓ Select Spawner to see green spawn area in Scene view  
✓ Create "Pooled Prefabs" folder for organization

---

**Need code control?** Call `PoolMasterManager.Instance.Spawn()` from scripts.  
**Full docs?** See README.md for programmer API.
