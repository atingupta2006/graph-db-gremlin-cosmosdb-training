# Day 05 — Intermediate Gremlin Traversals

**Duration**: 3 hours
**Prerequisite**: Days 01–04 completed. Graph loaded via GremlinSeed (run `dotnet run --project project/GremlinSeed -- --day4` from repo root). You should have both tenants, the full unit hierarchy (buildings, floors, rooms; plants, sections), gateways, equipment, sensors, and their edges — same dataset as Day 04.
**Participation Mode**: Strict Trainer Control — all queries are pre-approved by the trainer. Students observe or execute only as directed. No query modifications permitted. Execute each query once only.

Run all Gremlin below in Data Explorer: **iot-graph-db** → **asset-graph** → Gremlin tab.

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

> `coalesce(traversal1, traversal2, ...)` returns the first traversal that produces a result. Useful for handling missing properties. In the standard seed no sensor has `calibration_date`, so you’ll see `not-calibrated` for all — the point is how coalesce handles missing data.

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

1. Traverse the full unit hierarchy from Building-A. Run:

```gremlin
g.V('unit-bldgA')
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .path()
  .by('name')
```

2. Use `times()` for fixed-depth traversal. Run:

```gremlin
g.V('unit-bldgA')
  .repeat(__.out('contains'))
  .times(2)
  .values('name')
```

> `times(n)` repeats exactly n times — simpler than `until()` for fixed depth.

3. Use `emit()` to collect intermediate results. Run:

```gremlin
g.V('unit-bldgA')
  .emit()
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .values('name')
```

> `emit()` before `repeat()` includes the starting vertex in results.

4. Compare: `emit()` after `repeat()` excludes the starting vertex. Run:

```gremlin
g.V('unit-bldgA')
  .repeat(__.out('contains')).emit()
  .values('name')
```

5. Add a depth limiter with `loops()`. Run:

```gremlin
g.V('unit-bldgA')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

> `loops()` returns the current repeat iteration (0-based: first pass is 0, then 1, 2, …). Always use a depth limit (e.g. `until(__.loops().is(5))`) to prevent runaway queries.

6. Get hierarchy with depth information. Run:

```gremlin
g.V('unit-bldgA')
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

> `path()` combined with `repeat()` shows the full route through the hierarchy — useful for understanding connectivity and organizational structure.

1. Find all paths from tenant-1 to leaf units. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .emit()
  .repeat(__.out('contains'))
  .until(__.out('contains').count().is(0))
  .path()
  .by('name')
```

2. Find the path from a sensor to its tenant (reverse traversal). Run:

```gremlin
g.V('sensor-temp001')
  .out('assigned_to')
  .in('hosts')
  .emit()
  .repeat(__.in('contains'))
  .until(__.in('contains').count().is(0))
  .in('manages')
  .path()
  .by('name')
```

3. Find all paths of a specific length (2 hops from building). Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .repeat(__.out('contains'))
  .times(2)
  .path()
  .by('name')
```

4. Path with mixed edge types (hierarchy + operational). Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .path()
  .by('name')
```

> Mixed-edge-type paths reveal cross-functional relationships across organizational and operational boundaries.

5. Count unique paths. Run:

```gremlin
g.V('tenant-1')
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

> The position of `emit()` relative to `repeat()` determines which vertices appear in results. Observing the differences is essential for writing correct recursive queries.

1. `emit()` before `repeat()` — includes starting vertex. Run:

```gremlin
g.V('unit-bldgA')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(3))
  .values('name')
```

2. `emit()` after `repeat()` — excludes starting vertex. Run:

```gremlin
g.V('unit-bldgA')
  .repeat(__.out('contains'))
  .emit()
  .until(__.loops().is(3))
  .values('name')
```

> Compare the results of Tasks 1 and 2 — Task 1 includes Building-A, Task 2 does not.

3. Conditional emit — emit only specific types. Run:

```gremlin
g.V('unit-bldgA')
  .emit(__.has('type', 'room'))
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

> `emit(predicate)` selectively outputs only vertices matching the predicate.

4. Emit with aggregation. Run:

```gremlin
g.V('unit-bldgA')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .groupCount()
  .by('type')
```

> Groups all emitted vertices by type — shows the distribution across the hierarchy.

**Success**: Emit placement and conditional emit behavior understood.
