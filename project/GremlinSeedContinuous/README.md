# GremlinSeedContinuous

Loads graph data into a separate Cosmos DB container at a configurable interval for performance testing. Same schema as GremlinSeed. Configure target container, interval, and batch shape in `appsettings.json`.

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

Set **CosmosDb:Hostname** and **CosmosDb:Key** in `appsettings.json` (or env vars `COSMOS_ENDPOINT`, `COSMOS_KEY`). Set **CosmosDb:Graph** to the target container (e.g. `asset-graph-load`). The app creates the container if it does not exist. Press Ctrl+C to stop.

Query the data in Data Explorer under the target graph; the schema matches the Day 4 hands-on (tenant, unit, gateway, equipment, sensor and the same edge types).
