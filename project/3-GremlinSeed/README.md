# GremlinSeed

Seeds **Cosmos DB Gremlin API** (db: `iot-graph-db`, graph: `asset-graph`) for labs Days 01–04. Same model: tenants, units, gateways, equipment, sensors, edges.

## Setup

- .NET 10 SDK; Cosmos DB with Gremlin API, database/graph as above, partition key `/pk`.
- Set `CosmosDb:Hostname` and `CosmosDb:Key` in `appsettings.json` (see `appsettings.Example.json`) or env vars `COSMOS_ENDPOINT`, `COSMOS_KEY`.

## Commands

```bash
cd project/GremlinSeed
dotnet build
dotnet run -- --minimal   # base data only (default)
dotnet run -- --day4      # base + Day 04 data
```

- **--minimal**: tenant-1, buildings/floors, gw-001–004, equipment, sensors, edges.
- **--day4**: adds tenant-2, plants/sections/rooms, gw-005–008, extra equipment/sensors.

Re-runs are idempotent (409 “already exists” counted as OK). After seeding, verification queries run and print counts/traversals.
