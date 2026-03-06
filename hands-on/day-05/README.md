# Day 05 — Intermediate Gremlin Traversals

**Duration**: 3 hours  
**Prerequisite**: Days 01–04 completed. Graph loaded via **GremlinSeedContinuous** into the **asset-graph-load** container. You should have at least one batch of data: tenants, optional sites, buildings, floors, rooms, gateways, equipment, sensors, and their edges (same schema as Day 04, different container and IDs).  
Run queries as directed by the trainer; execute each once.

---

## Data source

This lab uses data produced by **GremlinSeedContinuous** in **asset-graph-load**. The queries below assume:

| Item | In this doc | In GremlinSeedContinuous |
|------|----------------|---------------------------|
| **Container** | asset-graph-load | `CosmosDb:Graph` = asset-graph-load |
| **Vertex labels** | tenant, unit, gateway, equipment, sensor | Same (unit has `type`: site, building, floor, room) |
| **Edge types** | manages, contains, hosts, connects_to, monitors, assigned_to | Same |
| **Partition key** | `pk` = tenant id (e.g. tenant-load-1-0) | Every vertex has `pk` = its tenant id |
| **Example IDs (batch 1)** | tenant-load-1-0, unit-bldg-1-0-0-0, gw-1-0-0-0-0, sensor-1-0-0-0-0-0, etc. | ID format: `tenant-load-{batch}-{t}`, `unit-bldg-{batch}-{t}-{s}-{b}`, `gw-{batch}-{t}-{s}-{b}-{g}`, `sensor-{batch}-{t}-{s}-{b}-{g}-{sn}` |
| **Hierarchy** | tenant → manages → site → contains → building → contains → floor → room; building → hosts → gateway | Same (when SitesPerTenant ≥ 1) |
| **Equipment** | has `status`: running / stopped | Same |
| **Sensor** | has `sensor_type`, no `calibration_date` | Same |

If you use different batch shape (e.g. SitesPerTenant 0 or more buildings), use the discovery queries in Setup to find your IDs and substitute them.

---

## Setup: Graph and Data Source

- **Data**: Load the graph with **GremlinSeedContinuous** (not GremlinSeed). From repo root run:
  ```bash
  dotnet run --project project/GremlinSeedContinuous
  ```
  Or use `run.bat` in `project/GremlinSeedContinuous`. Ensure at least one batch has completed (you should see "OK: N, Failed: 0" for that batch). **For Lab 5 (Performance and RU)**, run 2–3 or more batches so you have multiple tenants and enough data to see clear RU differences.
- **Where to run Gremlin**: In Azure Data Explorer use **iot-graph-db** → **asset-graph-load** → **Gremlin** tab (not asset-graph).
- **IDs in this guide**: Example queries use IDs from the **first batch** with default config (1 tenant, 1 site, 1 building, 1 floor, 1 room, 1 gateway, 1 equipment, 1 sensor), e.g.:
  - `tenant-load-1-0` — first tenant
  - `unit-site-1-0-0` — first site (tenant → manages → site)
  - `unit-bldg-1-0-0-0` — first building (site → contains → building)
  - `unit-floor-1-0-0-0-0` — first floor
  - `unit-room-1-0-0-0-0-0` — first room
  - `gw-1-0-0-0-0` — first gateway
  - `equip-1-0-0-0-0-0` — first equipment
  - `sensor-1-0-0-0-0-0` — first sensor
- If you changed batch shape (e.g. more buildings or no site), discover your IDs with:
  ```gremlin
  g.V().hasLabel('tenant').limit(3).valueMap('id','name')
  g.V().hasLabel('unit').has('type','building').limit(3).valueMap('id','name')
  g.V().hasLabel('sensor').limit(3).valueMap('id','name')
  ```
  Then substitute those IDs in the lab queries below.

---

## Lab 1: Conditional Traversals (~15 min)

**Objective**: Use `choose()` and `coalesce()` for branching logic in traversals.

> Conditional steps enable dynamic query behavior — handle branching and missing data gracefully within a single traversal.

1. Use `choose()` for if-then-else branching. Run:

```gremlin
g.V().hasLabel('equipment').choose(
  __.has('status', 'running'),
  __.constant('operational'),
  __.constant('needs-attention')
)
```

> `choose(condition, true-traversal, false-traversal)` evaluates the condition per element.

2. Use `choose()` with projected output. Run:

```gremlin
g.V().hasLabel('equipment')
  .project('name', 'health')
  .by('name')
  .by(
    __.choose(
      __.has('status', 'running'),
      __.constant('healthy'),
      __.constant('check-required')
    )
  )
```

3. Use `coalesce()` for fallback values. Run:

```gremlin
g.V().hasLabel('sensor')
  .coalesce(
    __.values('calibration_date'),
    __.constant('not-calibrated')
  )
```

> `coalesce(traversal1, traversal2, ...)` returns the first traversal that produces a result. In the GremlinSeedContinuous data no sensor has `calibration_date`, so you’ll see `not-calibrated` for all — the point is how coalesce handles missing data.

4. Combine choose with traversal steps. Run:

```gremlin
g.V().hasLabel('sensor').choose(
  __.has('sensor_type', 'temperature'),
  __.out('monitors').values('name'),
  __.out('assigned_to').values('name')
)
```

5. Use `optional()` for traversals that may not return results. Run:

```gremlin
g.V().hasLabel('equipment')
  .optional(__.in('monitors').has('sensor_type', 'vibration'))
  .values('name')
```

> `optional(traversal)` executes if possible; otherwise returns the current element unchanged.

**Success**: Conditional queries using choose, coalesce, and optional demonstrated.

---

## Lab 2: Recursive Hierarchy Traversal with repeat/until (~20 min)

**Objective**: Traverse hierarchical relationships of unknown depth.

> `repeat(traversal)` executes a traversal repeatedly. Combined with `until()` or `times()`, it enables recursive hierarchy exploration. Always include a depth limit to prevent runaway queries.

**Note**: In GremlinSeedContinuous, the first building (default config) is `unit-bldg-1-0-0-0`. Hierarchy from building: building → contains → floor → contains → room.

1. Traverse the full unit hierarchy from the first building. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .path()
  .by('name')
```

2. Use `times()` for fixed-depth traversal. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .repeat(__.out('contains'))
  .times(2)
  .values('name')
```

> `times(n)` repeats exactly n times — simpler than `until()` for fixed depth.

3. Use `emit()` to collect intermediate results. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .values('name')
```

> `emit()` before `repeat()` includes the starting vertex in results.

4. Compare: `emit()` after `repeat()` excludes the starting vertex. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .repeat(__.out('contains')).emit()
  .values('name')
```

5. Add a depth limiter with `loops()`. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

> `loops()` returns the current repeat iteration (0-based). Always use a depth limit (e.g. `until(__.loops().is(5))`) to prevent runaway queries.

6. Get hierarchy with depth information. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .project('name', 'depth')
  .by('name')
  .by(__.loops())
```

**Success**: Hierarchy traversal with repeat/until/emit/times/loops demonstrated at variable and fixed depths.

---

## Lab 3: Path-Based Queries on Hierarchies (~15 min)

**Objective**: Trace paths through hierarchical structures.

> `path()` combined with `repeat()` shows the full route through the hierarchy. In GremlinSeedContinuous, tenant → manages → site → contains → building → contains → floor → contains → room; building also hosts gateways.

1. Find all paths from the first tenant to leaf units. Run:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .emit()
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .path()
  .by('name')
```

2. Find the path from a sensor to its tenant (reverse traversal). Run:

```gremlin
g.V('sensor-1-0-0-0-0-0')
  .out('assigned_to')
  .in('hosts')
  .emit()
  .repeat(__.in('contains'))
  .until(__.in('contains').count().is(0))
  .in('manages')
  .path()
  .by('name')
```

> Sensor → gateway → building → (in contains) site → (in manages) tenant. With default data this shows the path from the first sensor up to the tenant.

3. Find all paths of a specific length (2 hops from tenant via manages/contains). Run:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .repeat(__.out('contains'))
  .times(2)
  .path()
  .by('name')
```

4. Path with mixed edge types (hierarchy + operational). Run:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .out('contains')
  .out('hosts')
  .out('connects_to')
  .path()
  .by('name')
```

> With default GremlinSeedContinuous data: tenant → manages → site → contains → building → hosts → gateway → connects_to → equipment. Mixed-edge paths reveal cross-functional relationships.

5. Count unique paths. Run:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(4))
  .path()
  .count()
```

> Path count gauges the complexity of the hierarchy.

**Success**: Paths traced through hierarchies and mixed relationship types.

---

## Lab 4: Emit Patterns for Intermediate Results (~10 min)

**Objective**: Master emit placement for different traversal behaviors.

> The position of `emit()` relative to `repeat()` determines which vertices appear in results.

1. `emit()` before `repeat()` — includes starting vertex. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(3))
  .values('name')
```

2. `emit()` after `repeat()` — excludes starting vertex. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .repeat(__.out('contains'))
  .emit()
  .until(__.loops().is(3))
  .values('name')
```

> Compare the results of Tasks 1 and 2 — Task 1 includes the building name, Task 2 does not.

3. Conditional emit — emit only specific types. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit(__.has('type', 'room'))
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

> `emit(predicate)` selectively outputs only vertices matching the predicate.

4. Emit with aggregation. Run:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .groupCount()
  .by('type')
```

> Groups all emitted vertices by type — shows the distribution across the hierarchy.

**Success**: Emit placement and conditional emit behavior understood.

---

## Lab 5: Performance and Request Units (RU) (~25 min)

**Objective**: Observe real Request Unit (RU) consumption and query performance on **asset-graph-load** with data from GremlinSeedContinuous. With more data (multiple batches), RU differences become visible — unlike the small Day 04 dataset.

> In Azure Data Explorer, after each Gremlin query the result panel shows **Request Charge (RU)**. Use it to compare partition-scoped vs cross-partition, payload size, repeat depth, and order/limit placement.

**Before starting**: Run GremlinSeedContinuous until at least **2–3 batches** have completed (or use existing data). More tenants (e.g. tenant-load-1-0, tenant-load-2-0) and more vertices make partition vs cross-partition and payload comparisons clearly visible.

---

### 5.1 Where to see Request Charge

1. In Data Explorer, open **iot-graph-db** → **asset-graph-load** → **Gremlin** tab.
2. Run any query (e.g. `g.V().count()`).
3. In the result panel, find **Request Charge** (or **Total Request Units**). Note this value for each exercise below and compare.

---

### 5.2 Partition-scoped vs cross-partition

Queries that filter by `pk` (partition key) read only one partition and typically cost fewer RUs than scanning all partitions.

1. **Count vertices in one partition** (partition-scoped). Run and note RU:

```gremlin
g.V().has('pk', 'tenant-load-1-0').count()
```

2. **Count all vertices** (cross-partition). Run and note RU:

```gremlin
g.V().count()
```

> With multiple batches, the second query usually has higher RU — it touches every partition. Use partition scope in multi-tenant apps to keep cost predictable.

---

### 5.3 Payload: full vertex vs projection

Returning only the fields you need often reduces RU compared to full vertex or `valueMap(true)`.

1. **Full vertex payload** for equipment connected to the first gateway. Run and note RU:

```gremlin
g.V('gw-1-0-0-0-0').out('connects_to').valueMap(true)
```

2. **Only names** (minimal projection). Run and note RU:

```gremlin
g.V('gw-1-0-0-0-0').out('connects_to').values('name')
```

> Smaller projection usually means lower RU. Use `project()` or `values()` instead of `valueMap(true)` when you only need a few properties.

---

### 5.4 Full chain: tenant → sensors (with and without tuning)

The full chain from tenant to sensors goes: tenant → manages → site → contains → building → hosts → gateway → connects_to → equipment ← monitors ← sensor.

1. **Full chain, all sensor names** (no early reduction). Run and note RU:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .out('contains')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .values('name')
```

2. **Same result, with early filter and dedup** (often cheaper). Run and note RU:

```gremlin
g.V('tenant-load-1-0')
  .out('manages')
  .out('contains')
  .out('hosts')
  .out('connects_to')
  .hasLabel('equipment')
  .in('monitors')
  .dedup()
  .values('name')
```

> Compare RUs: partition scope plus early `hasLabel` and `dedup` can reduce cost as the graph grows.

---

### 5.5 Repeat depth and RU

Deeper `repeat()` traversals touch more vertices and edges and usually cost more RUs. Always cap depth (e.g. `until(__.loops().is(n))`) to avoid runaway queries.

1. **Shallow repeat** (max 2 steps from building). Run and note RU:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(2))
  .values('name')
```

2. **Deeper repeat** (max 5 steps). Run and note RU:

```gremlin
g.V('unit-bldg-1-0-0-0')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

> With more data (e.g. multiple buildings and deeper hierarchy), the deeper repeat typically consumes more RUs. Use the smallest depth limit that meets your use case.

---

### 5.6 Order and limit placement (barrier steps)

Sorting is a barrier: Cosmos DB must collect enough traversers to sort. `order().by('name').limit(3)` sorts everything then takes three; `limit(3).order().by('name')` takes three then sorts (different result, often lower RU when you need only a few).

1. **Full sort then limit** (barrier then trim). Run and note RU:

```gremlin
g.V().has('pk', 'tenant-load-1-0').hasLabel('equipment').order().by('name').limit(3).values('name')
```

2. **Limit then sort** (trim then sort on smaller set). Run and note RU:

```gremlin
g.V().has('pk', 'tenant-load-1-0').hasLabel('equipment').limit(3).order().by('name').values('name')
```

> Step 1 returns the three alphabetically first names; step 2 returns the first three (by natural order) then sorts. Different semantics; step 2 is often cheaper as the graph grows.

---

### 5.7 Per-tenant aggregation (dashboard-style)

Use partition scope and aggregation for tenant-level KPIs (e.g. equipment count by status).

**Equipment count by status for the first tenant.** Run and note RU:

```gremlin
g.V().has('pk', 'tenant-load-1-0').hasLabel('equipment').groupCount().by('status')
```

> **Expected**: Map e.g. `{running: 1, stopped: 0}` (or more with larger batch shape). Use for tenant health or dashboard KPIs. With multiple batches, run the same for `tenant-load-2-0` and compare.

---

### 5.8 Optional: fill a simple RU comparison table

Record your Request Charge for a few queries to see the impact of partition key, payload, and depth:

| Query | RU (your run) |
|-------|----------------|
| 5.2.1 Partition-scoped count | |
| 5.2.2 Cross-partition count | |
| 5.3.1 valueMap(true) | |
| 5.3.2 values('name') | |
| 5.4.1 Full chain (no tuning) | |
| 5.4.2 Full chain (dedup + hasLabel) | |
| 5.6.1 order().limit(3) | |
| 5.6.2 limit(3).order() | |

**Success**: You observed real RU consumption and the effect of partition scope, projection, chain tuning, repeat depth, and order/limit placement. Use the same habits when tuning production queries.
