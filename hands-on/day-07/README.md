# Day 07 — Query Optimization & Performance (Part 1)

**Graph reminder:** Vertices — `tenant`, `unit`, `gateway`, `equipment`, `sensor`. Edges — tenant `manages` unit; unit `hosts` gateway; gateway `connects_to` equipment; sensor `monitors` equipment; sensor `assigned_to` gateway; unit `contains` unit (hierarchy). All vertices have `pk` (partition key). At scale: ~240K vertices, ~431K edges across 55 tenants.

---

## Lab 1: Verify Performance Dataset and Explore Scale

### 1.1 — Total vertex count

**Scenario:** Confirm total number of vertices in the graph. Run once; note RU. Expect ~240,000.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices;  /* no partition filter — all 55 partitions */
```
**Path:** All vertices → count.

**Tip:** Don't run full-graph counts in production. Do use only once for baseline; prefer per-partition counts for metrics.

---

### 1.2 — Total edge count

**Scenario:** Confirm total number of edges. Run once; note RU. Expect ~431,000.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM edges;  /* cross-partition */
```
**Path:** All edges → count.

**Tip:** Same as 1.1 — don't repeat; no partition filter = high cost.

---

### 1.3 — Per-label distribution

**Scenario:** Count vertices by label (tenant, unit, gateway, equipment, sensor). One map of label → count.

**Equivalent SELECT:**
```sql
SELECT label, COUNT(*) FROM vertices GROUP BY label;  /* no partition filter */
```
**Path:** All vertices → groupCount by label.

**Tip:** Don't use cross-partition groupCount for per-tenant dashboards. Do scope by pk when you need one tenant's distribution.

---

### 1.4 — Per-tenant vertex count (performance tenant)

**Scenario:** How many vertices in tenant-006? Single-partition count. Expect ~4,711. Note lower RU than 1.1.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices WHERE pk = 'tenant-006';
```
**Path:** Vertices with pk=tenant-006 → count.

**Solution:** Always add `has('pk', 'tenant-006')` first for tenant-scoped counts — one partition = much lower RU (10–25×).

---

### 1.5 — Per-tenant vertex count (seed tenant)

**Scenario:** How many vertices in **seed tenant** tenant-1? (Seed tenant = original smaller dataset from Days 01–06.) Compare with 1.4 (performance tenant). Expect ~1,046.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices WHERE pk = 'tenant-1';
```
**Path:** Vertices with pk=tenant-1 → count.

**Tip:** Do use the same single-partition pattern for any tenant. Don't drop the pk filter when comparing tenants.

---

### 1.6 — Approximate count and caching (reduce stress on the server)

**Scenario:** You need counts for dashboards or APIs without running expensive Gremlin count queries on every request. Cosmos DB Gremlin has **no built-in approximate count or server-side count cache** — every count runs the full query and consumes RU.

**Equivalent SELECT:**
```sql
/* No native "approximate count" in Cosmos DB Gremlin.
   Option 1: Partition-scoped count (cheap) — SELECT COUNT(*) FROM vertices WHERE pk = 'tenant-006';
   Option 2: Cache full-graph count in app (Redis/memory with TTL); refresh every 5–15 min or on schedule. */
```
**Path:** (1) Use partition-scoped count when possible. (2) Cache expensive counts in application layer; refresh periodically; serve cached value.

**Solution:** **Do** use partition-scoped counts (`has('pk', ...).count()`) when you can. **Do** cache full-graph or cross-partition counts in your app (e.g. Redis, in-memory with TTL) and refresh on a schedule — treat as "approximate" until next refresh. **Don't** call `g.V().count()` or similar from high-frequency or user-facing paths. **Don't** expect server-side approximate count or count cache; reduce stress by caching and scoping by pk.

**When to refresh the cached count:** To avoid rerunning the count for small updates, you can refresh only when data has changed *significantly*.

- **Change Feed (Cosmos DB supports it for graph):** Use the Change Feed Processor in a background job. It notifies you of inserts/updates. Count changes (or set a "dirty" flag); when the number of changes exceeds your threshold (e.g. 10+), run the full count and refresh the cache; otherwise keep serving the previous cached count. No Gremlin count needed to answer "has anything changed?"
- **Lightweight _ts check (per partition):** Store the time of the last count. Periodically run a cheap query: "any vertex in this partition with _ts >= lastCountTime?" using `ProjectionStrategy` to include `_ts`, with `limit(1)`. If yes, refresh (or count changes and refresh only if over threshold); if no, keep cached count. Use when you prefer polling per tenant instead of change feed.

---

## Lab 2: Measure RU Consumption Patterns

### 2.1 — Point lookup (cheapest)

**Scenario:** Fetch one vertex by ID (tenant-1). Note RU — baseline for cheapest pattern.

**Equivalent SELECT:**
```sql
SELECT * FROM vertices WHERE id = 'tenant-1';  /* single partition, direct read */
```
**Path:** One vertex by ID; no traversal.

**Tip:** Do use `g.V('id')` when you have the ID — cheapest. Don't use property scan when you already have the ID.

---

### 2.2 — Label scan (limit 100)

**Scenario:** Get up to 100 equipment vertices (no partition filter). Note RU.

**Equivalent SELECT:**
```sql
SELECT * FROM vertices WHERE label = 'equipment' LIMIT 100;  /* no pk — cross-partition */
```
**Path:** All partitions, filter by label, limit 100.

**Tip:** Don't omit partition key if use case is tenant-scoped. Do add `has('pk', ...)` before hasLabel for 10–25× lower RU.

---

### 2.3 — Property filter scan (limit 100)

**Scenario:** Get up to 100 equipment with type=hvac. Note RU.

**Equivalent SELECT:**
```sql
SELECT * FROM vertices WHERE label = 'equipment' AND type = 'hvac' LIMIT 100;  /* no pk */
```
**Path:** Scan equipment by label and type; limit 100.

**Tip:** Do add `has('pk', ...)` when you know the tenant. Don't run repeated cross-partition scans in hot paths.

---

### 2.4 — Multi-hop traversal

**Scenario:** From tenant-1, go manages → hosts → connects_to and get equipment names. Partition known from start.

**Equivalent SELECT:**
```sql
/* JOINs from tenant to equipment, scoped by tenant partition */
SELECT e.name FROM tenant t
JOIN manages m ON m.tenant_id = t.id
JOIN hosts h ON h.unit_id = m.unit_id
JOIN connects_to ct ON ct.gateway_id = h.gateway_id
JOIN equipment e ON e.id = ct.equipment_id
WHERE t.id = 'tenant-1';
```
**Path:** tenant-1 → manages → hosts → connects_to → values('name').

**Tip:** Do start from a known vertex ID so the whole traversal stays in one partition. Don't start with g.V().hasLabel(...) and then traverse.

---

### 2.5 — Full graph scan (trainer demo only)

**Scenario:** Count all vertices again — most expensive pattern. Trainer runs once.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices;  /* no partition filter — full scan */
```
**Path:** All vertices → count.

**Do's and Don'ts:** Don't run full graph count in production. Do use only once in a demo; use single-partition for real dashboards.

---

## Lab 3: Partition-Aware Query Patterns

### 3.1 — Cross-partition count (trainer demo only)

**Scenario:** Count equipment with type=hvac across entire graph (no pk). High RU; scans all 55 partitions.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices WHERE label = 'equipment' AND type = 'hvac';  /* no WHERE pk */
```
**Path:** All partitions → filter equipment, type=hvac → count.

**Solution:** Add `has('pk', 'tenant-006')` before other filters. Use single-partition version in production — 10–20× lower RU.

---

### 3.2 — Single-partition count

**Scenario:** Same count but only for tenant-006 (with pk filter). Much lower RU than 3.1.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM vertices WHERE pk = 'tenant-006' AND label = 'equipment' AND type = 'hvac';
```
**Path:** Partition tenant-006 → equipment, type=hvac → count.

**Tip:** Do put `has('pk', ...)` first in the step chain. Don't put label or property before pk when you know the partition.

---

### 3.3 — Multi-hop with pk (single partition)

**Scenario:** Sensors in tenant-006 → equipment names, limit 20. Single partition.

**Equivalent SELECT:**
```sql
SELECT e.name FROM sensor s
JOIN monitors m ON m.sensor_id = s.id JOIN equipment e ON e.id = m.equipment_id
WHERE s.pk = 'tenant-006' LIMIT 20;
```
**Path:** Sensors in tenant-006 → out('monitors') → values('name') → limit 20.

**Tip:** Do include partition key at the start of tenant-scoped traversals. Don't omit pk because you "only need 20 results" — you still pay for scanning all partitions.

---

### 3.4 — Multi-hop without pk (cross-partition)

**Scenario:** Same as 3.3 but without pk filter. Compare RU with 3.3 — much higher.

**Equivalent SELECT:**
```sql
SELECT e.name FROM sensor s
JOIN monitors m ON m.sensor_id = s.id JOIN equipment e ON e.id = m.equipment_id
LIMIT 20;  /* no partition filter */
```
**Path:** All sensors → out('monitors') → values('name') → limit 20.

**Solution:** Add `has('pk', 'tenant-006')` as the first step. Don't leave out pk when the request is tenant-scoped.

---

### 3.5 — Start from vertex ID

**Scenario:** From tenant-006 (by ID), traverse manages → hosts; count gateways. Partition from ID; cheap.

**Equivalent SELECT:**
```sql
SELECT COUNT(*) FROM hosts h JOIN manages m ON m.unit_id = h.unit_id WHERE m.tenant_id = 'tenant-006';
```
**Path:** V(tenant-006) → manages → hosts → count.

**Tip:** Do use `g.V('id')` when you have the ID — partition is derived from ID. Don't use property lookup when ID is available.

---

## Lab 4: Early Filtering Strategies

### 4.1 — Multi-hop with filter at equipment

**Scenario:** From tenant-1, get names of equipment that are type=hvac. Filter at equipment step.

**Equivalent SELECT:**
```sql
SELECT e.name FROM tenant t
JOIN manages m ON m.tenant_id = t.id JOIN hosts h ON h.unit_id = m.unit_id
JOIN connects_to ct ON ct.gateway_id = h.gateway_id JOIN equipment e ON e.id = ct.equipment_id
WHERE t.id = 'tenant-1' AND e.type = 'hvac';
```
**Path:** tenant-1 → manages → hosts → connects_to → has(type,hvac) → values('name').

**Tip:** Do apply filters in Gremlin as soon as the vertex is available. Don't fetch more and filter in application code.

---

### 4.2 — Start by ID vs by property

**Scenario:** (A) Start at tenant-006 by ID, get 10 gateway names. (B) Find tenant by name 'NexGen Industries', then same traversal. (A) cheaper.

**Equivalent SELECT:**
```sql
-- (A) WHERE id = 'tenant-006'  (primary key — direct lookup)
-- (B) WHERE label = 'tenant' AND name = 'NexGen Industries'  (scan)
```
**Path:** (A) V(tenant-006) → manages → hosts → values('name'). (B) V by name → same.

**Solution:** Do start with `g.V('tenant-006')` when you have the ID. Don't use hasLabel + has name when you could use the ID — property scan is 10×+ more expensive.

---

### 4.3 — Limit early vs limit late

**Scenario:** (A) Sensors in tenant-006, limit 10 then get names. (B) Same but get all names then limit 10. Compare RU.

**Equivalent SELECT:**
```sql
-- (A) SELECT name FROM ... WHERE pk = '...' LIMIT 10  (limit pushdown — less work)
-- (B) SELECT name FROM ... WHERE pk = '...' ; then take 10  (more work, then truncate)
```
**Path:** (A) pk+label → limit(10) → values('name'). (B) pk+label → values('name') → limit(10).

**Solution:** Do put `limit(N)` before `values('name')` so the engine stops after N vertices. Don't put limit after values when you want to minimize RU.

---

### 4.4 — has(pk) on first step

**Scenario:** Equipment in tenant-006, first 10 names. Partition key filter first — single-partition routing.

**Equivalent SELECT:**
```sql
SELECT name FROM vertices WHERE pk = 'tenant-006' AND label = 'equipment' LIMIT 10;
```
**Path:** has(pk, tenant-006) → hasLabel(equipment) → values('name') → limit(10).

**Tip:** Do put `has('pk', ...)` first in single-tenant queries. Don't put hasLabel or other predicates before pk. Standard order: pk → label → other filters → limit → values.
