# Day 01 — Introduction to Graph Databases & Connected Data

---

## 1. What is a Graph Database

A graph database stores data as **vertices** and **edges**. Both carry **properties** (key-value pairs).

```
  vertex = an entity          (a building, a sensor, a company)
  edge   = a relationship     (manages, monitors, connects_to)
  property = metadata         (name: "Building-A", status: "active")
  label  = category/type      (tenant, sensor, equipment)
```

### The Property Graph Model

```
 +----------------------------+                          +----------------------------+
 |  VERTEX                    |                          |  VERTEX                    |
 |  label: tenant             |      EDGE                |  label: unit               |
 |                            |      label: manages      |                            |
 |  id:       tenant-1        |      since: 2024-01-15   |  id:       unit-bldgA      |
 |  name:     Acme Corp       | -----------------------> |  name:     Building-A      |
 |  industry: manufacturing   |                          |  type:     building        |
 |  pk:       tenant-1        |                          |  location: Chicago         |
 |                            |                          |  pk:       tenant-1        |
 +----------------------------+                          +----------------------------+
    Two properties on the                                   Four properties on the
    vertex (name, industry)       One property on           vertex (name, type,
    plus id, label, pk            the edge (since)          location) plus id, label, pk
```

**Key rules**:

- Every vertex has exactly **one label** (e.g., `tenant`, `sensor`)
- Every edge has exactly **one label** (e.g., `manages`, `monitors`)
- Edges are **directed** — they go from a source vertex to a target vertex
- Both vertices and edges can have **multiple properties**

### Why Direct Pointers Matter

Graph databases store a **direct pointer** from each vertex to its neighbors. This makes traversing relationships fast.

```
  RELATIONAL DATABASE                      GRAPH DATABASE

  +--------+   index     +--------+       +--------+  pointer  +--------+
  | Row A  | --> scan --> | Row B  |       |Vertex A| --------> |Vertex B|
  +--------+             +--------+       +--------+           +--------+
       |                      |                |                     |
       v                      v                v                     v
  Look up index          Look up index    Follow the            Already at
  for table B            for table C      pointer               the neighbor
       |                      |                |
       v                      v                v
  JOIN result            JOIN result       Next vertex
  (gets slower as        (gets slower)     (same speed
   tables grow)                             every time)
```

- Each hop in a graph is a direct pointer follow — **constant cost**
- In relational databases, each hop is a JOIN that gets more expensive as data grows

---

## 2. Why Graph Databases for Connected Data

When the core question is **how things connect**, a graph database is the right tool.

### Relational vs Graph — Side by Side

```
  RELATIONAL: "Which sensors are connected to Tenant-1's gateways?"

  tenants        units          gateways       equipment      sensors
  +------+      +------+       +------+       +------+       +------+
  |      | JOIN |      | JOIN  |      | JOIN  |      | JOIN  |      |
  |  t1 ------>   u1  ------>    g1  ------>    e1  <------   s1   |
  |      |      |      |       |      |       |      |       |      |
  +------+      +------+       +------+       +------+       +------+

     4 JOINs = cost grows with each table's size


  GRAPH: Same question, same data

  (tenant-1)--manages-->(unit-bldgA)--hosts-->(gw-001)--connects_to-->(equip-hvac101)<--monitors--(sensor-temp001)

     4 hops = constant cost per hop regardless of graph size
```

### When to Use What

```
  +-----------------------------------+-----------------------------------+
  |     USE A GRAPH DATABASE          |     USE A RELATIONAL DATABASE     |
  +-----------------------------------+-----------------------------------+
  |                                   |                                   |
  |  Queries about connections        |  Simple tabular data              |
  |  (who connects to whom?)          |  (list all orders)                |
  |                                   |                                   |
  |  More than 2 levels of            |  Fixed reports with known         |
  |  relationships                    |  JOIN patterns                    |
  |                                   |                                   |
  |  Variable-depth traversals        |  Single-table CRUD operations     |
  |  (find all descendants)           |  (create, read, update, delete)   |
  |                                   |                                   |
  |  Impact analysis                  |  Financial transactions with      |
  |  (if X fails, what breaks?)       |  strict schema requirements       |
  |                                   |                                   |
  |  Many-to-many relationships       |  Heavy aggregation / reporting    |
  |  with shared entities             |                                   |
  +-----------------------------------+-----------------------------------+
```

---

## 3. Graph vs Relational vs Document — Quick Comparison

```
  +----------------------+----------------------+----------------------+
  |   RELATIONAL (SQL)   |  DOCUMENT (NoSQL)    |   GRAPH (Gremlin)    |
  +----------------------+----------------------+----------------------+
  |                      |                      |                      |
  |  Data in tables      |  Data in JSON        |  Data in vertices    |
  |  and rows            |  documents           |  and edges           |
  |                      |                      |                      |
  |  Schema defined      |  Schema flexible     |  Schema optional     |
  |  upfront (rigid)     |  (schema-on-read)    |  (add as you go)     |
  |                      |                      |                      |
  |  Relationships via   |  Relationships via   |  Relationships are   |
  |  foreign keys +      |  embedding or ID     |  first-class edges   |
  |  JOINs               |  references          |  with properties     |
  |                      |                      |                      |
  |  Performance drops   |  Multi-hop queries   |  Constant cost per   |
  |  as JOINs increase   |  need app logic      |  hop (scales well)   |
  |                      |                      |                      |
  |  Best for:           |  Best for:           |  Best for:           |
  |  structured,         |  semi-structured     |  connected data,     |
  |  transactional data  |  documents, catalogs |  hierarchies,        |
  |                      |                      |  networks            |
  +----------------------+----------------------+----------------------+
```

### Schema Comparison

```
  RELATIONAL (Rigid)                        GRAPH (Flexible)

  CREATE TABLE sensors (                    Vertex 1 (sensor):
    id    INT PRIMARY KEY,                    id, name, sensor_type,
    name  VARCHAR(100) NOT NULL,              unit_of_measure, pk
    type  VARCHAR(50)  NOT NULL,
    unit  VARCHAR(20)  NOT NULL             Vertex 2 (sensor):
  );                                          id, name, sensor_type,
                                              firmware_version, pk
  -- Adding a new column later:
  ALTER TABLE sensors                       -- No ALTER needed.
    ADD firmware VARCHAR(50);               -- Just add properties
                                            -- to new vertices.
  -- ALL existing rows get NULL             -- Existing vertices
  -- for the new column.                    -- stay unchanged.
```

In a graph, two vertices with the same label (e.g., `sensor`) can have **different properties**. There is no `CREATE TABLE` — you define the shape of your data by the properties you add.

---

## 4. Real-World Use Case — IoT Asset Management

### The IoT Hierarchy

```
  Tenant (Company)
  ├── Unit (Building / Plant)
  │   ├── Unit (Floor / Section)
  │   ├── Gateway (IoT Hub device)
  │   │   ├── Equipment (HVAC, Pump, Turbine)
  │   │   │   └── Sensor (TEMP, PRES, HUM, VIB)
  │   │   └── Equipment (Motor, Boiler)
  │   │       └── Sensor (FLOW, VOLT)
  │   └── Gateway (another hub)
  │       └── Equipment (...)
  └── Unit (Another Building)
      └── ...
```

### How This Looks as a Graph

```
                          +----------------+
                          |    Acme Corp   |
                          |   (tenant)     |
                          +-------+--------+
                                  | manages
                                  v
                          +----------------+
                          |  Building-A    |
                          |   (unit)       |
                          +--+----------+--+
                     contains |          | hosts
                              v          v
                     +----------+   +-----------+
                     | Floor-1  |   |  GW-001   |
                     |  (unit)  |   | (gateway) |
                     +----------+   +-----+-----+
                                          | connects_to
                                          v
                                   +-----------+
                                   | HVAC-101  |
                                   |(equipment)|
                                   +-----+-----+
                                         ^
                                         | monitors
                                   +-----------+
                                   | TEMP-001  |-----assigned_to-----+
                                   |  (sensor) |                      |
                                   +-----------+                      v
                                                               +-----------+
                                                               |  GW-001   |
                                                               | (gateway) |
                                                               +-----------+

        Data flow:
          Sensor monitors equipment  (what it measures)
          Sensor assigned_to gateway (how it reports data)
```

**Relationship meanings**:

```
  Edge Label        Direction                  What It Means
  ──────────────    ─────────────────────      ──────────────────────────────
  manages           tenant ──> unit            Company owns/manages a location
  contains          unit ──> unit              Building contains a floor
  hosts             unit ──> gateway           Building houses a gateway device
  connects_to       gateway ──> equipment      Gateway has data link to equipment
  monitors          sensor ──> equipment       Sensor measures this equipment
  assigned_to       sensor ──> gateway         Sensor reports through this gateway
```

> **Edge direction vs data flow**: Edge direction represents **responsibility or coverage**, not data flow.
> The `connects_to` edge means "this gateway is responsible for this equipment" (network topology).
> The actual data travels: equipment → sensor → gateway (sensor reads from equipment, then sends to gateway).
> Edges model **relationships**, not the direction data moves.

```
  Understanding the three edges around a sensor:

                reads from                sends data to
  HVAC-101 <───monitors─── TEMP-001 ───assigned_to───> GW-001
  (equipment)              (sensor)                    (gateway)

  connects_to (GW-001 ──> HVAC-101) answers a DIFFERENT question:
  "Which equipment does this gateway cover in the network?"

  +------------------+---------------------------------------------+
  | Edge             | Question It Answers                         |
  +------------------+---------------------------------------------+
  | connects_to      | "If this gateway fails, which equipment     |
  |                  |  loses network coverage?"                   |
  |                  |  (infrastructure topology)                  |
  +------------------+---------------------------------------------+
  | monitors         | "What equipment does this sensor measure?"  |
  |                  |  (measurement relationship)                 |
  +------------------+---------------------------------------------+
  | assigned_to      | "Where does this sensor send its data?"     |
  |                  |  (data reporting path)                      |
  +------------------+---------------------------------------------+
```

**Notice**: A sensor has **two independent edges**:

- `monitors` → points to the **equipment** it measures
- `assigned_to` → points to the **gateway** it reports data through

These can change independently. A sensor can be reassigned to a different gateway while still monitoring the same equipment.

### Impact Analysis — A Natural Graph Query

```
  "Gateway GW-001 fails — what is affected?"

                               Start here
                                   |
                                   v
                              +----------+
                              |  GW-001  |  <-- FAILURE POINT
                              | (gateway)|
                              +----+-----+
                                   |
                    +--------------+--------------+
                    | connects_to  |              | connects_to
                    v              v              v
               +----------+  +----------+   +----------+
               | HVAC-101 |  | Pump-201 |   | HVAC-102 |  <-- AFFECTED EQUIPMENT
               +-----+----+  +-----+----+   +-----+----+
                     ^              ^              ^
        monitors     |  monitors    |   monitors   |
              +------+       +------+       +------+
              |              |              |
         +----------+  +----------+   +----------+
         | TEMP-001 |  | PRES-001 |   | TEMP-002 |  <-- AFFECTED SENSORS
         +----------+  +----------+   +----------+

  Gremlin query:
  g.V('gw-001').out('connects_to').in('monitors').values('name')

  Result: TEMP-001, PRES-001, TEMP-002  (sensors that lose their data path)
```

This query follows edges outward from the failure point and collects all affected entities. In a relational database, this would require multiple JOINs and recursive queries.

---

## 5. Azure Cosmos DB — What You Need to Know

Azure Cosmos DB is the cloud database service we use for this training. It supports multiple data models through different APIs.

### Cosmos DB APIs

```
                        +------------------------------------------+
                        |         Azure Cosmos DB Engine            |
                        |                                          |
                        |  Partitioning | RU Billing | Replication |
                        +-----+----+----+----+----+----+-----------+
                              |    |    |    |    |    |
         +--------------------+    |    |    |    |    +------------------+
         |              +----------+    |    |    +-----------+           |
         v              v               v    v               v           v
  +-----------+  +-----------+  +-----------+ +-----------+ +--------+ +----------+
  |  NoSQL    |  |  MongoDB  |  |  Gremlin  | | Cassandra | | Table  | |PostgreSQL|
  | (SQL/Core)|  |           |  | (Graph)   | |           | |        | |          |
  +-----------+  +-----------+  +-----------+ +-----------+ +--------+ +----------+
                                 ^^^^^^^^^^^
                                 WE USE THIS
```

All APIs share the same underlying engine, partitioning, billing, and replication. The API is chosen at **account creation time** and cannot be changed afterward. We use the **Gremlin API** for graph data.

### Request Units (RU) — The Cost Model

Every operation in Cosmos DB costs **Request Units (RU)**. Think of RU as a blended currency for CPU, memory, and I/O.

```
  +------------------------------------+------------------+
  |  Operation                         |  Approximate RU  |
  +------------------------------------+------------------+
  |  Read a single small document      |  ~1 RU           |
  |  Write a vertex with 5 properties  |  ~10-14 RU       |
  |  Query within one partition        |  ~5-40 RU        |
  |  Query across all partitions       |  ~100-600 RU     |
  +------------------------------------+------------------+

  Training setting: 400 RU/s (manual provisioning)
  If you exceed 400 RU in one second, Cosmos DB returns HTTP 429 (throttled).
```

### Partitioning — How Data is Organized

Cosmos DB distributes data across **partitions** using a **partition key**.

```
  Container: asset-graph
  Partition Key: /pk

  +----------------------------------+----------------------------------+
  |        Partition: tenant-1       |        Partition: tenant-2       |
  |                                  |                                  |
  |   (tenant-1: Acme Corp)         |   (tenant-2: GlobalTech)        |
  |   (unit-bldgA: Building-A)      |   (unit-bldgC: Building-C)     |
  |   (gw-001: GW-001)              |   (gw-003: GW-003)             |
  |   (equip-hvac101: HVAC-101)     |   (equip-motor601: Motor-601)  |
  |   (sensor-temp001: TEMP-001)    |   (sensor-volt001: VOLT-001)   |
  +----------------------------------+----------------------------------+
```

```
  Query WITH partition key:               Query WITHOUT partition key:

  g.V().has('pk','tenant-1')              g.V().hasLabel('equipment')
    .hasLabel('equipment')                  .count()
    .count()
                                          Scans ALL partitions
  Reads ONE partition only                --> expensive (100-600 RU)
  --> cheap (5-40 RU)
```

**Analogy**: Partitions are like filing cabinets. If you know which cabinet to open (partition key), you search just that one. Without it, you search every cabinet.

**For this training**: The partition key is `/pk`, set to the tenant ID. All data for one tenant lives in the same partition.

---

## 6. Gremlin — The Graph Query Language

Gremlin is the query language for the Cosmos DB Gremlin API. It comes from Apache TinkerPop, an open-source graph framework.

### How Gremlin Works — A Pipeline

A Gremlin query is a **pipeline**: data flows from left to right through a series of steps.

```
  g.V('tenant-1').out('manages').has('type','building').out('hosts').values('name')
  |              |               |                     |             |
  v              v               v                     v             v
  START at     TRAVERSE to     FILTER only           TRAVERSE to   EXTRACT
  tenant-1     all vertices    vertices where         all vertices  the 'name'
               connected via   type = 'building'      connected via property
               'manages' edge                         'hosts' edge

  Data flow:

  [tenant-1] --> [Building-A, Building-B] --> [Building-A, Building-B] --> [GW-001, GW-002] --> ["GW-001", "GW-002"]
                    managed units                type filter passed          hosted gateways       names extracted
```

```
  Think of it like water through pipes:

  g.V('tenant-1')  .out('manages')    .has('type','building')   .out('hosts')       .values('name')
       |                  |                    |                      |                    |
       v                  v                    v                      v                    v
     FAUCET            PIPE                 FILTER                  PIPE               TAP
     emit 1            follow edges         keep only               follow edges       extract
     vertex            (may produce         matching                (may produce       property
                        many outputs)       vertices                 many outputs)      values
```

### Traversal Direction

Edges are directed. Gremlin provides steps to traverse in any direction.

```
                         manages
  +------------+ -----------------------> +-------------+
  | tenant-1   |          EDGE           | unit-bldgA  |
  | Acme Corp  |    since: 2024-01-15    | Building-A  |
  +------------+                          +-------------+

       out('manages')   ------------>   follows the edge direction
       in('manages')    <------------   reverses the edge direction
```

```
  +----------+-----------+--------------------------------------+
  | Step     | Direction | Returns                              |
  +----------+-----------+--------------------------------------+
  | out()    | Forward   | The TARGET vertex at the other end   |
  | in()     | Backward  | The SOURCE vertex at the other end   |
  | both()   | Either    | Vertices in both directions          |
  | outE()   | Forward   | The EDGE object itself               |
  | inE()    | Backward  | The EDGE object itself               |
  +----------+-----------+--------------------------------------+
```

```
  out('manages')  vs  outE('manages').inV()

  +----------+         manages          +----------+
  | tenant-1 | ----------------------> | unit-bldgA|
  +----------+            ^             +----------+
                          |
      out('manages')      |     outE('manages')       .inV()
      goes directly  -----+---->  stops at the   ----->  then moves
      to the vertex       |       edge first             to the vertex
                          |
                     Use outE() when
                     you need to read
                     edge properties
                     (like 'since')
```

**Example — `out()`: you only care about the destination vertex**

```gremlin
g.V('tenant-1').out('manages').values('name')
```

```
  Jumps directly from tenant-1 to the connected unit vertices.
  Returns: ["Building-A"]

  You never see the edge — you just land on the next vertex.
```

**Example — `outE().inV()`: you need to read an edge property first**

```gremlin
g.V('tenant-1').outE('manages').has('since', '2024-01-15').inV().values('name')
```

```
  Step 1: outE('manages')        -- stop at the edge object
  Step 2: .has('since',...)      -- filter edges by their property
  Step 3: .inV()                 -- now move to the target vertex
  Step 4: .values('name')        -- extract the name property

  Returns: ["Building-A"]  (only units connected since that date)

  If you had used out() here, you could NOT filter on 'since'
  because out() skips the edge entirely.
```

**Side-by-side comparison**:

```
  USE out()                           USE outE().inV()
  ──────────────────────────────      ──────────────────────────────────
  You only need the target vertex     You need to read or filter
                                      on an edge property

  g.V('tenant-1')                     g.V('tenant-1')
    .out('manages')                     .outE('manages')
    .values('name')                     .has('since', '2024-01-15')
                                        .inV()
  Simpler, slightly cheaper RU         .values('name')

                                      More steps, but edge properties
                                      are accessible
```

**`inV()` and `outV()` — naming from the edge's perspective**:

Every edge has two ends. The names `inV()` and `outV()` describe which end of the edge you move to — from the edge's point of view, not the traversal direction.

```
  tenant-1  ───manages───>  unit-bldgA
      ^                          ^
      |                          |
  outV() = SOURCE            inV() = TARGET
  (edge leaves here)         (edge arrives here)

  Step            Moves to
  ──────────      ───────────────────────────────────
  inV()           Target vertex  (where arrow points TO)
  outV()          Source vertex  (where arrow comes FROM)
  bothV()         Both vertices
```

These steps only work when the traverser is sitting **on an edge** — after `outE()`, `inE()`, `bothE()`, or when starting with `g.E()`.

### Gremlin vs SQL — How They Differ

```
  +----------------------------+-----------------------------------+
  |       SQL (Declarative)    |       Gremlin (Data-flow)         |
  +----------------------------+-----------------------------------+
  |                            |                                   |
  |  "Find me all X"          |  "Start here, follow this edge,   |
  |   Database decides how    |   filter by this, go there"       |
  |                            |   You control the path            |
  |                            |                                   |
  |  SELECT ... FROM ... WHERE |  g.V() -> step -> step -> step   |
  |   ... JOIN ...             |                                   |
  |                            |                                   |
  |  Query planner optimizes   |  You are the optimizer:           |
  |  the execution plan        |  step order matters               |
  |                            |                                   |
  +----------------------------+-----------------------------------+
```

---

## 7. The Training Graph — Configuration

```
  +-------------------------------+
  |  Database:    iot-graph-db    |
  |  Container:   asset-graph    |
  |  Partition:   /pk            |
  |  Throughput:  400 RU/s       |
  +-------------------------------+
```

### Vertex Types

```
  +-------------+----------------------------+-------------------------------+
  | Label       | Represents                 | Key Properties                |
  +-------------+----------------------------+-------------------------------+
  | tenant      | A company / organization   | name, industry                |
  | unit        | A location (building/floor)| name, type, location          |
  | gateway     | An IoT hub device          | name, model, status           |
  | equipment   | Physical asset monitored   | name, type, manufacturer      |
  | sensor      | Measures a condition       | name, sensor_type, threshold  |
  +-------------+----------------------------+-------------------------------+
```

### Edge Types

```
  +-------------+------------------------+----------------------------------+
  | Label       | Direction              | Meaning                          |
  +-------------+------------------------+----------------------------------+
  | manages     | tenant --> unit         | Company manages a location       |
  | contains    | unit --> unit           | Building contains a floor        |
  | hosts       | unit --> gateway        | Location houses a gateway        |
  | connects_to | gateway --> equipment   | Network coverage (not data flow) |
  | monitors    | sensor --> equipment    | Sensor measures this equipment   |
  | assigned_to | sensor --> gateway      | Sensor reports through gateway   |
  +-------------+------------------------+----------------------------------+

  Note: connects_to does NOT mean data flows from gateway to equipment.
  It means the gateway covers/serves that equipment in the network.
  Actual data flow: equipment --> sensor (reads) --> gateway (reports)
```

---

## 8. Gremlin — Core Syntax Reference

### Creating Vertices and Edges

```gremlin
// Create a vertex
g.addV('tenant').property('id', 'tenant-1').property('name', 'Acme Corp').property('pk', 'tenant-1')

// Create an edge with a property
g.V('tenant-1').addE('manages').to(g.V('unit-bldgA')).property('since', '2024-01-15')
```

```
  +------------+       manages        +-------------+
  | tenant-1   | ------------------> | unit-bldgA  |
  | Acme Corp  |   since: 2024-01-15 | Building-A  |
  +------------+                      +-------------+
```

### Traversing and Filtering

```gremlin
// Traverse forward, extract a property
g.V('tenant-1').out('manages').values('name')
// Returns: Building-A

// Filter by property value during traversal
g.V('tenant-1').out('manages').has('type', 'building').out('hosts').values('name')
// Returns: names of gateways hosted in buildings managed by Acme Corp

// Traverse backward
g.V('unit-bldgA').in('manages').values('name')
// Returns: Acme Corp

// Count all vertices
g.V().count()

// Count by label
g.V().hasLabel('sensor').count()
```

### Reading Edge Properties

```gremlin
// Use outE().inV() when you need to filter on an edge property
g.V('tenant-1').outE('manages').has('since', '2024-01-15').inV().values('name')
// Returns: Building-A  (only units connected since that date)

// Read edge properties directly
g.V('tenant-1').outE('manages').values('since')
// Returns: 2024-01-15
```

---

## Quick Reference

### Gremlin Step Cheat Sheet

```
  +---------------------------+------------------------------------------+
  | Step                      | What It Does                             |
  +---------------------------+------------------------------------------+
  | g.V()                     | Start with all vertices                  |
  | g.V('id')                 | Start with a specific vertex             |
  | g.E()                     | Start with all edges                     |
  | .addV('label')            | Create a new vertex                      |
  | .addE('label').to(target) | Create a new edge                        |
  | .property('key', 'val')   | Set a property                           |
  | .out('label')             | Traverse forward along an edge           |
  | .in('label')              | Traverse backward along an edge          |
  | .both('label')            | Traverse in either direction             |
  | .outE('label')            | Get the outgoing edge object             |
  | .inE('label')             | Get the incoming edge object             |
  | .inV()                    | Move to the target vertex of an edge     |
  | .outV()                   | Move to the source vertex of an edge     |
  | .has('key', 'val')        | Filter by property value                 |
  | .hasLabel('label')        | Filter by label                          |
  | .values('key')            | Extract a property value                 |
  | .count()                  | Count the results                        |
  | .path()                   | Show the full traversal route            |
  +---------------------------+------------------------------------------+
```

### Training Environment

```
  +-----------------------------+-----------------------+
  | Tool                        | Purpose               |
  +-----------------------------+-----------------------+
  | Azure Portal                | Account management    |
  | Data Explorer               | Run Gremlin queries   |
  | Azure Cloud Shell           | Azure CLI commands    |
  | VS Code + PowerShell        | .NET/C# code          |
  +-----------------------------+-----------------------+

  Access Data Explorer:
  Azure Portal --> Cosmos DB Account --> Data Explorer
```
