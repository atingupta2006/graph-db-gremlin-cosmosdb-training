# Day 04 — Modeling IoT & Asset Relationships

**Duration**: 3 hours  

We pick up the IoT/asset model from the earlier days and push further: multi-tenant hierarchy, real-world query patterns, and the same performance ideas as Day 03 (partition key, payload, early reduction) applied to this graph.

---

## Load the data

First step is to get the graph populated. Use the GremlinSeed project — same config as GremlinTraining (Hostname and Key in `appsettings.json` under project/3-GremlinSeed; see that project’s README if you need details).

From the repo root, run:

```bash
dotnet run --project project/3-GremlinSeed
```

The seed clears the graph and loads all data for both tenants (Acme and GlobalTech): units, gateways, equipment, sensors, and edges. The rest of the session is querying that data (we add one extra edge in Lab 5 for the many-to-many bit).

Run all the Gremlin below in Data Explorer: **iot-graph-db** → **asset-graph** → Gremlin tab. Keep an eye on Request Charge (RU) in the result panel — we’ll compare a few queries.

---

# Conceptual model: data and relationships

## Mind map — what’s in the graph

| Concept | In our graph |
|--------|----------------|
| **Vertices** | `tenant`, `unit`, `gateway`, `equipment`, `sensor` |
| **Partition key** | Every vertex has `pk` = `tenant-1` (Acme) or `tenant-2` (GlobalTech). All queries can be scoped by `pk` for lower RU. |
| **Hierarchy edges** | `manages` (tenant → unit), `contains` (unit → sub-unit, e.g. building→floor, plant→section) |
| **Placement edges** | `hosts` (unit → gateway), `connects_to` (gateway → equipment) |
| **Sensor edges** | `monitors` (sensor → equipment), `assigned_to` (sensor → gateway) |
| **Edge properties** | e.g. `protocol`, `signal_strength` on `connects_to`; `priority`, `position` on `monitors` |

Two tenants share the same **label and edge types**; they are distinguished by `pk` and by which vertices/edges the seed created.

**In the diagrams below**: **V:** = vertex (node), **E:** = edge (relationship). Arrow or vertical bar shows edge direction.

---

## Diagram 1 — Hierarchy and flow (tenant → sensor)

One logical path from a tenant to its sensors. **V** = vertex (node), **E** = edge (relationship). Direction: arrow shows edge direction (e.g. tenant → unit).

```
  V: tenant
       |
  E: manages
       |
  V: unit  (building, plant, floor, section, room)
       |
  E: hosts
       |
  V: gateway
       |
  E: connects_to
       |
  V: equipment  <————— E: monitors —————  V: sensor
       ^                      |
       |                      |  E: assigned_to
       |                      v
       +——————————————  V: gateway  (same gateway as above)
```

**Chain in words**: tenant —(manages)→ unit —(hosts)→ gateway —(connects_to)→ equipment ←(monitors)— sensor; and sensor —(assigned_to)→ gateway. The labs walk this chain and add filters (e.g. by label or partition) to reduce cost.

---

## Diagram 2 — One tenant’s shape (Acme, simplified)

Same convention: **V** = vertex, **E** = edge. Names in parentheses are vertex `id` or `name`; labels next to arrows are edge labels.

```
  V: tenant (Acme, id: tenant-1)
       |
  E: manages
       |
  V: unit (Building-A) —— E: contains —— V: unit (Floor-1) —— E: contains —— V: unit (Room-101)
       |                                    |
       |                                    V: unit (Floor-2) —— E: contains —— V: unit (Room-102)
       |
       |   E: hosts
       +——> V: gateway (GW-001) —— E: connects_to —— V: equipment (HVAC-101) <—— E: monitors —— V: sensor (TEMP-001)
       |                                    |                                              |
       |                                    |              E: assigned_to (sensor → gateway)
       |                                    |                                              v
       |                                    +—————————————— V: equipment (Pump-201)    V: gateway (GW-001)
       |
       |   E: hosts
       +——> V: gateway (GW-002) —— E: connects_to —— ...
       |
  V: unit (Building-B) —— E: hosts —— V: gateway (GW-003), V: gateway (GW-004) —— ...
```

Same pattern for GlobalTech (tenant-2): V: tenant → E: manages → V: unit (plants) → E: contains → V: unit (sections); E: hosts → V: gateway; E: connects_to → V: equipment; sensors with E: monitors and E: assigned_to. Every vertex has `pk` = `tenant-2`.

---

## How the labs use this

- **Labs 1–3**: Explore one layer at a time (units, gateways/equipment, sensors) and see partition vs cross-partition RU.
- **Lab 4**: Run the full chain (Diagram 1), then add partition scope and early reduction and compare RU.
- **Labs 5–6**: Many-to-many (one gateway ↔ many equipment) and edge metadata (properties on edges).
- **Labs 7–9**: Denormalization, complex patterns (shared gateway, sensors by type, path with edge props), and order/limit (barrier) tuning.

---

# 1. Hierarchy and partition key

The graph has two tenants; every vertex has `pk` = `tenant-1` or `tenant-2`. Queries that include `has('pk', 'tenant-1')` (or tenant-2) are partition-scoped and typically cost fewer RUs than cross-partition scans. We use this in multi-tenant dashboards and alerts.

---

## Lab 1: Explore the tenant–unit hierarchy (~20 min)

The seed gives you a multi-level hierarchy (tenant → building/plant → floor/section → room) with `manages` and `contains`. These queries explore it.

1. Count units by type (building, floor, plant, etc.):

```gremlin
g.V().hasLabel('unit').groupCount().by('type')
```

2. From Acme, one step manages then one step contains (direct children of Acme's units — floors, rooms): “All direct children of Acme’s units” (e.g. floors, rooms). Run:

```gremlin
g.V('tenant-1').out('manages').out('contains').values('name')
```

3. Units directly managed by GlobalTech. Run:

```gremlin
g.V('tenant-2').out('manages').values('name')
```

4. Sections under Plant-1 (you should see Section-A, Section-B):

```gremlin
g.V('unit-plant1').out('contains').values('name')
```

5. Unit count for Acme only (partition-scoped). Note the RU — we'll compare with the next one:

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('unit').count()
```

6. Same count but without the partition filter (cross-partition). Run and note RU:

```gremlin
g.V().hasLabel('unit').count()
```

You’ll typically see higher RU here than in step 5 — that’s the cost of hitting all partitions.

---

## Lab 2: Explore gateways and equipment (~15 min)

Units host gateways; gateways connect to equipment. A few queries to see that in the data.

1. Equipment count per tenant (use `pk` so each query stays in one partition):

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('equipment').count()
```

```gremlin
g.V().has('pk', 'tenant-2').hasLabel('equipment').count()
```

2. Gateways hosted by Plant-1. Run:

```gremlin
g.V('unit-plant1').out('hosts').values('name', 'model')
```

3. Equipment connected by GW-001. Run:

```gremlin
g.V('gw-001').out('connects_to').values('name', 'type')
```

You should see HVAC-101, Pump-201 and their types (and more once you’ve done Lab 5).

4. Payload matters: full vertex vs just the fields you need. Run both and compare RU:

```gremlin
g.V('gw-001').out('connects_to').valueMap(true)
```

```gremlin
g.V('gw-001').out('connects_to').values('name', 'type')
```

Smaller projection usually means lower RU.

---

## Lab 3: Explore sensor assignments (~15 min)

Sensors link to equipment (monitors) and to a gateway (assigned_to). Quick check that both show up.

1. Sensors that monitor HVAC-101:

```gremlin
g.V('equip-hvac101').in('monitors').values('name', 'sensor_type')
```

2. All sensors assigned to one gateway. Run:

```gremlin
g.V('gw-001').in('assigned_to').values('name', 'sensor_type')
```

3. One sensor’s equipment and gateway. Run each:

```gremlin
g.V('sensor-temp001').out('monitors').values('name').fold()
```

```gremlin
g.V('sensor-temp001').out('assigned_to').values('name').fold()
```

(Two separate result rows: equipment name(s) and gateway name.)

---

# 2. Full-chain traversal and early reduction

From tenant to sensors we traverse: `manages` → `hosts` → `connects_to` → `in('monitors')`. Adding filters (e.g. `hasLabel('equipment')`) earlier in the chain reduces traversers before later steps — same idea as Day 03 reduction.

**Traversal in words**: Start at tenant → follow manages → then hosts → then connects_to → then go *back* along monitors (in) to sensors → return names.

---

## Lab 4: Query the hierarchy (~15 min)

Run the full chain from tenant to sensors, then a tuned version — compare RUs.

1. From Acme, list all sensor names along the chain (tenant → unit → gateway → equipment → sensor). Run and note RU:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .values('name')
```

**Expected**: List of sensor names (e.g. TEMP-001, PRES-001, HUM-001, …).

2. Same list of sensors, but this time we filter to equipment before hitting sensors and dedup. Often cheaper. Run and note RU:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .hasLabel('equipment')
  .in('monitors')
  .dedup()
  .values('name')
```

Compare RU with step 1: partition + early `hasLabel` and `dedup` often reduce cost.

3. Vertex and edge counts. Run:

```gremlin
g.V().count()
```

```gremlin
g.E().count()
```

4. Composition by label. Run:

```gremlin
g.V().groupCount().by(label)
```

```gremlin
g.E().groupCount().by(label)
```

5. Per-tenant vertex count (partition key). Run each:

```gremlin
g.V().has('pk', 'tenant-1').count()
```

```gremlin
g.V().has('pk', 'tenant-2').count()
```

6. **Path**: show path from tenant to first sensor. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .limit(1)
  .path()
  .by('name')
```

**Expected**: One path like [Acme Corp, Building-A, GW-001, HVAC-101, TEMP-001].

7. In Data Explorer, switch to the **Graph** tab and run `g.V().has('pk', 'tenant-1')` to visualize one tenant’s graph.

---

# 3. Many-to-many relationships

One gateway can connect to many equipment; one equipment can be connected by many gateways. One sensor can monitor multiple equipment (e.g. zone sensor). We add one edge of each type and then query.

**Diagram — many-to-many**: One gateway, multiple equipment; one sensor, multiple equipment. **V** = vertex, **E** = edge.

```
  V: gateway (GW-001) —— E: connects_to ——> V: equipment (HVAC-101)
        |                      E: connects_to ——> V: equipment (Pump-201)   (one gateway, two equipment)
        └—— E: connects_to ——> V: equipment (Pump-201)

  V: sensor (TEMP-001) —— E: monitors ——> V: equipment (HVAC-101)
        |                     E: monitors ——> V: equipment (HVAC-102)   (one sensor, two equipment)
        └—— E: assigned_to ——> V: gateway (GW-001)
```

---

## Lab 5: Many-to-many relationships (~15 min)

**Goal**: Add one shared connection and one zone sensor, then query many-to-many.

1. Add another `connects_to` from an existing gateway to another equipment (e.g. shared gateway). Run:

```gremlin
g.V('gw-001').addE('connects_to').to(g.V('equip-pump201')).property('protocol', 'modbus').property('signal_strength', 88)
```

2. Add another `monitors` from one sensor to a second equipment (zone sensor). Run:

```gremlin
g.V('sensor-temp001').addE('monitors').to(g.V('equip-hvac102')).property('attached_date', '2025-01-10').property('position', 'zone')
```

3. All equipment for a gateway. Run:

```gremlin
g.V('gw-001').out('connects_to').values('name', 'type')
```

4. All gateways for one equipment. Run:

```gremlin
g.V('equip-pump201').in('connects_to').hasLabel('gateway').values('name')
```

5. Equipment monitored by more than one sensor. Run:

```gremlin
g.V().hasLabel('equipment')
  .where(__.in('monitors').count().is(gt(1)))
  .values('name')
```

**Expected**: Equipment that has at least two sensors (e.g. HVAC-101 after step 2).

---

# 4. Edge metadata

Edges can carry context: priority, SLA, protocol, signal strength. Querying by edge properties (e.g. `has('priority', 'critical')`) supports alerting and reporting.

---

## Lab 6: Edge metadata (~15 min)

**Goal**: Edges carry context (priority, SLA); we add properties and query by them.

1. **What**: Add `priority` and `alert_threshold` on the monitors edge from sensor-temp003 (position housing). **Why**: Mark critical sensor–equipment links for alerting. Run:

```gremlin
g.V('sensor-temp003').outE('monitors').has('position', 'housing').property('priority', 'critical').property('alert_threshold', 90)
```

2. Add SLA on a `connects_to` edge. Run:

```gremlin
g.V('gw-001').outE('connects_to').where(__.inV().has('name', 'HVAC-101')).property('sla_tier', 'gold').property('max_latency_ms', 100)
```

3. Query edges by property. Run:

```gremlin
g.E().hasLabel('monitors').has('priority', 'critical').valueMap()
```

4. Sensor names that have a critical-priority monitors edge. Run:

```gremlin
g.E().hasLabel('monitors').has('priority', 'critical').outV().values('name')
```

**Expected**: Sensor name(s) with that edge (e.g. TEMP-003).

5. Equipment names for those critical monitors. Run:

```gremlin
g.E().hasLabel('monitors').has('priority', 'critical').inV().values('name')
```

---

# 5. Denormalization and read cost

Traversing multiple hops (e.g. equipment → gateway → unit → tenant) costs RU. Storing a copy on the vertex (e.g. `tenant_name` on equipment) avoids the traversal and often reduces read cost. Trade-off: the copy must be updated when the source changes.

---

## Lab 7: Denormalization (~15 min)

**Goal**: Compare traversal cost vs storing a copy of a property on the vertex.

1. **What**: Get tenant name by traversing equipment → gateway → unit → tenant. **Why**: Baseline RU for “lookup by traversal.” Run and note RU:

```gremlin
g.V('equip-hvac101').in('connects_to').in('hosts').in('manages').values('name')
```

2. Add `tenant_name` on tenant-1 equipment. Run:

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('equipment').property('tenant_name', 'Acme Corp')
```

3. **What**: Read tenant name from the equipment vertex (no hops). **Why**: Compare RU with step 1; denormalization avoids the traversal. Run and note RU:

```gremlin
g.V('equip-hvac101').values('name', 'tenant_name')
```

4. Compare RUs: note Request Charge for the traversal (step 1) vs the direct read (step 3). Fill a small table:

| Query | RU (your run) |
|-------|----------------|
| Four-hop to tenant name | _____ |
| Direct tenant_name on vertex | _____ |

Direct property is usually cheaper; use denormalization when reads dominate and the copied value changes rarely.

---

# 6. Complex and real-world patterns

Combining traversals, filters, `where`, and `dedup` supports patterns like: “equipment that shares a gateway with X,” “gateways that have both HVAC and pump,” “sensors by type per tenant,” “equipment with no sensors,” “path including edge properties.”

**Diagram — “equipment that shares a gateway with HVAC-101”**: From HVAC-101 go to its gateway(s), then to *other* equipment on that gateway; exclude HVAC-101.

```
  V: equipment (HVAC-101)  [start]
         |  E: connects_to (in: equipment to gateway)
         v
  V: gateway (GW-001)
         |
         |  E: connects_to (out: gateway to other equipment; exclude HVAC-101)
         v
  V: equipment (Pump-201), …
```

---

## Lab 8: Complex relationship queries (~20 min)

**Goal**: Real-world patterns: shared gateway, gateways by equipment type, sensors by type, path with edge properties, optional edge.

1. **What**: Equipment that shares at least one gateway with HVAC-101 (colocation / failure-domain). **Why**: “If this gateway fails, which other equipment is affected?” Run:

```gremlin
g.V('equip-hvac101').as('start')
  .in('connects_to')
  .out('connects_to')
  .where(neq('start'))
  .dedup()
  .values('name')
```

**Expected**: Other equipment names (e.g. Pump-201). From HVAC-101 → its gateway(s) → other equipment, exclude self.

2. **What**: All sensors under tenant-1 along the full chain; dedup so each sensor appears once. **Why**: Tenant sensor inventory. Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .dedup()
  .values('name', 'sensor_type')
```

**Expected**: Pairs of sensor name and type. Use for tenant-wide sensor inventory.

3. **What**: Gateways that connect to at least one HVAC *and* at least one pump. **Why**: “Which gateways serve mixed equipment types?” Run:

```gremlin
g.V().hasLabel('gateway')
  .where(__.out('connects_to').has('type', 'hvac'))
  .where(__.out('connects_to').has('type', 'pump'))
  .values('name')
```

**Expected**: GW-001 (and any other gateway with both). Pattern: “vertices where two different conditions hold.”

4. **What**: For tenant-1, traverse to all sensors then group count by `sensor_type`. **Why**: Dashboard — “sensor types per tenant.” Run:

```gremlin
g.V('tenant-1')
  .out('manages')
  .out('hosts')
  .out('connects_to')
  .in('monitors')
  .dedup()
  .groupCount()
  .by('sensor_type')
```

**Expected**: Map like {temperature: 2, pressure: 2, humidity: 1, …}. Real-world: dashboard “sensor types per tenant.”

5. **What**: From a gateway, list each connected equipment with its edge properties (protocol, signal_strength). **Why**: Connection health or SLA reporting. Run:

```gremlin
g.V('gw-001')
  .outE('connects_to')
  .project('equipment', 'protocol', 'signal')
  .by(__.inV().values('name'))
  .by('protocol')
  .by('signal_strength')
```

**Expected**: List of maps: equipment name, protocol, signal_strength. Use for connection health or SLA reporting.

6. **Shortest path** from tenant-1 to sensor-temp001 (by name). Run:

```gremlin
g.V('tenant-1')
  .repeat(__.both().simplePath())
  .until(__.hasId('sensor-temp001'))
  .path()
  .by('name')
  .limit(1)
```

**Expected**: One path as a list of names. On large graphs this can be costlier; keep scope small.

7. **What**: Equipment connected with `signal_strength` &lt; 90. **Why**: “Weak links” for maintenance or diagnostics. Run:

```gremlin
g.V().hasLabel('gateway')
  .outE('connects_to')
  .has('signal_strength', lt(90))
  .inV()
  .values('name', 'type')
```

**Expected**: Equipment connected with signal_strength &lt; 90. Real-world: “weak links” for maintenance.

8. Visualize: run `g.V().has('pk', 'tenant-1')` and switch to the **Graph** tab.

---

# 7. Barrier steps: order and limit

Sorting is a barrier: Cosmos DB must collect enough traversers to sort. As in Day 03, `order().by('name').limit(3)` sorts everything then takes three; `limit(3).order().by('name')` takes three then sorts them (different result, often lower RU if you only need a few).

---

## Lab 9: Order and limit placement (~10 min)

1. **Full sort then limit** (barrier then trim). Run and note RU:

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('equipment').order().by('name').limit(3).values('name')
```

2. **Limit then sort** (trim then barrier on smaller set). Run and note RU:

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('equipment').limit(3).order().by('name').values('name')
```

**Expected**: Step 1 returns the three alphabetically first equipment names; step 2 returns the first three (by natural order) then sorted. Different semantics; step 2 is often cheaper when the graph grows.

3. **Real-world: equipment count by status per tenant** (aggregation with partition key). Run:

```gremlin
g.V().has('pk', 'tenant-1').hasLabel('equipment').groupCount().by('status')
```

**Expected**: Map e.g. {running: 6, stopped: 2}. Use for tenant health or dashboard KPIs.

---

**Wrap-up**: We touched hierarchy, partition key, payload vs projection, edge metadata, denormalization, and a few real-world patterns (shared gateway, sensors by type, path with edge props, order/limit). Keep comparing RUs when you change queries — same habit as Day 03.
