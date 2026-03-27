# Day 10 — End-to-End Use Cases & Labs

**Duration:** ~3 hours  
**Prerequisite:** Days 01–09 completed. Performance dataset loaded (~240,000 vertices, ~431,000 edges, 55 tenants). You already know Gremlin patterns, optimization, and migration ideas from earlier labs.

**Participation:** Trainer-controlled — run queries **exactly** as directed. Do **not** change traversal depth, filters, or projections unless the instructor asks. Run each approved query **once** unless told to repeat.

**Graph reminder:** IoT asset graph; partition key **`pk`**. Performance labs use **`tenant-006`**. Shape: tenant → manages → unit → hosts → gateway → connects_to → equipment; sensor → monitors → equipment, assigned_to → gateway; unit → contains → unit.

**Cosmos DB Gremlin:** For `order().by(...)`, use **`incr`** / **`decr`** for sort direction — not `asc` / `desc`.

---

## Lab 1: Gateways for a tenant (pagination)

### 1.1 — Baseline list

**Scenario:** From a tenant vertex, walk to all gateways in that tenant and list names — feel the result size (~150 gateways for a performance tenant).

**Equivalent SELECT:** Join tenant → units → gateways; `SELECT name FROM gateway WHERE ...`.

**Path:** `V('tenant-006')` → `out('manages')` → `out('hosts')` → `hasLabel('gateway')` → `values('name')`.

**Tip:** Note **request charge (RU)** in Data Explorer — baseline for comparison.

---

### 1.2 — Stable sort

**Scenario:** Same traversal, add **`order().by('name', incr)`** before taking values — pagination needs a **deterministic** order.

**Equivalent SELECT:** `ORDER BY name`.

**Path:** Same chain as §1.1, with order on gateway vertices before `values`.

**Tip:** `order()` is a **barrier** — sorts the gateway set; RU usually **increases** vs unsorted stream.

---

### 1.3 — Page 1 (range + project)

**Scenario:** First page: 10 gateways as JSON-shaped fields (`name`, `model`, `status`, `ip_address`).

**Equivalent SELECT:** `ORDER BY name OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY` plus selected columns.

**Path:** Same chain → `order().by('name', incr)` → **`range(0, 10)`** (0-based, end exclusive) → **`project`** with `.by` for each field.

**Tip:** **`range(a, b)`** = rows **a** through **b − 1**.

**RU note (common confusion):** **`order()` is a barrier** — the engine still has to **obtain and sort the full set** of gateways for that tenant (e.g. ~150) to sort by name. **`range()` does not remove that cost** on each page request. It **limits how many vertices go through `project`** and **shrinks the response** per call. So Page 1 and Page 2 often have **similar** RU, not “10× cheaper” than sorting everything. Pagination here is for **correct pages**, **smaller payloads**, and **less work after the sort** — not for skipping the sort itself.

**Total RU if you load every page:** **10** separate calls with `order` + different `ranges` usually cost **more RU in total** than **one** call with `order` and **no** `range` that returns the full sorted list — because you repeat traversal + sort **once per request**. Pagination optimizes **each response** and **UX**, not **minimum RU to fetch the entire dataset**.

---

### 1.4 — Page 2

**Scenario:** Next 10 gateways — same order, **`range(10, 20)`**.

**Equivalent SELECT:** Second page with same `ORDER BY`.

**Path:** Identical to §1.3 except **`range(10, 20)`**.

**Tip:** If sort or filter changes between requests, pages can **shift** — APIs freeze sort keys.

---

### 1.5 — Total count for pagination metadata

**Scenario:** Separate query returning **total** gateway count for the tenant — used for “page X of Y” or `Link` headers.

**Equivalent SELECT:** `SELECT COUNT(*) FROM gateway WHERE tenant = ?` (conceptually).

**Path:** Same `V → manages → hosts → gateway` chain, then **`count()`** (no `range`).

**Tip:** Count + paged reads are **two requests** — normal for Cosmos-backed APIs.

**Flow (ASCII):**

```
  tenant ──manages──► units ──hosts──► gateways
                              │
                    order(name) → range(page) → project
                              │
                    parallel: same chain → count()  ← total for UI
```

**Success:** You can explain why **order + range** and **count** go together for REST-style paging, and why **`range` does not make `order` free** in terms of RU.

---

## Lab 2: Full asset hierarchy (depth, sort, page, children)

### 2.1 — Depth in the tree

**Scenario:** From tenant, walk **`manages`**, then **`emit()`** + **`repeat(out('contains'))`** with **`until(loops().is(5))`**. Project `name`, `type`, and **depth** (`loops()`).

**Equivalent SELECT:** Recursive CTE with depth column capped at max depth.

**Path:** `V('tenant-006')` → `out('manages')` → `emit()` → `repeat(__.out('contains'))` → `until(__.loops().is(5))` → `project` with depth from `__.loops()`.

**Tip:** `emit()` **before** `repeat()` includes **starting** vertices (e.g. buildings).

---

### 2.2 — Sort by depth then name (Cosmos-safe)

**Scenario:** Tree-like listing: shallow nodes first, then alphabetical by name.

**Equivalent SELECT:** `ORDER BY depth, name`.

**Path:** Same repeat pattern as §2.1, then **`order().by(__.loops(), incr).by('name', incr)`** on vertices, **then** `project` — avoid ordering on **projected maps** with `select('depth')` in Cosmos.

**Tip:** Same lesson as Day 08 — **order on vertices**, then project.

---

### 2.3 — Paginated hierarchy sample

**Scenario:** Order units by name, take **first 10**, project `name` and `type`.

**Equivalent SELECT:** Ordered hierarchy with `LIMIT 10`.

**Path:** Same `emit` / `repeat` / `until` → `order().by('name', incr)` → **`range(0, 10)`** → `project`.

**Tip:** `range` after `order` defines the **window** on the sorted stream.

---

### 2.4 — Child count for UI expand/collapse

**Scenario:** Each row includes **how many** `contains` children the unit has.

**Equivalent SELECT:** Correlated `COUNT` of children per row.

**Path:** Same hierarchy traverser, `project` with `.by(__.out('contains').count())` for the `children` field.

**Tip:** Buildings usually **> 0** children; floors may be **0** — matches expand/collapse UX.

**Success:** Hierarchy queries tied to **API pagination** and **UI** patterns.

---

## Lab 3: Equipment + sensors + health

### 3.1 — Nested sensor list

**Scenario:** Sample **10** equipment vertices; each row lists related sensors (name, type, status) as a **nested structure** (`fold`).

**Equivalent SELECT:** Parent row + JSON array of child sensors from JOIN.

**Path:** `hasLabel('equipment')` + `has('pk','tenant-006')` → **`limit(10)`** → `project` including `in('monitors')` → nested `project` → **`fold()`**.

**Tip:** Sub-traversals per equipment **multiply** cost — keep `limit` small.

---

### 3.2 — Counts per equipment

**Scenario:** For **20** equipment rows, show total sensors and **active** sensors.

**Equivalent SELECT:** `COUNT(*)` and `SUM(CASE WHEN status='active' ...)` per equipment.

**Path:** `limit(20)` → `project` with `.by(__.in('monitors').count())` and `.by(__.in('monitors').has('status','active').count())`.

**Tip:** Compare RU with §3.1 — two counts vs one folded list.

---

### 3.3 — Tenant equipment status histogram

**Scenario:** **`groupCount().by('status')`** on all equipment in the partition.

**Equivalent SELECT:** `GROUP BY status` with counts.

**Path:** Equipment + `has('pk','tenant-006')` → `groupCount().by('status')`.

**Tip:** Scoped to **one pk** — not a full-account scan.

---

### 3.4 — Uninstrumented equipment

**Scenario:** Equipment with **zero** incoming `monitors` edges.

**Equivalent SELECT:** Equipment with no child rows in sensor join.

**Path:** `where(__.in('monitors').count().is(0))` → `values('name')`.

**Tip:** Data-quality signal for onboarding / commissioning.

---

### 3.5 — All sensors inactive (but some exist)

**Scenario:** Equipment that has sensors, but **none** with `status = active`.

**Equivalent SELECT:** `HAVING COUNT(active)=0 AND COUNT(*)>0`.

**Path:** Two `where` clauses: active count **0**, total monitor count **> 0**.

**Tip:** Operational alert — different from “no sensors at all.”

**Success:** Dashboard-style aggregates and **negative** conditions on edges.

---

## Lab 4: Orphaned sensors

### 4.1 — No `monitors` edge

**Scenario:** Sensors that do not **monitor** any equipment.

**Equivalent SELECT:** Sensor rows with no FK to equipment.

**Path:** Sensors + pk → **`not(__.out('monitors'))`** → `values('name','sensor_type')`.

**Tip:** `not(inner)` keeps vertices where **inner emits nothing**.

---

### 4.2 — No `assigned_to` gateway

**Scenario:** Sensors not assigned to any gateway.

**Equivalent SELECT:** Missing gateway FK.

**Path:** **`not(__.out('assigned_to'))`**.

**Tip:** Different defect from §4.1 — connectivity vs assignment.

---

### 4.3 — Fully orphaned

**Scenario:** Neither monitors nor assigned_to.

**Equivalent SELECT:** Both FKs null / missing.

**Path:** **`not(__.out('monitors'))`** and **`not(__.out('assigned_to'))`**.

**Tip:** Strongest “bad row” signal for IoT ingestion.

---

### 4.4 — Monitors something but not assigned

**Scenario:** Partial orphan — has equipment link, no gateway link.

**Equivalent SELECT:** One FK present, the other absent.

**Path:** **`where(__.out('monitors'))`** + **`not(__.out('assigned_to'))`** + `project` with equipment names **`fold`**.

**Tip:** Typical for **misconfigured** routing or partial migration.

**Success:** You can combine **existence** and **non-existence** of specific edge labels.

---

## Lab 5: Hierarchy consistency

### 5.1 — Units without parent

**Scenario:** Units that are not under **`contains`** nor **`manages`** — discuss whether any results are expected (model-dependent).

**Equivalent SELECT:** Orphan unit rows.

**Path:** `hasLabel('unit')` + pk → `not(in('contains'))` + `not(in('manages'))`.

**Tip:** After bad loads, unexpected roots appear.

---

### 5.2 — Equipment without gateway

**Scenario:** Equipment with no incoming **`connects_to`**.

**Equivalent SELECT:** Equipment missing gateway relationship.

**Path:** `not(__.in('connects_to'))`.

**Tip:** Breaks gateway-centric dashboards.

---

### 5.3 — Gateway without host unit

**Scenario:** Gateways with no incoming **`hosts`**.

**Equivalent SELECT:** Gateway missing unit.

**Path:** `not(__.in('hosts'))`.

**Tip:** Often migration ordering bugs (unit after gateway).

---

### 5.4 — Reachable vs total units

**Scenario:** Count units reachable from **`V('tenant-006')`** via `manages` + `emit` + `repeat(contains)` (cap depth **10**). Compare to **`count()`** of all units with same pk.

**Equivalent SELECT:** Reachable subgraph size vs table row count.

**Path:** Tenant-rooted walk with `until(loops().is(10))` → `count()` vs global unit `count()` for pk.

**Tip:** **Mismatch** ⇒ disconnected subgraph or wrong edges.

---

### 5.5 — Cycle check (concept)

**Scenario:** Detect a **cycle** along `contains` (should be **empty** result if hierarchy is a DAG).

**Equivalent SELECT:** Graph cycle detection (conceptual).

**Path:** Instructor shows Gremlin that marks a start, repeats `out('contains')`, stops at depth or when returning to start, and inspects **`path`**.

**Tip:** **Empty** = good; non-empty path = fix data before unbounded traversals in production.

**Success:** Post-migration **integrity** mindset: reachability + cycles.

---

## Lab 6: Performance debugging

### 6.1 — Bad query (instructor only)

**Scenario:** One deliberately **expensive** query: cross-partition sensor scan, unlabeled `out()`, wide property map, long nonsense chain — **high RU**. The instructor may run it **once** for the class; **students do not run it**.

**Equivalent SELECT:** Worst-case full table scan with useless joins.

**Path:** Concept only — `hasLabel('sensor')` without pk, **`out().out().out()`**, **`valueMap(true)`**.

**Tip:** Memorize the **failure modes**: no **pk**, no **edge labels**, **fat** projections, wrong **start** and **direction**.

---

### 6.2 — Partition + labels + slim read

**Scenario:** Same intent as a “fixed” path: **`has('pk','tenant-006')`**, explicit edge labels **`monitors` → connects_to → hosts → manages**, **`values('name')`**.

**Equivalent SELECT:** Filtered joins with selected columns.

**Path:** Sensor + pk → labeled hops to tenant side → minimal properties.

**Tip:** Watch RU **drop** vs the story in §6.1.

---

### 6.3 — Point start + deduped sensor count

**Scenario:** Start at **`V('tenant-006')`**, `project` tenant name and **dedup** sensor count across the fan-out.

**Equivalent SELECT:** Single-row dashboard with aggregate.

**Path:** `project('tenant','sensor_count').by('name').by(__.out('manages').out('hosts').out('connects_to').in('monitors').dedup().count())`.

**Tip:** **Known id** start is often the cheapest entry point.

---

### 6.4 — Document the journey

**Scenario:** Fill a **three-row** table: unoptimized vs pk-scoped vs point-start — approximate RU and **one bullet** of why.

**Equivalent SELECT:** N/A — operational worksheet.

**Path:** Compare Data Explorer metrics for the queries your instructor runs.

**Tip:** At course scale, **50–100×** RU gaps between naive and tuned are plausible — not academic.

**Success:** You can **name** optimizations: partition scope, edge labels, projection, traversal root.

---

## Day 10 short solutions (Gremlin only)

### 1.1
```gremlin
g.V('tenant-006').out('manages').out('hosts').hasLabel('gateway').values('name')
```

### 1.2
```gremlin
g.V('tenant-006').out('manages').out('hosts').hasLabel('gateway').order().by('name', incr).values('name')
```

### 1.3
```gremlin
g.V('tenant-006').out('manages').out('hosts').hasLabel('gateway')
  .order().by('name', incr).range(0, 10)
  .project('name', 'model', 'status', 'ip_address').by('name').by('model').by('status').by('ip_address')
```

### 1.4
```gremlin
g.V('tenant-006').out('manages').out('hosts').hasLabel('gateway')
  .order().by('name', incr).range(10, 20)
  .project('name', 'model', 'status', 'ip_address').by('name').by('model').by('status').by('ip_address')
```

### 1.5
```gremlin
g.V('tenant-006').out('manages').out('hosts').hasLabel('gateway').count()
```

### 2.1
```gremlin
g.V('tenant-006').out('manages').emit().repeat(__.out('contains')).until(__.loops().is(5))
  .project('name', 'type', 'depth').by('name').by('type').by(__.loops())
```

### 2.2
```gremlin
g.V('tenant-006').out('manages').emit().repeat(__.out('contains')).until(__.loops().is(5))
  .order().by(__.loops(), incr).by('name', incr)
  .project('name', 'type', 'depth').by('name').by('type').by(__.loops())
```

### 2.3
```gremlin
g.V('tenant-006').out('manages').emit().repeat(__.out('contains')).until(__.loops().is(5))
  .order().by('name', incr).range(0, 10)
  .project('name', 'type').by('name').by('type')
```

### 2.4
```gremlin
g.V('tenant-006').out('manages').emit().repeat(__.out('contains')).until(__.loops().is(5))
  .project('name', 'type', 'children').by('name').by('type').by(__.out('contains').count())
```

### 3.1
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').limit(10)
  .project('equipment', 'type', 'status', 'sensors')
  .by('name').by('type').by('status')
  .by(__.in('monitors').project('name', 'type', 'status').by('name').by('sensor_type').by('status').fold())
```

### 3.2
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').limit(20)
  .project('equipment', 'status', 'total_sensors', 'active_sensors')
  .by('name').by('status')
  .by(__.in('monitors').count())
  .by(__.in('monitors').has('status', 'active').count())
```

### 3.3
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').groupCount().by('status')
```

### 3.4
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').where(__.in('monitors').count().is(0)).values('name')
```

### 3.5
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006')
  .where(__.in('monitors').has('status', 'active').count().is(0))
  .where(__.in('monitors').count().is(gt(0)))
  .values('name')
```

### 4.1
```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006').not(__.out('monitors')).values('name', 'sensor_type')
```

### 4.2
```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006').not(__.out('assigned_to')).values('name', 'sensor_type')
```

### 4.3
```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006').not(__.out('monitors')).not(__.out('assigned_to')).values('name')
```

### 4.4
```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006')
  .where(__.out('monitors'))
  .not(__.out('assigned_to'))
  .project('sensor', 'monitors_equipment')
  .by('name')
  .by(__.out('monitors').values('name').fold())
```

### 5.1
```gremlin
g.V().hasLabel('unit').has('pk', 'tenant-006').not(__.in('contains')).not(__.in('manages')).values('name', 'type')
```

### 5.2
```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').not(__.in('connects_to')).values('name')
```

### 5.3
```gremlin
g.V().hasLabel('gateway').has('pk', 'tenant-006').not(__.in('hosts')).values('name')
```

### 5.4 (reachable count)
```gremlin
g.V('tenant-006').out('manages').emit().repeat(__.out('contains')).until(__.loops().is(10)).count()
```

### 5.4 (total count)
```gremlin
g.V().hasLabel('unit').has('pk', 'tenant-006').count()
```

### 5.5
```gremlin
g.V().hasLabel('unit').has('pk', 'tenant-006').as('start')
  .repeat(__.out('contains'))
  .until(__.or(__.loops().is(10), __.where(eq('start'))))
  .where(eq('start'))
  .path().by('name')
```

### 6.1 (instructor only)
```gremlin
g.V().hasLabel('sensor').out('monitors').out().out().out().hasLabel('tenant').valueMap(true)
```

### 6.2
```gremlin
g.V().has('pk', 'tenant-006').hasLabel('sensor')
  .out('monitors').in('connects_to').in('hosts').in('manages')
  .values('name')
```

### 6.3
```gremlin
g.V('tenant-006')
  .project('tenant', 'sensor_count')
  .by('name')
  .by(__.out('manages').out('hosts').out('connects_to').in('monitors').dedup().count())
```

---

## Day 10 wrap-up

You practiced **API pagination**, **hierarchy** reads, **operational dashboards**, **data-quality** Gremlin, **integrity** checks, and a **performance** narrative — end-to-end skills for a Cosmos DB Gremlin workload.
