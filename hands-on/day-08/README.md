# Day 08 — Query Optimization & Performance (Part 2)

**Graph reminder:** Same IoT asset graph: `tenant`, `unit`, `gateway`, `equipment`, `sensor`; edges `manages`, `contains`, `hosts`, `connects_to`, `monitors`, `assigned_to`. Partition key `pk`. **Seed graph:** `unit-bldgA` (tenant-1) from 3-GremlinSeed. **Performance tenant:** `tenant-006` (~750 equipment, ~3,750 sensors per tenant). Prerequisite: Day 07; performance dataset loaded.

---

## Lab 1: Reduce Projection Cost

### 1.1 — Full properties (expensive)

**Scenario:** Compare RU when fetching all properties vs selective fields for equipment in tenant-006.

**Equivalent SELECT:**
```sql
SELECT * FROM equipment WHERE pk = 'tenant-006';  /* all columns — heavy */
```
**Path:** equipment + pk → valueMap(true).

**Tip:** Don't use valueMap(true) for list UIs. Do fetch only columns you need.

---

### 1.2 — Two properties only

**Scenario:** Same set of vertices; only `name` and `status`.

**Equivalent SELECT:**
```sql
SELECT name, status FROM equipment WHERE pk = 'tenant-006';
```
**Path:** equipment + pk → values('name', 'status').

**Tip:** values('a','b') is cheaper than full vertex materialization.

---

### 1.3 — Structured project

**Scenario:** Same data as 1.2 as named fields in a map.

**Equivalent SELECT:**
```sql
SELECT name, status FROM equipment WHERE pk = 'tenant-006';  /* shape differs in Gremlin — project gives maps */
```
**Path:** project('name','status').by('name').by('status').

**Tip:** project() is good for APIs; still limit properties to what you need.

---

### 1.4 — RU comparison table

**Scenario:** Record RU for 1.1–1.3 in the lab table. valueMap(true) should be highest.

**Equivalent SELECT:** N/A — measurement exercise.

**Path:** Run each query once; note Request Charge.

**Tip:** Every extra property and nested structure adds RU.

---

### 1.5 — Projection with sub-traversals (capped)

**Scenario:** For up to 20 equipment, project name + sensor names + gateway names. Sub-traversals run per vertex — cost scales with row count.

**Equivalent SELECT:**
```sql
SELECT e.name,
       (SELECT ARRAY_AGG(s.name) FROM monitors m JOIN sensor s ... WHERE m.equipment_id = e.id) AS sensors,
       (SELECT ARRAY_AGG(g.name) FROM connects_to ct JOIN gateway g ... WHERE ct.equipment_id = e.id) AS gateway
FROM equipment e WHERE e.pk = 'tenant-006' LIMIT 20;
```
**Path:** limit(20) before project; in('monitors') / in('connects_to') with fold.

**Tip:** Do put limit **before** project when sub-traversals are heavy. Don't project all 750 then limit.

---

## Lab 2: Mitigate Deep Traversals

### How `repeat`, `emit`, and `loops()` actually run (read this first)

Gremlin does **not** execute like a single `for` loop in one thread. Think of **many parallel “walkers”** (traversers): each sits on one vertex; each step can **split** a walker into several walkers if there are several outgoing edges. The patterns below control **how deep** those walks go and **whether you keep** vertices along the way.

---

**1) `repeat(__.out('contains')).times(3)` — fixed depth, no `emit`**

The inner traversal `out('contains')` is applied **exactly 3 times in a row** along each path (like chaining three moves). You typically only see **vertices reached after the 3rd hop**, not the start or middle — unless you add `emit()`.

```
                    times(3) = apply "out('contains')" three times in sequence
                    -----------------------------------------------------------

  Step 0 (start)          Step 1               Step 2               Step 3
  -------------          -------              -------              -------
  [unit-bldgA]  --contains-->  [floor?]  --contains-->  [room?]  --contains-->  [child?]
       ^                                                                            |
       |                                                                            v
   NOT emitted                                                                  THESE are the
   in the result                                                                main results
   (no emit())                                                                  (names, etc.)
```

---

**2) `emit().repeat(__.out('contains')).until(__.loops().is(4))` — yield along the way + stop by loop count**

- **`emit()`** (placed **before** `repeat`): **output** the current vertex **before** you enter the repeat, and (with the default repeat configuration) also **output vertices as you walk** — like a **generator** that **`yield`s every place you visit**, not only the last one. Without `emit()`, you would only care about the “end of the walk” for each path.

- **`loops()`**: inside `repeat`, Gremlin tracks **how many times** the repeat body has been applied for **that path** (a **loop counter**, like `for i in range(...)` or a `while` with `depth += 1`). **`until(__.loops().is(4))`** means: **stop repeating when the loop counter reaches 4** (so you do **not** walk forever).

```
  emit()  = "output current vertex"     repeat  = "go out('contains')"     until(loops==4) = "stop depth"
  ---------------------------------------------------------------------------------------------------

  loops=0        loops=1           loops=2           loops=3           loops=4 → STOP (no 5th repeat)
     |               |                 |                 |                 |
  [bldgA] -----> [floor1] --------> [room?] --------> ... --------> (exit repeat)
     ^               ^                 ^
  EMIT            EMIT              EMIT           ...    (start + intermediates appear in results)
```

---

**3) Putting it together: `out('manages')` then `emit().repeat(...).until(...)` (Lab 2.4)**

Flow:

1. One step **outside** the repeat: `out('manages')` — move from tenant to managed units (no repeat yet).
2. **`emit()`** — those unit vertices (and later descendants) can be **emitted** into the result stream as the repeat runs.
3. **`repeat(__.out('contains'))`** — walk **only** the `contains` hierarchy (not `hosts`, etc.).
4. **`until(__.loops().is(5))`** — cap **how many repeat iterations** per path (tune `5` to your model).
5. **`limit(10)`** — after all that, **truncate** to 10 results (saves RU / response size).

**Tip:** **`times(n)`** = “exactly n applications of the inner step.” **`emit() + until(loops()...)`** = “walk until a **depth counter** says stop, **reporting** vertices along the way.” Use **labeled edges** inside `repeat` (`out('contains')`), never bare `out()` unless you really want **every** edge type at every hop.

---

### 2.1 — Unbounded repeat (trainer demo only)

**Scenario:** Show why `repeat(__.out())` without a safe bound is dangerous (can time out or huge RU). **Do not** have students run the unbounded variant in Data Explorer.

**Equivalent SELECT:** N/A — anti-pattern.

**Path:** Trainer explains; optional bounded demo: repeat with times(3) or until(loops().is(N)) on `contains` only.

**Tip:** Don't use repeat(__.out()) without times() or until(loops()). Do always restrict edge labels (e.g. out('contains')) for hierarchy.

---

### 2.2 — Bounded repeat with times()

**Scenario:** From `unit-bldgA`, walk up to 3 hops along `contains` only.

**Equivalent SELECT:** Recursive CTE with depth ≤ 3 on parent/child edges.

**Path:** V('unit-bldgA') → repeat(out('contains')).times(3) → values('name').

**Tip:** times(N) caps depth explicitly.

---

### 2.3 — Bounded repeat with emit and loops()

**Scenario:** Emit intermediate nodes; stop when `loops()` reaches 4. See **“How repeat, emit, and loops() actually run”** above for ASCII

**Equivalent SELECT:** N/A — Gremlin repeat/emit pattern.

**Path:** emit() → repeat(out('contains')) → until(loops().is(4)).

**Tip:** `emit()` is what puts **start + intermediates** in the result; `until(loops().is(4))` caps **how many repeat iterations** run.

---

### 2.4 — Limit after deep traversal

**Scenario:** From tenant-1, repeat contains with cap, then limit(10) names.

**Equivalent SELECT:** Hierarchy walk with LIMIT 10 on final names.

**Path:** V('tenant-1') → out('manages') → emit/repeat contains → limit(10) → values('name').

**Tip:** limit() after repeat caps how many results you return.

---

### 2.5 — Edge label in repeat (compare counts)

**Scenario:** Same depth, two queries: `out()` vs `out('contains')` — unlabeled out() follows every edge type (hosts, contains, …) and explodes RU.

**Equivalent SELECT:** N/A — compare COUNT for two traversals.

**Path:** repeat(out()).times(2).count() vs repeat(out('contains')).times(2).count() from unit-bldgA.

**Tip:** Do specify edge labels inside repeat(). Don't use bare out() in repeat unless you intend all edge types.

---

## Lab 3: Handle Edge Explosion

### 3.1 — Fan-out counts per hop

**Scenario:** From tenant-006, count after manages, hosts, connects_to, in('monitors') to see growth 10 → 150 → 750 → ~3750.

**Equivalent SELECT:** COUNT(*) at each JOIN stage.

**Path:** V('tenant-006') → out chain → count at each step.

**Tip:** Plan limits before explosive hops in production queries.

---

### 3.2 — Limit at intermediate hops

**Scenario:** Cap buildings, gateways, equipment per hop to keep result small.

**Equivalent SELECT:** Subquery with LIMIT at each level.

**Path:** out('manages').limit(2) → out('hosts').limit(4) → out('connects_to').limit(8) → values('name').

**Tip:** Intermediate limit() is a sampling pattern — good for exploration, not exact analytics.

---

### 3.3 — Pagination with range()

**Scenario:** Equipment names sorted by name; pages of 10 (0–9, then 10–19).

**Equivalent SELECT:**
```sql
SELECT name FROM equipment WHERE pk = 'tenant-006' ORDER BY name OFFSET 0 FETCH NEXT 10 ROWS ONLY;
```
**Path:** hasLabel + pk → order().by('name', incr) → range(0,10) / range(10,20) → values('name'). (Cosmos Gremlin: use `incr`/`decr`, not `asc`/`desc`.)

**Tip:** order() is a barrier — sorts all matching vertices in partition before range; still better than returning thousands to the client.

---

### 3.4 — dedup after many-to-many

**Scenario:** Same path to sensors via equipment; dedup() removes duplicate sensor vertices.

**Equivalent SELECT:** SELECT DISTINCT ...

**Path:** ... out('connects_to').in('monitors').dedup().count().

**Tip:** Use dedup() when multiple paths reach the same vertex.

---

## Lab 4: Order and Limit Tuning

### 4.1 — Order all equipment by name

**Scenario:** Sort ~750 equipment names in tenant-006 — note RU.

**Equivalent SELECT:** ORDER BY name on full partition slice.

**Path:** equipment + pk → order().by('name', incr) → values('name').

**Tip:** If you only need “first page”, prefer order + range or limit early where semantics allow.

---

### 4.2 — Order by computed count (50 vertices)

**Scenario:** Among 50 equipment, order by sensor count descending, then project.

**Equivalent SELECT:** ORDER BY (SELECT COUNT(*) ...) with LIMIT 50 first in a subquery pattern.

**Path:** limit(50) → order().by(__.in('monitors').count(), decr) → project.

**Tip:** limit(50) caps how many sub-traversals run for the sort key.

---

### 4.3 — limit before order vs order before limit

**Scenario:** Two queries on sensors — different semantics (any 5 sorted vs top 5 globally sorted).

**Equivalent SELECT:** LIMIT before ORDER BY vs ORDER BY then LIMIT.

**Path:** limit(5) → order() vs order() → limit(5).

**Tip:** First is cheaper; second is correct for global top-5 by name.

---

### 4.4 — Top-N by sensor count (sample of 50)

**Scenario:** Take 50 equipment, sort by in('monitors').count(), take top 5, then project name + count.

**Equivalent SELECT:** Subquery with ROW_NUMBER or ORDER BY count LIMIT 5 over a sample.

**Path:** limit(50) → order().by(__.in('monitors').count(), decr) → limit(5) → project.

**Tip:** Order on **vertices** before project; avoid order().by(select('sensor_count')) on maps in Cosmos Gremlin.

---

### 4.5 — tail(3) after sort

**Scenario:** Last 3 names in ascending name order (bottom of sorted list).

**Equivalent SELECT:** ORDER BY name DESC LIMIT 3 or tail window.

**Path:** order().by('name', incr) → tail(3) → values('name').

**Tip:** tail(n) after order gives bottom-N of that ordering.
