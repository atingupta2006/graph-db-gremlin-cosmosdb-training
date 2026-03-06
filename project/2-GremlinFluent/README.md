# GremlinFluent

Same as GremlinTraining but uses the **fluent traversal API** instead of raw Gremlin strings. You build queries in C# with `g.V().HasLabel("tenant").Count()` instead of `client.SubmitAsync("g.V().hasLabel('tenant').count()")`. Same Cosmos DB config (appsettings.json or env vars).

Uses Apache TinkerPop’s **Process.Traversal** and **DriverRemoteConnection**: the driver sends bytecode, so you get IntelliSense and compile-time structure. RU and status attributes are not exposed the same way as with string submission; this project is for the fluent style.

**Open in VS Code:** File → Open Folder → `GremlinFluent`. Run with `dotnet run` or F5. Config: copy `appsettings.Example.json` to `appsettings.json` and set Hostname and Key.

**Note:** Some Cosmos DB setups have had issues with bytecode submission (e.g. hangs). If that happens, use the **GremlinTraining** project (string-based) instead.
