# Day 01 — Introduction to Graph Databases & Property Graph Model

**Duration**: 3 hours
**Participation Mode**: Guided Hands-On — execute queries alongside the instructor.

---

## Lab 1: Create Azure Cosmos DB Account with Gremlin API (~20 min)

**Objective**: Provision a Cosmos DB account configured for the Gremlin API.

1. Open Azure Portal: [https://portal.azure.com](https://portal.azure.com)

2. Click **Create a resource** → search for **Azure Cosmos DB** → select **Azure Cosmos DB**

3. On the API selection screen, select **Apache Gremlin**

4. Configure the account:

   | Setting | Value |
   |---------|-------|
   | Subscription | Your Azure subscription |
   | Resource Group | Create new: `rg-graphdb-training` |
   | Account Name | `graphdb-training-<your-initials>` (lowercase, globally unique) |
   | Location | Nearest Azure region |
   | Capacity mode | **Provisioned throughput** |
   | Apply Free Tier Discount | Apply if available |
   | Geo-Redundancy | **Disable** |
   | Multi-region Writes | **Disable** |

   > Provisioned throughput mode allows explicit RU measurement — critical for later performance labs.

5. Click **Review + create** → **Create**

6. Wait for deployment to complete (2–5 minutes)

7. Click **Go to resource** to open the Cosmos DB account overview

**Success**: Cosmos DB account with Apache Gremlin API deployed and visible in Azure Portal.

---

## Lab 2: Create Database and Graph Container (~15 min)

**Objective**: Create the `iot-graph-db` database and `asset-graph` container.

1. In your Cosmos DB account, click **Data Explorer** in the left menu

2. Click **New Database**:
   - Database id: `iot-graph-db`
   - Click **OK**

3. Click **New Graph**:

   | Setting | Value |
   |---------|-------|
   | Database id | Use existing: `iot-graph-db` |
   | Graph id | `asset-graph` |
   | Partition key | `/pk` |
   | Throughput | **Manual** — `400` RU/s |

   > The partition key `/pk` groups related vertices by tenant. Every vertex will include a `pk` property set to its tenant ID.

   > 400 RU/s is sufficient for training. Do NOT enable Autoscale. Do NOT increase RU/s during training.

4. Click **OK**

5. Verify in Data Explorer: expand `iot-graph-db` → `asset-graph` appears as a graph container

**Success**: `iot-graph-db` database with `asset-graph` container visible in Data Explorer.

---

## Lab 3: Add First Vertices and Edges (~15 min)

**Objective**: Create the first graph vertices and a relationship using Gremlin in Data Explorer.

> A vertex represents an entity (a thing). An edge represents a relationship between two vertices. Together they form a graph.

1. In Data Explorer, expand `iot-graph-db` → click on `asset-graph`

2. Open the Gremlin query editor (query tab at the top)

3. Add a tenant vertex. Run:

```gremlin
g.addV('tenant').property('id', 'tenant-1').property('name', 'Acme Corp').property('industry', 'manufacturing').property('pk', 'tenant-1')
```

> This creates a vertex with label `tenant`, a unique `id`, descriptive properties, and a partition key `pk`.

4. Add a unit vertex. Run:

```gremlin
g.addV('unit').property('id', 'unit-bldgA').property('name', 'Building-A').property('type', 'building').property('location', 'Chicago').property('pk', 'tenant-1')
```

> **Note**: This vertex uses the same partition key (`tenant-1`) as the previous vertex, but a different `id` (`unit-bldgA`). This is a valid and common pattern.

5. Add an edge — Acme Corp manages Building-A. Run:

```gremlin
g.V('tenant-1').addE('manages').to(g.V('unit-bldgA')).property('since', '2024-01-15')
```

> `addE('manages')` creates a directed edge from the tenant to the unit. The `since` property captures when the relationship started.

6. Verify vertex count (**JSON Results**). Run:

```gremlin
g.V().count()
```

> Expected: 2

7. Verify edge count (**JSON Results**). Run:

```gremlin
g.E().count()
```

> Expected: 1

**Success**: 2 vertices and 1 edge in the graph. Note that `.count()` queries do not show a Graph tab.

---

## Lab 4: Visualize the Graph in Data Explorer (~10 min)

**Objective**: Use Data Explorer's graph visualization to see vertices and edges.

1. Run a query to fetch both vertices and their edges to ensure the Graph tab appears:

```gremlin
g.V().union(identity(), outE())
```

2. In the results pane, switch to the **Graph** tab.

3. Observe the two vertices and the edge between them rendered visually.

> **UI Tip**: If the Graph tab is empty, ensure your query returns actual Vertex or Edge objects. Queries returning data (like counts or property names) only appear in the **JSON Results** tab.

4. Click on any vertex in the graph view — the side panel shows its properties (id, label, name, industry/type, pk).

5. Click on the edge — the side panel shows its properties (label: manages, since).

6. Run a traversal to fetch a vertex and its connections:

```gremlin
g.V('tenant-1').has('pk', 'tenant-1').union(identity(), outE())
```

> Using `.union(identity(), outE())` is the most reliable way to populate the Graph tab with both the nodes and the lines connecting them.

7. Switch between **Graph** view and **JSON Results** view to compare how the same data appears in both formats.

**Success**: Can visualize vertices and edges graphically in Data Explorer and inspect properties via the UI.

---

## Lab 5: Create Vertices with Multiple Properties (~15 min)

**Objective**: Build out the seed dataset with richly-propertied vertices.

> **Key Concept**: All vertices in this lab belong to the same logical partition (`tenant-1`) but have unique `id` values. This grouping allows for efficient single-partition queries later.

1. Add Building-B under Acme Corp. Run:

```gremlin
g.addV('unit').property('id', 'unit-bldgB').property('name', 'Building-B').property('type', 'building').property('location', 'Detroit').property('pk', 'tenant-1')
```

2. Add Floor-1 and Floor-2 under Building-A. Run each:

```gremlin
g.addV('unit').property('id', 'unit-floor1').property('name', 'Floor-1').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')
```

```gremlin
g.addV('unit').property('id', 'unit-floor2').property('name', 'Floor-2').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')
```

3. Add four gateways. Run each:

```gremlin
g.addV('gateway').property('id', 'gw-001').property('name', 'GW-001').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.1').property('pk', 'tenant-1')
```

```gremlin
g.addV('gateway').property('id', 'gw-002').property('name', 'GW-002').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.2').property('pk', 'tenant-1')
```

```gremlin
g.addV('gateway').property('id', 'gw-003').property('name', 'GW-003').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.2.1').property('pk', 'tenant-1')
```

```gremlin
g.addV('gateway').property('id', 'gw-004').property('name', 'GW-004').property('model', 'IoT-Hub-5000').property('status', 'inactive').property('ip_address', '10.0.2.2').property('pk', 'tenant-1')
```

4. Add four equipment vertices. Run each:

```gremlin
g.addV('equipment').property('id', 'equip-hvac101').property('name', 'HVAC-101').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-15').property('status', 'running').property('pk', 'tenant-1')
```

```gremlin
g.addV('equipment').property('id', 'equip-hvac102').property('name', 'HVAC-102').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-20').property('status', 'running').property('pk', 'tenant-1')
```

```gremlin
g.addV('equipment').property('id', 'equip-pump201').property('name', 'Pump-201').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-10').property('status', 'running').property('pk', 'tenant-1')
```

```gremlin
g.addV('equipment').property('id', 'equip-pump202').property('name', 'Pump-202').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-12').property('status', 'stopped').property('pk', 'tenant-1')
```

5. Verify vertex counts by label (these return numbers and will **not** show a Graph tab). Run each:

```gremlin
g.V().hasLabel('unit').count()
```

> Expected: 4

---

### Visualize the Graph (~5 min)

To see the vertices you just created in the **Graph** tab, run a query that returns the actual vertices:

```gremlin
g.V().hasLabel(within('unit', 'gateway', 'equipment'))
```

1. Click **Execute Gremlin Query**.
2. Switch to the **Graph** tab. You should see the disconnected nodes.

**Success**: All unit, gateway, and equipment vertices created.

> **Why no Graph tab?**: The Azure Portal only shows the "Graph" tab when the query result contains Vertices or Edges. Queries like `.count()`, `.values()`, or `.valueMap()` return raw data (JSON only).

---

## Lab 6: Create Directed Edges with Properties (~15 min)

**Objective**: Understand edge directionality by creating relationships with meaningful direction.

> In a property graph, edges have direction. "Tenant manages Unit" is not the same as "Unit manages Tenant." Direction captures the nature of the relationship.

1. Create `manages` edge — Acme Corp manages Building-B. Run:

```gremlin
g.V('tenant-1').addE('manages').to(g.V('unit-bldgB')).property('since', '2024-03-01')
```

2. Create `contains` edges — Building-A contains Floor-1 and Floor-2. Run each:

```gremlin
g.V('unit-bldgA').addE('contains').to(g.V('unit-floor1')).property('relationship_type', 'structural')
```

```gremlin
g.V('unit-bldgA').addE('contains').to(g.V('unit-floor2')).property('relationship_type', 'structural')
```

3. Create `hosts` edges — units host gateways. Run each:

```gremlin
g.V('unit-bldgA').addE('hosts').to(g.V('gw-001')).property('installed_date', '2023-05-01')
```

```gremlin
g.V('unit-bldgA').addE('hosts').to(g.V('gw-002')).property('installed_date', '2023-05-01')
```

```gremlin
g.V('unit-bldgB').addE('hosts').to(g.V('gw-003')).property('installed_date', '2023-07-15')
```

```gremlin
g.V('unit-bldgB').addE('hosts').to(g.V('gw-004')).property('installed_date', '2023-07-15')
```

4. Create `connects_to` edges — gateways connect to equipment. Run each:

```gremlin
g.V('gw-001').addE('connects_to').to(g.V('equip-hvac101')).property('protocol', 'mqtt').property('signal_strength', 95)
```

```gremlin
g.V('gw-001').addE('connects_to').to(g.V('equip-pump201')).property('protocol', 'modbus').property('signal_strength', 88)
```

```gremlin
g.V('gw-002').addE('connects_to').to(g.V('equip-hvac102')).property('protocol', 'mqtt').property('signal_strength', 92)
```

```gremlin
g.V('gw-002').addE('connects_to').to(g.V('equip-pump202')).property('protocol', 'modbus').property('signal_strength', 85)
```

> Edge properties like `protocol` and `signal_strength` add operational context to the relationship — they describe how the gateway connects, not just that it connects.

5. Verify total edge count (**JSON Result**). Run:

```gremlin
g.E().count()
```

> Expected: 12

6. **Visualize the connections**: To see the relationships in the **Graph** tab, run:

```gremlin
g.V().hasLabel('unit').union(identity(), outE())
```

> **Why this works**: This query returns both the Vertices (`identity()`) and their outgoing Edges (`outE()`) as a flat list. This is the most reliable way to make the **Graph** tab appear in the Azure Portal.

**Success**: All edges created. Switch to the **Graph** tab to see the connected structure.

---

## Lab 7: Multiple Edges Between Same Vertices (~15 min)

**Objective**: Demonstrate that graph databases allow multiple edges between the same pair of vertices.

> Unlike relational databases, a graph allows multiple distinct relationships between the same two entities. A sensor can both monitor equipment and be assigned to a gateway — two separate edges from the same sensor.

1. Add sensor vertices. Run each:

```gremlin
g.addV('sensor').property('id', 'sensor-temp001').property('name', 'TEMP-001').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 85).property('status', 'active').property('pk', 'tenant-1')
```

```gremlin
g.addV('sensor').property('id', 'sensor-temp002').property('name', 'TEMP-002').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 90).property('status', 'active').property('pk', 'tenant-1')
```

```gremlin
g.addV('sensor').property('id', 'sensor-pres001').property('name', 'PRES-001').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('pk', 'tenant-1')
```

```gremlin
g.addV('sensor').property('id', 'sensor-pres002').property('name', 'PRES-002').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 160).property('status', 'active').property('pk', 'tenant-1')
```

2. Create a `monitors` edge — TEMP-001 monitors HVAC-101. Run:

```gremlin
g.V('sensor-temp001').addE('monitors').to(g.V('equip-hvac101')).property('attached_date', '2024-03-01').property('position', 'intake')
```

3. Create an `assigned_to` edge — same sensor assigned to a gateway. Run:

```gremlin
g.V('sensor-temp001').addE('assigned_to').to(g.V('gw-001')).property('channel', 1)
```

> TEMP-001 now has two outgoing edges: `monitors` (to equipment) and `assigned_to` (to gateway). This is the dual-relationship pattern common in IoT.

4. Create edges for the remaining sensors. Run each:

```gremlin
g.V('sensor-temp002').addE('monitors').to(g.V('equip-hvac102')).property('attached_date', '2024-03-05').property('position', 'exhaust')
```

```gremlin
g.V('sensor-temp002').addE('assigned_to').to(g.V('gw-002')).property('channel', 1)
```

```gremlin
g.V('sensor-pres001').addE('monitors').to(g.V('equip-pump201')).property('attached_date', '2024-04-01').property('position', 'outlet')
```

```gremlin
g.V('sensor-pres001').addE('assigned_to').to(g.V('gw-001')).property('channel', 2)
```

```gremlin
g.V('sensor-pres002').addE('monitors').to(g.V('equip-pump202')).property('attached_date', '2024-04-05').property('position', 'inlet')
```

```gremlin
g.V('sensor-pres002').addE('assigned_to').to(g.V('gw-002')).property('channel', 2)
```

5. Query paths from TEMP-001 to see the multi-edge pattern (**Graph Result**). Run:

```gremlin
g.V('sensor-temp001').union(identity(), outE())
```

6. Switch to the **Graph** tab. 

**Success**: You should see one sensor node with two lines pointing to two different nodes (the Gateway and the HVAC unit).

---

## Lab 8: Explore Schema-Optional Behavior (~15 min)

**Objective**: Demonstrate that vertices with the same label can have different properties.

> Graph databases are schema-optional. Two `sensor` vertices can have different properties — no schema definition required upfront. This contrasts with relational databases where all rows in a table must have the same columns.

1. Add a sensor with additional properties not present on other sensors. Run:

```gremlin
g.addV('sensor').property('id', 'sensor-pres001-v2').property('name', 'PRES-001-V2').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('calibration_date', '2025-06-15').property('firmware_version', '2.3.1').property('pk', 'tenant-1')
```

> This sensor has `calibration_date` and `firmware_version` — properties that other sensors do not have.

2. Compare properties of two sensors. Run each:

```gremlin
g.V('sensor-temp001').valueMap(true)
```

```gremlin
g.V('sensor-pres001-v2').valueMap(true)
```

> `valueMap(true)` returns all properties including `id` and `label`. Compare the two — PRES-001-V2 has extra properties that TEMP-001 does not.

3. Add a property to an existing vertex. Run:

```gremlin
g.V('sensor-temp001').property('firmware_version', '2.1.0')
```

> The `property()` step on an existing vertex adds or updates a property without altering other properties.

4. Query vertices by a property that only some have. Run:

```gremlin
g.V().hasLabel('sensor').has('calibration_date').values('name', 'calibration_date')
```

> Only sensors with `calibration_date` are returned. No error for sensors that lack this property — they are simply excluded.

5. Verify the updated sensor now has `firmware_version`. Run:

```gremlin
g.V('sensor-temp001').valueMap(true)
```

**Success**: Vertices with the same label but different property sets, queryable without errors.
