# Day 06 — Advanced Data Shaping & Aggregations

**Graph reminder:** Vertices — `tenant`, `unit`, `gateway`, `equipment`, `sensor`. Edges — tenant `manages` unit; unit `hosts` gateway; gateway `connects_to` equipment; sensor `monitors` equipment; sensor `assigned_to` gateway; unit `contains` unit (hierarchy). All vertices have `pk` (partition key).

---

## Lab 1: Projection Techniques

### 1.1 — Structured output (three columns)

**Scenario:** Report all equipment with only name, type, and status as columns.

**Equivalent SELECT:**
```sql
SELECT name, type, status FROM equipment;
```
**Path:** Equipment only (no edge traversal).

---

### 1.2 — Computed column: sensor count per equipment

**Scenario:** Each equipment row should include how many sensors monitor it.

**Equivalent SELECT:**
```sql
SELECT e.name,
       (SELECT COUNT(*) FROM monitors m WHERE m.equipment_id = e.id) AS sensor_count
FROM equipment e;
```
**Path:** Start at equipment; go backward along `monitors` to sensors; count.

---

### 1.3 — Pairs: sensor name and equipment name

**Scenario:** For every sensor–equipment monitoring link, output (sensor name, equipment name).

**Equivalent SELECT:**
```sql
SELECT s.name AS sensor_name, e.name AS equipment_name
FROM sensor s
JOIN monitors m ON m.sensor_id = s.id
JOIN equipment e ON m.equipment_id = e.id;
```
**Path:** Start at sensor; follow `monitors` out to equipment.

---

### 1.4 — Equipment with gateway name(s)

**Scenario:** List each equipment with the name(s) of the gateway that connects to it (as a list; empty if none).

**Equivalent SELECT:**
```sql
SELECT e.name AS equipment,
       (SELECT ARRAY_AGG(g.name) FROM gateway g
        JOIN connects_to ct ON ct.gateway_id = g.id
        WHERE ct.equipment_id = e.id) AS gateway
FROM equipment e;
```
**Path:** Start at equipment; go backward along `connects_to` to gateway(s).

---

### 1.5 — Equipment with full context (tenant-1)

**Scenario:** One row per equipment (tenant-1) with name, type, list of gateway names, list of sensor names, and list of unit names (building/floor) hosting the gateway.

**Equivalent SELECT:**
```sql
SELECT e.name, e.type,
       (SELECT ARRAY_AGG(g.name) FROM connects_to ct JOIN gateway g ON ct.gateway_id = g.id WHERE ct.equipment_id = e.id) AS gateway,
       (SELECT ARRAY_AGG(s.name) FROM monitors m JOIN sensor s ON m.sensor_id = s.id WHERE m.equipment_id = e.id) AS sensors,
       (SELECT ARRAY_AGG(u.name) FROM connects_to ct JOIN gateway g ON ct.gateway_id = g.id
        JOIN hosts h ON h.gateway_id = g.id JOIN unit u ON h.unit_id = u.id WHERE ct.equipment_id = e.id) AS unit
FROM equipment e
WHERE e.pk = 'tenant-1';
```
**Path:** Start at equipment; three backward paths: gateway, sensors, gateway then hosts to unit.

---

## Lab 2: Grouping

### 2.1 — Group equipment by type (list of names)

**Scenario:** Group all equipment by type; each type shows the list of equipment names.

**Equivalent SELECT:**
```sql
SELECT type, ARRAY_AGG(name) AS names FROM equipment GROUP BY type;
```
**Path:** Equipment only; group by property `type`.

---

### 2.2 — Count equipment by type

**Scenario:** How many equipment per type (e.g. 2 HVAC, 2 pump)?

**Equivalent SELECT:**
```sql
SELECT type, COUNT(*) FROM equipment GROUP BY type;
```
**Path:** Equipment only; group by `type`, count.

---

### 2.3 — Count sensors by sensor_type

**Scenario:** How many sensors per type (temperature, pressure, etc.)?

**Equivalent SELECT:**
```sql
SELECT sensor_type, COUNT(*) FROM sensor GROUP BY sensor_type;
```
**Path:** Sensor only; group by `sensor_type`, count.

---

### 2.4 — Nested grouping: type, then status

**Scenario:** For each equipment type, show count by status (e.g. hvac: 2 running, 0 stopped).

**Equivalent SELECT:**
```sql
SELECT type, status, COUNT(*) FROM equipment GROUP BY type, status;
-- or: type -> (status -> count) as a nested structure
```
**Path:** Equipment only; group by type, then within each group by status.

---

### 2.5 — Gateways by status (list of names)

**Scenario:** Group gateways by status; for each status, list gateway names.

**Equivalent SELECT:**
```sql
SELECT status, ARRAY_AGG(name) AS names FROM gateway GROUP BY status;
```
**Path:** Gateway only; group by `status`, collect names.

---

### 2.6 — Equipment count per building

**Scenario:** Each building (unit type = building) with how many equipment under it (via hosts → gateway → connects_to).

**Equivalent SELECT:**
```sql
SELECT u.name AS building,
       (SELECT COUNT(*) FROM hosts h
        JOIN connects_to ct ON ct.gateway_id = h.gateway_id
        WHERE h.unit_id = u.id) AS equipment_count
FROM unit u
WHERE u.type = 'building';
```
**Path:** Start at unit (building); out `hosts` → gateway; out `connects_to` → equipment; count.

---

## Lab 3: Fold, Unfold, Count

### 3.1 — All sensor names as one list

**Scenario:** Single result: the list of all sensor names for tenant-1.

**Equivalent SELECT:**
```sql
SELECT ARRAY_AGG(name) AS names FROM sensor WHERE pk = 'tenant-1';
```
**Path:** Sensor (tenant-1) → values(name) → collect into one list.

---

### 3.2 — Count of items in that list

**Scenario:** How many sensors (tenant-1)? After building the list, count its size.

**Equivalent SELECT:**
```sql
SELECT array_length(ARRAY_AGG(name)) FROM sensor WHERE pk = 'tenant-1';
-- or: SELECT COUNT(*) FROM sensor WHERE pk = 'tenant-1';
```
**Path:** Same as 3.1, then count elements in the list (count local).

---

### 3.3 — Unfold: one list → one row per element

**Scenario:** Turn the single list of names back into one result row per name.

**Equivalent SELECT:**
```sql
SELECT unnest(ARRAY_AGG(name)) AS name FROM sensor WHERE pk = 'tenant-1';
```
**Path:** One list → expand to one row per element.

---

### 3.4 — Per gateway: name + list of equipment names

**Scenario:** One row per gateway (tenant-1) with gateway name and list of connected equipment names.

**Equivalent SELECT:**
```sql
SELECT g.name AS gateway,
       (SELECT ARRAY_AGG(e.name) FROM connects_to ct JOIN equipment e ON ct.equipment_id = e.id WHERE ct.gateway_id = g.id) AS equipment_list
FROM gateway g
WHERE g.pk = 'tenant-1';
```
**Path:** Start at gateway; out `connects_to` to equipment; collect names into list.

---

### 3.5 — Global count vs per-gateway count

**Scenario:** (a) Total equipment count. (b) Per gateway: name and count of equipment it connects to.

**Equivalent SELECT:**
```sql
-- (a)
SELECT COUNT(*) FROM equipment;

-- (b)
SELECT g.name AS gateway,
       (SELECT COUNT(*) FROM connects_to ct WHERE ct.gateway_id = g.id) AS equipment_count
FROM gateway g;
```
**Path:** (a) Equipment only, count. (b) Gateway → out connects_to → count per gateway.

---

### 3.6 — Numeric aggregates (mean, min)

**Scenario:** Average and minimum sensor threshold for tenant-1.

**Equivalent SELECT:**
```sql
SELECT AVG(threshold) AS mean_threshold, MIN(threshold) AS min_threshold
FROM sensor
WHERE pk = 'tenant-1';
```
**Path:** Sensor (tenant-1) → values(threshold) → aggregate.

---

## Lab 4: Asset Summary Queries

### 4.1 — Asset inventory per tenant

**Scenario:** One row per tenant with columns: name, count of units, gateways, equipment, and (distinct) sensors.

**Equivalent SELECT:**
```sql
SELECT t.name AS tenant,
       (SELECT COUNT(*) FROM manages m WHERE m.tenant_id = t.id) AS units,
       (SELECT COUNT(*) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id WHERE m.tenant_id = t.id) AS gateways,
       (SELECT COUNT(*) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id
        JOIN connects_to ct ON ct.gateway_id = h.gateway_id WHERE m.tenant_id = t.id) AS equipment,
       (SELECT COUNT(DISTINCT s.id) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id
        JOIN connects_to ct ON ct.gateway_id = h.gateway_id
        JOIN monitors mon ON mon.equipment_id = ct.equipment_id JOIN sensor s ON mon.sensor_id = s.id
        WHERE m.tenant_id = t.id) AS sensors
FROM tenant t;
```
**Path:** Start at tenant; four traversals: manages (units); +hosts (gateways); +connects_to (equipment); +in monitors dedup (sensors).

---

### 4.2 — Equipment health summary (tenant-1)

**Scenario:** Count of equipment by status (running, stopped) for tenant-1.

**Equivalent SELECT:**
```sql
SELECT status, COUNT(*) FROM equipment WHERE pk = 'tenant-1' GROUP BY status;
```
**Path:** Equipment (tenant-1) only; group by status, count.

---

### 4.3 — Sensor distribution by type (tenant-1)

**Scenario:** Count of sensors by sensor_type for tenant-1.

**Equivalent SELECT:**
```sql
SELECT sensor_type, COUNT(*) FROM sensor WHERE pk = 'tenant-1' GROUP BY sensor_type;
```
**Path:** Sensor (tenant-1) only; group by sensor_type, count.

---

### 4.4 — Gateway load (tenant-1)

**Scenario:** Per gateway: name, count of equipment, count of distinct sensors on that equipment.

**Equivalent SELECT:**
```sql
SELECT g.name AS gateway,
       (SELECT COUNT(*) FROM connects_to ct WHERE ct.gateway_id = g.id) AS equipment_count,
       (SELECT COUNT(DISTINCT mon.sensor_id) FROM connects_to ct
        JOIN monitors mon ON mon.equipment_id = ct.equipment_id WHERE ct.gateway_id = g.id) AS sensor_count
FROM gateway g
WHERE g.pk = 'tenant-1';
```
**Path:** Start at gateway; out connects_to (count equipment); out connects_to, in monitors (count distinct sensors).

---

### 4.5 — Complete tenant dashboard (tenant-1)

**Scenario:** One row for tenant-1: name, total_units (incl. hierarchy), total_gateways, total_equipment, active_equipment (status=running), total_sensors, active_sensors (status=active).

**Equivalent SELECT:**
```sql
SELECT t.name AS tenant,
       (SELECT COUNT(*) FROM manages m
        JOIN unit u ON u.id = m.unit_id
        -- recursive units via contains
        ) AS total_units,
       (SELECT COUNT(DISTINCT h.gateway_id) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id WHERE m.tenant_id = t.id) AS total_gateways,
       (SELECT COUNT(DISTINCT ct.equipment_id) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id JOIN connects_to ct ON ct.gateway_id = h.gateway_id WHERE m.tenant_id = t.id) AS total_equipment,
       (SELECT COUNT(DISTINCT ct.equipment_id) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id JOIN connects_to ct ON ct.gateway_id = h.gateway_id JOIN equipment e ON e.id = ct.equipment_id AND e.status = 'running' WHERE m.tenant_id = t.id) AS active_equipment,
       (SELECT COUNT(DISTINCT mon.sensor_id) FROM manages m JOIN hosts h ON h.unit_id = m.unit_id JOIN connects_to ct ON ct.gateway_id = h.gateway_id JOIN monitors mon ON mon.equipment_id = ct.equipment_id WHERE m.tenant_id = t.id) AS total_sensors,
       (SELECT COUNT(DISTINCT mon.sensor_id) FROM ... JOIN sensor s ON s.id = mon.sensor_id AND s.status = 'active' ...) AS active_sensors
FROM tenant t
WHERE t.id = 'tenant-1';
```
**Path:** Start at tenant; seven traversals (one recursive for units; rest along manages→hosts→connects_to→monitors with filters/dedup).
