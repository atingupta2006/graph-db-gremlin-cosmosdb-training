# GremlinTraining

Small .NET 10 console app to talk to Cosmos DB (Gremlin API). Same graph we use in the labs: iot-graph-db / asset-graph.

You need .NET 10 (`dotnet --version`) and a Cosmos account with that database and graph already created (Day 01).

**Open in VS Code:** File → Open Folder → pick the `GremlinTraining` folder. Or from the repo root: `code project/GremlinTraining`. If it asks, install the C# extension. Terminal is Ctrl+`; run `dotnet run` from the project folder, or use F5 / Ctrl+F5 to run with or without the debugger. Ctrl+Shift+B builds.

**Config:** The app reads from `appsettings.json`. Get your Gremlin URI and Primary Key from Azure Portal → Cosmos DB account → Keys. Put the hostname only in `CosmosDb:Hostname` (e.g. `your-account.gremlin.cosmos.azure.com` — no https or port). Put the key in `CosmosDb:Key`. Database and graph default to iot-graph-db and asset-graph; change them there if yours are different. You can also set `COSMOS_ENDPOINT` and `COSMOS_KEY` in the environment instead.

When you run it, it runs two queries — `g.V().limit(1)` and `g.V().hasLabel('tenant').count()` — and prints the results and RU for each. That’s it.

If you get 401, the key is wrong. 404 usually means the database or graph name is off (path is `/dbs/{db}/colls/{graph}`). 429 is throttling. Timeout is 30 seconds.

To run different Gremlin, edit the two `RunQueryAsync(...)` calls in `Program.cs` in the try block.
