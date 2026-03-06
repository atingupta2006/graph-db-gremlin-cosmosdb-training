# Day 03 — Traversal Strategies & Evaluation Model

**Duration**: 3 Hours
**Prerequisite**: Day 01–02 completed and seed data available

Today’s objective is to understand how Gremlin queries are executed inside Azure Cosmos DB — not just how to write them.

Performance depends on:

• Traversers
• Expansion (hops)
• Reduction (filters / scope)
• Barriers
• Partition targeting
• Payload size

---

# 1. What is a Traverser?

A traverser is the execution token moving through the graph.

Each traverser represents one possible path of execution.

Example:

Assume total vertices = 12

```
g.V()
```

```
Start
 ↓
12 Traversers
```

Now filter:

```
g.V().hasLabel('equipment')
```

Assume only 5 are equipment

```
12 Traversers
 ↓ hasLabel()
5 Traversers Continue
```

Filtering reduces traversers.

---

# 2. Traverser Expansion (Hop Multiplication)

Hops increase traversers.

```
g.V('tenant-1')
```

```
1 Traverser
```

```
out('manages')
```

Tenant manages 3 units

```
1 → 3 Traversers
```

```
out('hosts')
```

Each unit hosts 2 gateways

```
3 → 6 Traversers
```

```
out('connects_to')
```

Each gateway connects to 3 equipment

```
6 → 18 Traversers
```

ASCII:

```
Tenant
 ↓
Units (3)
 ↓
Gateways (6)
 ↓
Equipment (18)
```

Each hop multiplies work.

---

# Lab 1 — Observe Expansion

Run step by step:

```
g.V('tenant-1').out('manages')
```

Expected Output:

```
Unit-A
Unit-B
Unit-C
```

---

```
g.V('tenant-1').out('manages').out('hosts')
```

Expected Output:

```
Gateway-1
Gateway-2
Gateway-3
...
```

---

```
g.V('tenant-1').out('manages').out('hosts').out('connects_to')
```

Expected Output:

```
AHU-1
CHILLER-1
PUMP-1
...
```

Observe growth.

---

# 3. Streaming Execution

Streaming steps process traversers one-by-one.

```
g.V().hasLabel('sensor').values('name')
```

Expected Output:

```
"Temp-1"
"Temp-2"
"Humidity-1"
```

No waiting.

---

# 4. Barrier Steps

Barrier steps pause streaming and collect all traversers.

Barrier = extra RU

Common barriers:

• count()
• order()
• dedup()
• fold()

---

# 5. Fold and Unfold

## Streaming

```
g.V().hasLabel('equipment').values('name')
```

Expected Output:

```
"AHU-1"
"CHILLER-1"
"PUMP-1"
```

---

## Fold (Barrier)

```
g.V().hasLabel('equipment').values('name').fold()
```

Expected Output:

```
["AHU-1", "CHILLER-1", "PUMP-1"]
```

Stream becomes collection.

---

## Unfold

```
g.V().hasLabel('equipment').values('name').fold().unfold()
```

Expected Output:

```
"AHU-1"
"CHILLER-1"
"PUMP-1"
```

Collection becomes stream again.

---

# Lab 2 — Fold Behavior

Run all three queries above and observe:

Streaming → Collection → Streaming

---

# 6. Order Barrier

```
g.V().hasLabel('equipment').order().by('name')
```

Sorting requires full dataset.

---

# Lab 3 — Order Placement

Run:

```
g.V().hasLabel('equipment').limit(3).order().by('name')
```

Expected:

Only 3 sorted

---

Run:

```
g.V().hasLabel('equipment').order().by('name').limit(3)
```

Expected:

Full sort first → then limit

Compare RU.

---

# 7. Payload Size Impact

Run:

```
g.V().hasLabel('equipment').valueMap(true)
```

Expected:
Full object

---

Run:

```
g.V().hasLabel('equipment').values('name')
```

Expected:
Only names

---

# Lab 4 — Payload Comparison

Observe RU difference.

---

# 8. Partition Targeting

Without pk:

```
g.V().hasLabel('equipment').count()
```

With pk:

```
g.V().has('pk','tenant-1').hasLabel('equipment').count()
```

Check Request Charge below results panel.

---

# 9. Reduction Without Business Filtering

Instead of filtering HVAC, reduce structurally.

Run:

```
g.V('tenant-1')
 .out('manages')
 .out('hosts')
 .out('connects_to')
 .values('name')
```

---

Now reduce by label:

```
g.V('tenant-1')
 .out('manages')
 .out('hosts')
 .out('connects_to')
 .hasLabel('equipment')
 .values('name')
```

Same goal → less noise.

---

# Lab 5 — Reduction Timing

Run both above queries and compare RU.

---

# 10. Visualizing Traverser Flow using path()

Normally:

```
g.V('tenant-1').out('manages')
```

Output:

```
Unit-A
Unit-B
```

---

With path():

```
g.V('tenant-1').out('manages').path()
```

Expected:

```
[tenant-1, Unit-A]
[tenant-1, Unit-B]
```

---

Next:

```
g.V('tenant-1').out('manages').out('hosts').path()
```

Expected:

```
[tenant-1, Unit-A, Gateway-1]
...
```

Shows expansion visually.

---

# Lab 6 — Path Observation

Run:

```
g.V('tenant-1')
 .out('manages')
 .out('hosts')
 .out('connects_to')
 .path()
```

Then reduce:

```
.hasLabel('equipment')
```

Observe fewer paths.

---

# 11. Barrier Stacking

Run:

```
g.V().values('name').fold().count()
```

Fold + Count = stacked barrier

---

# 12. Combined Optimization

```
g.V().has('pk','tenant-1')
 .out('manages')
 .out('hosts')
 .out('connects_to')
 .hasLabel('equipment')
 .values('name')
```
