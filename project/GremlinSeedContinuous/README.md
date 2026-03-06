# GremlinSeedContinuous

Loads graph data **continuously** into a separate Cosmos DB container at a configurable interval. Same schema as GremlinSeed (Day 4); use it to grow a graph for performance testing (Day 5) without touching `asset-graph`.

## How it works

- Runs in a loop: generate a batch of vertices and edges → send via Gremlin → wait **IntervalSeconds** → repeat. Stop with Ctrl+C or after **MaxBatches** (if set).
- Each batch creates **new** tenants (and optionally **sites**), then buildings, floors, rooms, gateways, equipment, sensors, and edges. IDs include batch index (e.g. `tenant-load-1-0`, `unit-bldg-1-0-0-0`) so batches never overlap.
- **On startup** the app asks: **"Clean up and start from batch 1? (y/N)"**. Answer **y** to start from batch 1 (bookmark is cleared so the next run will insert from batch 1 again; delete graph data in the portal if you want a truly empty graph). Answer **N** (or Enter) to **resume** from the last completed batch (bookmark is used; no duplicate batches).
- **Bookmark file**: the last completed batch index is saved so you can resume. It is stored in the **application directory** (where the exe runs, e.g. `bin/Debug/net10.0/`) as **`gremlin-continuous-bookmark-{Graph}.txt`** (e.g. `gremlin-continuous-bookmark-asset-graph-load.txt`). When you choose cleanup (y), this file is deleted.
- **Target container**: set **CosmosDb:Graph** to e.g. `asset-graph-load`. The app creates the container if it does not exist (partition key `/pk`). It will not write to `asset-graph`.

## Hierarchy (with SitesPerTenant ≥ 1)

When **SitesPerTenant** ≥ 1: tenant → **manages** → **site** (unit with `type=site`) → **contains** → building → contains → floor → contains → room; building → **hosts** → gateway → **connects_to** → equipment; sensors **monitors** equipment and **assigned_to** gateway.

When **SitesPerTenant** = 0: tenant → **manages** → building directly (no site level).

ID format (with sites): `tenant-load-{batch}-{t}`, `unit-site-{batch}-{t}-{s}`, `unit-bldg-{batch}-{t}-{s}-{b}`, `unit-floor-...`, `unit-room-...`, `gw-{batch}-{t}-{s}-{b}-{g}`, `equip-{batch}-{t}-{s}-{b}-{g}-{e}`, `sensor-{batch}-{t}-{s}-{b}-{g}-{sn}`. This matches the Day 5 hands-on expectations.

## How to run

From repo root:

```bash
dotnet run --project project/GremlinSeedContinuous
```

Or from the project folder:

```bash
cd project/GremlinSeedContinuous
dotnet run
```

- Set **CosmosDb:Hostname** and **CosmosDb:Key** in `appsettings.json` (or env **COSMOS_ENDPOINT**, **COSMOS_KEY**). Optional: create **`.cosmos-key`** in this folder with your key on one line (gitignored).
- When you run, you are prompted: **Clean up and start from batch 1? (y/N)** — choose **y** to reset and start from batch 1, or **N** to resume from the last saved batch.
- Query the data in Data Explorer under **iot-graph-db** → **asset-graph-load** → Gremlin. Schema matches Day 4 (tenant, unit with type site/building/floor/room, gateway, equipment, sensor; same edge types).

## Configuration (appsettings.json)

| Section | Key | Meaning |
|---------|-----|--------|
| CosmosDb | Hostname, Key | Cosmos account (Gremlin endpoint). |
| CosmosDb | Database | Database name (default `iot-graph-db`). |
| CosmosDb | Graph | Target container (e.g. `asset-graph-load`). Must not be `asset-graph`. |
| Continuous | IntervalSeconds | Pause between batches in seconds (default 10). |
| Continuous | TenantsPerBatch | New tenants per batch (default 1). |
| Continuous | SitesPerTenant | Sites per tenant; 0 = tenant manages buildings directly, ≥1 = tenant → site → building (default 1). |
| Continuous | BuildingsPerTenant | Buildings per tenant (per site when using sites). |
| Continuous | FloorsPerBuilding, RoomsPerFloor | Hierarchy shape. |
| Continuous | GatewaysPerBuilding, EquipmentPerGateway, SensorsPerGateway | Gateways and devices per building. |
| Continuous | MaxBatches | Stop after this many batches (0 = run until Ctrl+C). |
| Continuous | DelayBetweenStatementsMs | Optional delay in ms after each Gremlin statement to reduce 429 throttling (0 = no delay). |
| Continuous | ContainerThroughput | RU for new container if created (default 400). |
