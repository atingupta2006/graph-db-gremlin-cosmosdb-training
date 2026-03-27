# Day 09 — Monitoring, Best Practices & Migration Strategies

**Graph reminder:** IoT asset graph; partition key `pk`. Seed data: `tenant-1`, `unit-bldgA`, `equip-hvac101`, etc. Migration labs use isolated partitions **`mig-tenant-1`** and **`mig2-t1`** (create, verify, then drop). Prerequisite: Day 08.

---

## Lab 1: Monitor RU in Azure Portal

### 1.1 — Portal metrics

**Scenario:** Use **Azure Portal → Cosmos DB account → Monitoring → Metrics**; chart **Total Request Units** (last 1 hour).

**Equivalent SELECT:** N/A — Azure Metrics, not SQL.

**Path:** Portal UI; correlate spikes with queries you run in Data Explorer.

**Tip:** Also chart **Total Requests**, split by **status** (watch **429** throttling).

---

### 1.2 — Sample queries for metrics demo

**Scenario:** Run a cheap lookup then a heavier pk-scoped scan so the metrics chart shows a difference.

**Equivalent SELECT:** `SELECT * WHERE id = ?` vs `SELECT name, status FROM equipment WHERE pk = ?`.

**Path:** `g.V('tenant-006')` then equipment with pk + two properties.

**Tip:** Alerts: e.g. RU approaching provisioned throughput.

---

## Lab 2: Query metrics in Data Explorer

### 2.1 — Simple fetch

**Scenario:** Note **Request Charge**, **Server execution time**, **Activity ID** for a limited equipment query.

**Equivalent SELECT:** `SELECT name FROM equipment WHERE pk = ? LIMIT 20`.

**Path:** equipment + pk + values + limit.

**Tip:** Activity ID for support tickets.

---

### 2.2 — Projection with sub-traversals

**Scenario:** Same limit; compare RU with Task 1 — `project` + `in('monitors').fold()` costs more.

**Equivalent SELECT:** SELECT with correlated subqueries for related sensors.

**Path:** limit(20) → project with fold on monitors.

**Tip:** Sub-traversals per row multiply cost.

---

### 2.3 — Cross-partition count (trainer demo)

**Scenario:** `g.V().hasLabel('equipment').count()` — all partitions; high RU. Trainer runs once.

**Equivalent SELECT:** `SELECT COUNT(*) FROM equipment` with no partition filter.

**Path:** Full scan count.

**Tip:** Don’t use in production dashboards without caching.

---

### 2.4 — Zero-result query

**Scenario:** `hasLabel('nonexistent')` — still consumes RU.

**Equivalent SELECT:** Query matching zero rows still scans.

**Path:** hasLabel filter on unknown label.

**Tip:** Engine still plans/executes the request.

---

### 2.5 — Metrics table

**Scenario:** Fill the lab table with RU, time, and result count for each query type.

**Equivalent SELECT:** N/A.

**Path:** Document from Data Explorer response headers.

**Tip:** Server time excludes network latency.

---

## Lab 3: Common pitfalls

### 3.1 — Missing partition key

**Scenario:** Same pattern with/without `has('pk', 'tenant-006')` — compare RU.

**Equivalent SELECT:** `WHERE pk = ?` vs no partition predicate.

**Path:** equipment limit 20 cross-partition vs single-partition.

**Tip:** Always scope by pk when possible.

---

### 3.2 — Unbounded vs bounded repeat

**Scenario:** Dangerous `repeat(__.out()).emit()` — trainer explains only. Safe: `repeat(__.out('contains')).emit().until(loops().is(5))`.

**Equivalent SELECT:** N/A.

**Path:** unit-bldgA + labeled edges + depth cap.

**Tip:** Never unbounded `repeat(__.out())` in production.

---

### 3.3 — valueMap vs selective values

**Scenario:** `valueMap(true)` vs `values('name','sensor_type')` on sensors (tenant-1).

**Equivalent SELECT:** SELECT * vs SELECT name, sensor_type.

**Path:** sensor + pk.

**Tip:** Fetch only needed properties.

---

### 3.4 — Upsert with coalesce

**Scenario:** If vertex **exists**, use it; if **not**, **create** once — rerunnable loaders without duplicating `tenant-1`.

**Equivalent SELECT:** MERGE / upsert.

**Path:** `g.V('tenant-1').fold().coalesce(__.unfold(), __.addV(...))`.

**Flow (ASCII):**

```
  V('tenant-1')  →  fold()  →  list []  or  list [vertex]
                      |
           coalesce( unfold() , addV(...) )
                      |
        [] + unfold  → no traversers  →  addV runs (insert)
        [v] + unfold → emits v         →  addV skipped (already there)
```

| Step | Role |
|------|------|
| **fold()** | One traverser carrying a **list**: empty if id missing, else one vertex. |
| **unfold()** | List back to traversers; **empty list ⇒ no emission** → `coalesce` tries the next branch. |
| **coalesce(A,B)** | First branch that **produces output** wins; otherwise **B**. |
| **addV(...)** | **B** = create vertex only when lookup was empty. |

**Why `fold()`?** After `g.V(id)` you have **0 or 1 traversers**. `fold()` makes it **always one traverser** whose payload is **`[]` or `[v]`**, so `unfold()` can mean “empty vs not” in one place — `coalesce` keys off **whether branch A emitted anything**.

**Two runs:**

- **Exists:** `fold` → `[v]` → `unfold` emits `v` → `addV` skipped.  
- **Missing:** `fold` → `[]` → `unfold` emits nothing → `addV` runs.

**Python sketch:** `found = lookup(id)` as a list; `if found: use(found[0]) else: create()`.

**Tip:** Elsewhere, `fold()` inside `project().by(__....fold())` means **collect many neighbors into one list** — not the same as upsert `fold()` after `g.V(id)`.

**Tip:** Idempotent migrations.

---

### 3.5 — Safe drop

**Scenario:** Never `g.V().drop()`. Example: drop by specific id (trainer uses a **non-existent** id so seed data is not deleted).

**Equivalent SELECT:** DELETE with WHERE id = ?

**Path:** V('nonexistent-vertex-for-drop-demo').drop()

**Tip:** Always filter before drop.

---

### 3.6 — Order + limit

**Scenario:** Large sorts need `limit()` to cap RU.

**Equivalent SELECT:** ORDER BY with LIMIT.

**Path:** Conceptual — pair order with limit on large sets.

**Tip:** From Day 08 — use `incr`/`decr` in Cosmos for order direction, not `asc`/`desc`.

---

## Lab 4: Safe design patterns

### 4.1 — Partition or ID first

**Scenario:** Template: pk + label + limit.

**Equivalent SELECT:** WHERE pk AND type LIMIT n.

**Path:** Pattern 1 in trainer doc.

**Tip:** Default for tenant-scoped APIs.

---

### 4.2 — Bounded recursion

**Scenario:** `emit` + `repeat(out('contains'))` + `until(loops().is(5))`.

**Equivalent SELECT:** Recursive CTE with max depth.

**Path:** Pattern 2 in trainer.

**Tip:** Always cap depth and label edges.

---

### 4.3 — Exploration + limit

**Scenario:** `limit(20)` before heavy steps.

**Equivalent SELECT:** LIMIT early.

**Path:** Pattern 3 in trainer.

**Tip:** exploration queries always bounded.

---

### 4.4 — Upsert equipment

**Scenario:** Same pattern as §3.4: **fold** → **coalesce( unfold().property(...) , addV(...) )** — update `status` if `equip-hvac101` exists, else insert.

**Equivalent SELECT:** UPSERT.

**Path:** First branch must **emit** when vertex exists (`unfold().property(...)`), or `coalesce` would still run `addV` and duplicate.

**Mini flow:**

```
  fold → coalesce(  unfold().property('status', 'maintenance')  ,  addV('equipment')...  )
         [v] → update path emits vertex     [] → insert path
```

**Order matters:** **`unfold` (update) before `addV`** — never `addV` first.

**Tip:** Known stable **id** is required for lookup + upsert.

---

### 4.5 — Pre-computed counts

**Scenario:** Either **recompute on every read** (accurate, more RU) or **store a number on the vertex** and refresh in batch (cheaper reads, can be briefly stale).

**Equivalent SELECT:** Live: correlated subquery / join aggregate. Stored: materialized `equipment_count` column updated by ETL.

**Path:** Pattern 5 in trainer.

**Flow:**

```
  Live:     gateway → project by out('connects_to').count()   [each request]
  Stored:   job computes N → property(..., N) → read values('equipment_count')
```

| Approach | Pros | Cons |
|----------|------|------|
| **`project` + `by(__.out().count())`** | Always matches graph | Higher read cost |
| **Literal property** | Cheap property reads | Must refresh; not “free” in one Gremlin step from `select()` in Cosmos |

**Tip:** Cosmos Gremlin won’t reliably do `property(key, __.select('count'))`; compute **N** in your app/Spark and pass **N** as a literal.

**Python sketch:** live = `len(neighbors)` per row; materialized = batch `UPDATE ... SET count = computed`.

---

### 4.6 — Filtered drop

**Scenario:** Drop only decommissioned sensors in a partition.

**Equivalent SELECT:** DELETE ... WHERE status = ? AND pk = ?

**Path:** hasLabel + pk + status + drop.

**Tip:** Multiple has() before drop.

---

## Lab 5: Relational → graph mapping

### 5.1 — Table to vertex / FK to edge

**Scenario:** Map tenants, units, gateways, equipment, sensors tables to labels and `manages`, `hosts`, `connects_to`, `monitors`, `assigned_to`.

**Equivalent SELECT:** N/A — modeling exercise.

**Path:** Document vertex and edge table from trainer.

**Tip:** Two FKs on sensors → two edge types.

---

## Lab 6: Load sequence

### 6.1 — Edge without vertices fails

**Scenario:** addE between missing ids — expect failure.

**Equivalent SELECT:** FK insert without parent row.

**Path:** Demonstration query in trainer.

**Tip:** Vertices first, edges second.

---

### 6.2–6.3 — mig-tenant-1 subgraph

**Scenario:** addV tenant, unit, gateway; then edges; verify count and path.

**Equivalent SELECT:** Bulk INSERT phases.

**Path:** pk = `mig-tenant-1` for all test vertices.

**Tip:** Isolated pk for easy cleanup.

---

## Lab 7: IDs, partition key, cleanup

### 7.1–7.4 — Meaningful ids and edges

**Scenario:** add equipment and sensor under `mig-tenant-1`; edge monitors; verify count.

**Equivalent SELECT:** Natural keys from source system.

**Path:** equip-mig-001, sensor-mig-001, etc.

**Tip:** Avoid opaque ids for migrations.

---

### 7.5 — Drop partition

**Scenario:** `g.V().has('pk', 'mig-tenant-1').drop()` then count = 0.

**Equivalent SELECT:** DELETE partition slice.

**Path:** has pk + drop.

**Tip:** Confirms cleanup of migration test data.

---

## Lab 8: End-to-end migration (mig2-t1)

### 8.1 — Phases 1–3

**Scenario:** Vertices mig2-t1, mig2-u1/u2, mig2-e1; edges manages, contains; validate paths; drop pk mig2-t1.

**Equivalent SELECT:** ETL phases + validation + cleanup.

**Path:** Follow trainer Gremlin order exactly once per classroom run.

**Tip:** In production use Gremlin.NET / bulk tools; Data Explorer is for learning and small validation.

---

## Day 09 Gremlin Query Pack (All Queries)

### Lab 1 — Portal metrics demo

```gremlin
g.V('tenant-006')
```

```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').values('name', 'status')
```

### Lab 2 — Data Explorer metrics

```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006').values('name').limit(20)
```

```gremlin
g.V().hasLabel('equipment').has('pk', 'tenant-006')
  .limit(20)
  .project('name', 'sensors')
  .by('name')
  .by(__.in('monitors').values('name').fold())
```

```gremlin
g.V().hasLabel('equipment').count()
```

```gremlin
g.V().hasLabel('nonexistent')
```

### Lab 3 — Pitfalls

```gremlin
g.V().hasLabel('equipment').values('name').limit(20)
```

```gremlin
g.V().has('pk', 'tenant-006').hasLabel('equipment').values('name').limit(20)
```

```gremlin
g.V('unit-bldgA').repeat(__.out('contains')).emit().until(__.loops().is(5)).values('name')
```

```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-1').valueMap(true)
```

```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-1').values('name', 'sensor_type')
```

```gremlin
g.V('tenant-1').fold()
  .coalesce(
    __.unfold(),
    __.addV('tenant').property('id', 'tenant-1').property('name', 'Acme Corp').property('pk', 'tenant-1')
  )
```

```gremlin
g.V('nonexistent-vertex-for-drop-demo').drop()
```

### Lab 4 — Safe query patterns

```gremlin
g.V().has('pk', 'tenant-006').hasLabel('equipment').values('name').limit(20)
```

```gremlin
g.V('unit-bldgA')
  .emit()
  .repeat(__.out('contains'))
  .until(__.loops().is(5))
  .values('name')
```

```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006').limit(20).valueMap('name', 'sensor_type')
```

```gremlin
g.V('equip-hvac101').fold()
  .coalesce(
    __.unfold().property('status', 'maintenance'),
    __.addV('equipment').property('id', 'equip-hvac101').property('name', 'HVAC-101').property('status', 'maintenance').property('pk', 'tenant-1')
  )
```

```gremlin
g.V().hasLabel('gateway').has('pk', 'tenant-006').limit(5)
  .project('name', 'equipment_count')
  .by('name')
  .by(__.out('connects_to').count())
```

```gremlin
g.V().hasLabel('sensor').has('pk', 'tenant-006').has('status', 'decommissioned').drop()
```

### Lab 6 — Load sequence

```gremlin
g.V('nonexistent-id').addE('manages').to(g.V('also-nonexistent'))
```

```gremlin
g.addV('tenant').property('id', 'mig-tenant-1').property('name', 'MigrationTest Corp').property('industry', 'logistics').property('pk', 'mig-tenant-1')
```

```gremlin
g.addV('unit').property('id', 'mig-unit-1').property('name', 'Warehouse-A').property('type', 'warehouse').property('location', 'Dallas').property('pk', 'mig-tenant-1')
```

```gremlin
g.addV('gateway').property('id', 'mig-gw-1').property('name', 'MIG-GW-001').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.3.1').property('pk', 'mig-tenant-1')
```

```gremlin
g.V('mig-tenant-1').addE('manages').to(g.V('mig-unit-1')).property('since', '2025-01-01')
```

```gremlin
g.V('mig-unit-1').addE('hosts').to(g.V('mig-gw-1')).property('installed_date', '2025-02-15')
```

```gremlin
g.V().has('pk', 'mig-tenant-1').count()
```

```gremlin
g.V('mig-tenant-1').out('manages').out('hosts').values('name')
```

### Lab 7 — IDs, partition key, cleanup

```gremlin
g.addV('equipment').property('id', 'equip-mig-001').property('name', 'Conveyor-101').property('type', 'conveyor').property('pk', 'mig-tenant-1')
```

```gremlin
g.addV('sensor').property('id', 'sensor-mig-001').property('name', 'MIG-TEMP-001').property('sensor_type', 'temperature').property('pk', 'mig-tenant-1')
```

```gremlin
g.V('sensor-mig-001').addE('monitors').to(g.V('equip-mig-001')).property('attached_date', '2025-03-01')
```

```gremlin
g.V().has('pk', 'mig-tenant-1').count()
```

```gremlin
g.V().has('pk', 'mig-tenant-1').drop()
```

```gremlin
g.V().has('pk', 'mig-tenant-1').count()
```

### Lab 8 — End-to-end migration (mig2-t1)

```gremlin
g.addV('tenant').property('id', 'mig2-t1').property('name', 'LogiFlow Inc').property('industry', 'logistics').property('pk', 'mig2-t1')
```

```gremlin
g.addV('unit').property('id', 'mig2-u1').property('name', 'Distribution-Center-1').property('type', 'warehouse').property('location', 'Houston').property('pk', 'mig2-t1')
```

```gremlin
g.addV('unit').property('id', 'mig2-u2').property('name', 'Loading-Dock-A').property('type', 'dock').property('location', 'Houston').property('pk', 'mig2-t1')
```

```gremlin
g.addV('equipment').property('id', 'mig2-e1').property('name', 'Forklift-01').property('type', 'forklift').property('manufacturer', 'Toyota').property('status', 'running').property('pk', 'mig2-t1')
```

```gremlin
g.V('mig2-t1').addE('manages').to(g.V('mig2-u1')).property('since', '2024-06-01')
```

```gremlin
g.V('mig2-t1').addE('manages').to(g.V('mig2-u2')).property('since', '2024-06-01')
```

```gremlin
g.V('mig2-u1').addE('contains').to(g.V('mig2-e1'))
```

```gremlin
g.V('mig2-t1').out('manages').values('name')
```

```gremlin
g.V('mig2-t1').out('manages').out('contains').values('name')
```

```gremlin
g.V().has('pk', 'mig2-t1').drop()
```
