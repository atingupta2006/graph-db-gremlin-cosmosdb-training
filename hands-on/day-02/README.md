# Day 02 — Gremlin Fundamentals & Core Traversal Patterns

**Duration**: 3 hours
**Prerequisite**: Day 01 completed — seed vertices (tenants, units, gateways, equipment, sensors) and edges exist in `asset-graph`.
**Participation Mode**: Guided Hands-On — execute queries alongside the instructor.

---

## Lab 1: Query All Vertices and Edges (~15 min)

**Objective**: Use fundamental Gremlin steps to explore the graph.

> `g.V()` is the starting point for all vertex traversals. `g.E()` is the starting point for all edge traversals.

1. Count all vertices. Run:

```gremlin
g.V().count()
```

> Expected: ~15 vertices (tenants, units, gateways, equipment, sensors from Day 01).

2. Count all edges. Run:

```gremlin
g.E().count()
```

> Expected: ~20 edges.

3. List vertices (limited to first 10). Run:

```gremlin
g.V().limit(10)
```

> Always use `limit()` when exploring unknown data to avoid large result sets.

4. List edges (limited to first 10). Run:

```gremlin
g.E().limit(10)
```

5. Get all distinct vertex labels. Run:

```gremlin
g.V().label().dedup()
```

> Returns the set of unique labels: tenant, unit, gateway, equipment, sensor.

6. Get all distinct edge labels. Run:

```gremlin
g.E().label().dedup()
```

> Returns: manages, contains, hosts, connects_to, monitors, assigned_to.

7. Count vertices by label. Run:

```gremlin
g.V().groupCount().by(label)
```

> Shows the distribution of vertex types in the graph — a quick composition overview.

**Success**: Can count and list vertices/edges, identify all labels in the graph.

---

## Lab 2: Filter with has(), hasLabel(), hasId() (~15 min)

**Objective**: Target specific vertices and edges using filter steps.

> `has()` is the primary filter mechanism in Gremlin. It narrows results by label, property values, or property existence.

1. Find vertices by label. Run:

```gremlin
g.V().hasLabel('equipment')
```

2. Find a vertex by ID. Run:

```gremlin
g.V().hasId('equip-hvac101')
```

Shorthand (equivalent). Run:

```gremlin
g.V('equip-hvac101')
```

> `g.V('id')` is shorthand for `g.V().hasId('id')`. Both target a specific vertex.

3. Filter by property value. Run:

```gremlin
g.V().hasLabel('equipment').has('type', 'hvac')
```

4. Filter by property existence. Run:

```gremlin
g.V().hasLabel('sensor').has('calibration_date')
```

> `has('property')` without a value checks that the property exists — regardless of its value.

5. Combine label and property filters. Run:

```gremlin
g.V().hasLabel('gateway').has('status', 'active')
```

6. Filter edges by label. Run:

```gremlin
g.E().hasLabel('monitors')
```

7. Filter edges by property. Run:

```gremlin
g.E().hasLabel('connects_to').has('protocol', 'mqtt')
```

**Success**: Can target specific vertices and edges using `has()`, `hasLabel()`, `hasId()`.

---

## Lab 3: Access Properties with values() and valueMap() (~15 min)

**Objective**: Extract property values from vertices and edges.

1. Get specific property values. Run:

```gremlin
g.V().hasLabel('equipment').values('name')
```

> Returns a flat list of `name` values for all equipment.

2. Get multiple property values. Run:

```gremlin
g.V().hasLabel('equipment').values('name', 'type', 'status')
```

> Returns interleaved values — name, type, status for each equipment, as a flat stream.

3. Get all properties as a map. Run:

```gremlin
g.V().hasLabel('equipment').valueMap()
```

> `valueMap()` returns key-value maps. Property values appear as arrays (Cosmos DB convention).

4. Get all properties including id and label. Run:

```gremlin
g.V().hasLabel('equipment').valueMap(true)
```

> `valueMap(true)` adds `id` and `label` to the map — useful when you need to identify which vertex each map belongs to.

5. Get properties of a single vertex. Run:

```gremlin
g.V('equip-hvac101').valueMap(true)
```

6. Get edge properties. Run:

```gremlin
g.E().hasLabel('connects_to').valueMap(true)
```

7. Use `properties()` for individual property objects. Run:

```gremlin
g.V('equip-hvac101').properties()
```

> `properties()` returns property objects with key, value, and metadata — more detailed than `values()`.

8. Access a single property. Run:

```gremlin
g.V('equip-hvac101').properties('status')
```

**Success**: Can extract property values in multiple formats (flat values, maps, property objects).

---

## Lab 4: Combine Filters for Targeted Queries (~15 min)

**Objective**: Build precise queries combining multiple filter conditions.

1. AND logic — chain `has()` steps (implicit AND). Run:

```gremlin
g.V().hasLabel('equipment')
  .has('type', 'hvac')
  .has('status', 'running')
  .values('name')
```

> Chaining `has()` steps creates implicit AND — both conditions must be true.

2. Find sensors of a specific type with threshold above a value. Run:

```gremlin
g.V().hasLabel('sensor')
  .has('sensor_type', 'temperature')
  .has('threshold', gt(80))
  .valueMap('name', 'threshold')
```

> `gt()` is a comparison predicate — greater than. Also available: `gte()`, `lt()`, `lte()`, `eq()`, `neq()`.

3. Find active gateways. Run:

```gremlin
g.V().hasLabel('gateway')
  .has('status', 'active')
  .values('name', 'ip_address')
```

4. Count results of filtered queries. Run:

```gremlin
g.V().hasLabel('sensor')
  .has('status', 'active')
  .count()
```

5. Use OR logic with `or()` step. Run:

```gremlin
g.V().hasLabel('equipment')
  .or(
    __.has('type', 'hvac'),
    __.has('type', 'pump')
  )
  .values('name', 'type')
```

> `or()` provides boolean OR logic. Anonymous traversals use `__` (double underscore) prefix.

6. Use NOT logic with `not()` step. Run:

```gremlin
g.V().hasLabel('sensor')
  .not(__.has('calibration_date'))
  .values('name')
```

> `not()` inverts the condition — returns sensors that do NOT have `calibration_date`.

**Success**: Can construct multi-condition queries using AND, OR, NOT logic with comparison predicates.

---

## Lab 5: Traverse Relationships with out(), in(), both() (~15 min)

**Objective**: Navigate edges to discover connected vertices.

> Gremlin navigates graphs by traversing edges. `out()` follows outgoing edges, `in()` follows incoming edges, `both()` follows both directions.

1. From a tenant, find all units it manages. Run:

```gremlin
g.V('tenant-1').out('manages').values('name')
```

> `out('manages')` follows outgoing `manages` edges from tenant-1, arriving at units.

2. From a unit, find which tenant manages it. Run:

```gremlin
g.V('unit-bldgA').in('manages').values('name')
```

> `in('manages')` follows incoming `manages` edges — the reverse direction. Returns: Acme Corp.

3. From a gateway, find all equipment it connects to. Run:

```gremlin
g.V('gw-001').out('connects_to').values('name', 'type')
```

4. From equipment, find what connects to it (sensors, gateways). Run:

```gremlin
g.V('equip-hvac101').in().values('name')
```

> Without a label argument, `in()` follows ALL incoming edge labels.

5. From a sensor, find all related vertices in both directions. Run:

```gremlin
g.V('sensor-temp001').both().values('name')
```

> `both()` follows edges in both directions — returns equipment (via monitors) and gateway (via assigned_to).

6. Use edge-specific traversals — stop at the edge. Run:

```gremlin
g.V('gw-001').outE('connects_to').inV().values('name')
```

> `outE()` stops at the edge. `inV()` continues from the edge to its target vertex. This allows accessing edge properties mid-traversal.

7. Access edge properties during traversal. Run:

```gremlin
g.V('gw-001').outE('connects_to').valueMap()
```

> Returns edge properties: protocol, signal_strength for each connection.

**Success**: Can traverse relationships in both directions, access edge properties mid-traversal.

---

## Lab 6: Chain Multi-Step Traversals (~15 min)

**Objective**: Build multi-hop traversals to navigate deep relationships.

> Each step in a chain follows edges from the current position. Multi-hop traversals read like a sentence: "start at tenant, go out to units, go out to gateways..."

1. Two-hop: From tenant to gateways (tenant → units → gateways). Run:

```gremlin
g.V('tenant-1').out('manages').out('hosts').values('name')
```

2. Three-hop: From tenant to equipment (tenant → units → gateways → equipment). Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .values('name', 'type')
```

3. Find sensors monitoring equipment under Building-A. Run:

```gremlin
g.V('unit-bldgA')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .hasLabel('sensor')
  .values('name', 'sensor_type')
```

> Direction changes mid-chain: `out()` from unit to gateway to equipment, then `in()` from equipment back to sensors.

4. Navigate the full chain: tenant → unit → gateway → equipment → sensor. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .values('name')
```

5. Count equipment per tenant. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .count()
```

**Success**: Can chain 2–4 hop traversals to navigate the full IoT hierarchy.

---

## Lab 7: Filter Within Traversals (~15 min)

**Objective**: Apply filters at each step of a multi-hop traversal.

> Filters can be applied at any step in the traversal chain. Placing the most selective filter earliest reduces the number of traversers — better performance.

1. From tenant, find only active gateways. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .has('status', 'active')
  .values('name')
```

2. Find HVAC equipment connected to a specific gateway. Run:

```gremlin
g.V('gw-001')
  .out('connects_to')
  .has('type', 'hvac')
  .values('name', 'status')
```

3. Find temperature sensors monitoring running equipment. Run:

```gremlin
g.V().hasLabel('equipment')
  .has('status', 'running')
  .in('monitors')
  .has('sensor_type', 'temperature')
  .values('name')
```

4. Chain filters at multiple levels. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .has('type', 'building')
  .out('hosts')
  .has('status', 'active')
  .out('connects_to')
  .has('type', 'hvac')
  .values('name')
```

> Filters at each hop: only buildings, only active gateways, only HVAC equipment.

5. Filter on edge properties using the outE/has/inV pattern. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .outE('connects_to')
  .has('protocol', 'mqtt')
  .inV()
  .values('name')
```

> `outE().has().inV()` filters by edge properties — returns only equipment connected via MQTT.

**Success**: Can filter results at any point in a multi-hop traversal.

---

## Lab 8: Path Traversals (~15 min)

**Objective**: Track the full path taken through a graph traversal.

> `path()` returns the sequence of vertices and edges visited during a traversal — useful for understanding how entities are connected.

1. Show the path from tenant to equipment. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .path()
```

2. Show path with specific property values. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .path()
  .by('name')
```

> `path().by('name')` extracts the `name` property at each step, producing readable output like: [Acme Corp, Building-A, GW-001, HVAC-101].

3. Find path from sensor to tenant (reverse direction). Run:

```gremlin
g.V('sensor-temp001')
  .out('assigned_to')
  .in('hosts')
  .in('manages')
  .path()
  .by('name')
```

> Traverses upward: sensor → gateway → unit → tenant.

4. Use `simplePath()` to avoid cycles. Run:

```gremlin
g.V('unit-bldgA')
  .both()
  .both()
  .simplePath()
  .path()
  .by('name')
```

> `simplePath()` filters out paths that revisit vertices — prevents circular traversals.

5. Visualize paths in the **Graph** tab of Data Explorer

> Run one of the path queries above and switch to the Graph tab to see the traversal path rendered visually.

**Success**: Can trace and display the full traversal path through the graph.
