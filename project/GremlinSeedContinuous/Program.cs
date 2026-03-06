using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;

// Continuous graph data load into a separate container. Same schema as GremlinSeed.
// Supports: site-level nesting (SitesPerTenant), optional delay, bookmark (resume), and cleanup prompt on startup.

static void WaitForKey()
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(); } catch (InvalidOperationException) { }
    }
}

var appDir = AppContext.BaseDirectory;
var config = new ConfigurationBuilder()
    .SetBasePath(appDir)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var hostnameRaw = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? config["CosmosDb:Hostname"]
    ?? throw new InvalidOperationException("Set CosmosDb:Hostname in appsettings.json or COSMOS_ENDPOINT env var.");

var authKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
    ?? config["CosmosDb:Key"]
    ?? throw new InvalidOperationException("Set CosmosDb:Key in appsettings.json or COSMOS_KEY env var.");

if (string.IsNullOrWhiteSpace(hostnameRaw) || string.IsNullOrWhiteSpace(authKey))
    throw new InvalidOperationException("CosmosDb:Hostname and CosmosDb:Key must be set. See appsettings.Example.json.");

var databaseName = config["CosmosDb:Database"] ?? "iot-graph-db";
var graphName = config["CosmosDb:Graph"] ?? "asset-graph-load";

var intervalSeconds = int.TryParse(config["Continuous:IntervalSeconds"], out var iv) ? Math.Max(1, iv) : 10;
var tenantsPerBatch = int.TryParse(config["Continuous:TenantsPerBatch"], out var tb) ? Math.Max(1, tb) : 1;
var sitesPerTenant = int.TryParse(config["Continuous:SitesPerTenant"], out var st) ? Math.Max(0, st) : 1;
var buildingsPerTenant = int.TryParse(config["Continuous:BuildingsPerTenant"], out var bp) ? Math.Max(1, bp) : 2;
var floorsPerBuilding = int.TryParse(config["Continuous:FloorsPerBuilding"], out var fp) ? Math.Max(1, fp) : 2;
var roomsPerFloor = int.TryParse(config["Continuous:RoomsPerFloor"], out var rp) ? Math.Max(0, rp) : 2;
var gatewaysPerBuilding = int.TryParse(config["Continuous:GatewaysPerBuilding"], out var gp) ? Math.Max(1, gp) : 2;
var equipmentPerGateway = int.TryParse(config["Continuous:EquipmentPerGateway"], out var ep) ? Math.Max(1, ep) : 2;
var sensorsPerGateway = int.TryParse(config["Continuous:SensorsPerGateway"], out var sp) ? Math.Max(1, sp) : 2;
var maxBatches = int.TryParse(config["Continuous:MaxBatches"], out var mb) ? Math.Max(0, mb) : 0;
var delayBetweenStatementsMs = int.TryParse(config["Continuous:DelayBetweenStatementsMs"], out var dm) ? Math.Max(0, dm) : 0;
var throughput = int.TryParse(config["Continuous:ContainerThroughput"], out var ru) && ru > 0 ? ru : 400;

if (string.Equals(graphName, "asset-graph", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Continuous load must use a different container than asset-graph. Set CosmosDb:Graph to e.g. asset-graph-load.");
    return 1;
}

var hostname = hostnameRaw
    .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
    .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
    .Replace(":443/", "").Replace(":443", "").TrimEnd('/');
if (hostname.Contains(".documents.azure.com", StringComparison.OrdinalIgnoreCase))
    hostname = hostname.Replace(".documents.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);
else if (!hostname.Contains(".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase) && hostname.Contains(".cosmos.azure.com", StringComparison.OrdinalIgnoreCase))
    hostname = hostname.Replace(".cosmos.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);

var accountName = hostname.Replace(".gremlin.cosmos.azure.com", "", StringComparison.OrdinalIgnoreCase);
var sqlEndpoint = $"https://{accountName}.documents.azure.com";

// Bookmark file: saved in the application directory (where the exe runs, e.g. bin/Debug/net10.0).
// Filename: gremlin-continuous-bookmark-{graphName}.txt (e.g. gremlin-continuous-bookmark-asset-graph-load.txt).
var bookmarkFile = Path.Combine(appDir, $"gremlin-continuous-bookmark-{graphName}.txt");

Console.WriteLine($"Database: {databaseName}, Target graph: {graphName}");
Console.WriteLine($"Interval: {intervalSeconds}s, Tenants/batch: {tenantsPerBatch}, Sites/tenant: {sitesPerTenant}, Max batches: {(maxBatches == 0 ? "unlimited (Ctrl+C to stop)" : maxBatches.ToString())}");
if (delayBetweenStatementsMs > 0)
    Console.WriteLine($"Delay between statements: {delayBetweenStatementsMs} ms");
Console.WriteLine();

// Ask whether to clean up (start from batch 1) or resume from last batch.
var startBatchIndex = 1;
if (!Console.IsInputRedirected)
{
    Console.Write("Clean up and start from batch 1? (y/N): ");
    var line = Console.ReadLine()?.Trim().ToUpperInvariant();
    var doCleanup = line == "Y" || line == "YES";
    if (doCleanup)
    {
        if (File.Exists(bookmarkFile))
            File.Delete(bookmarkFile);
        Console.WriteLine("Starting from batch 1 (bookmark cleared).");
    }
    else
    {
        if (File.Exists(bookmarkFile) && int.TryParse(await File.ReadAllTextAsync(bookmarkFile), out var lastBatch) && lastBatch >= 1)
        {
            startBatchIndex = lastBatch + 1;
            Console.WriteLine($"Resuming from batch {startBatchIndex} (last completed: {lastBatch}).");
        }
        else
            Console.WriteLine("No bookmark found; starting from batch 1.");
    }
}
else
    Console.WriteLine("Starting from batch 1 (non-interactive).");
Console.WriteLine();

// Create database and container if not exist
using var cosmosClient = new CosmosClient(sqlEndpoint, authKey, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
var dbResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName);
var database = dbResponse.Database;
var containerProps = new ContainerProperties(graphName, partitionKeyPath: "/pk");
ContainerResponse? containerResponse = null;
try
{
    containerResponse = await database.CreateContainerIfNotExistsAsync(containerProps, throughput);
}
catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest && ex.SubStatusCode == 1028)
{
    try
    {
        containerResponse = await database.CreateContainerIfNotExistsAsync(containerProps, throughput: null);
    }
    catch (CosmosException)
    {
        Console.WriteLine("Could not create container: account throughput limit reached. Create container manually (partition key /pk) then run again.");
        WaitForKey();
        return 1;
    }
}
if (containerResponse == null) { Console.WriteLine("Container creation failed."); WaitForKey(); return 1; }
Console.WriteLine($"Container '{graphName}' ready (partition key /pk).");
Console.WriteLine();

var username = $"/dbs/{databaseName}/colls/{graphName}";
var server = new GremlinServer(hostname, 443, true, username, authKey);
void AcceptCert(System.Net.WebSockets.ClientWebSocketOptions o) => o.RemoteCertificateValidationCallback = (_, _, _, _) => true;

GremlinClient gremlinClient;
try
{
    gremlinClient = new GremlinClient(
        server,
        new GraphSON2Reader(),
        new GraphSON2Writer(),
        mimeType: GremlinClient.GraphSON2MimeType,
        connectionPoolSettings: new ConnectionPoolSettings { PoolSize = 2 },
        webSocketConfiguration: AcceptCert);
}
catch (Exception ex)
{
    var inner = ex;
    while (inner != null) { Console.WriteLine($"Connection error: {inner.Message}"); inner = inner.InnerException; }
    WaitForKey();
    return 1;
}

var batchIndex = startBatchIndex - 1;
var totalOk = 0;
var totalFail = 0;

using (gremlinClient)
{
    Console.WriteLine("Starting continuous load. Press Ctrl+C to stop.");
    Console.WriteLine();

    while (maxBatches == 0 || batchIndex < maxBatches)
    {
        batchIndex++;
        var (queries, vertexCount, edgeCount) = GenerateBatch(
            batchIndex,
            tenantsPerBatch,
            sitesPerTenant,
            buildingsPerTenant,
            floorsPerBuilding,
            roomsPerFloor,
            gatewaysPerBuilding,
            equipmentPerGateway,
            sensorsPerGateway);

        Console.WriteLine($"[Batch {batchIndex}] Sending {queries.Count} statements (~{vertexCount} vertices, ~{edgeCount} edges)...");

        var ok = 0;
        var fail = 0;
        for (var i = 0; i < queries.Count; i++)
        {
            var q = queries[i];
            try
            {
                await gremlinClient.SubmitAsync<dynamic>(q);
                ok++;
            }
            catch (ResponseException ex)
            {
                var statusCode = (int)ex.StatusCode;
                if (ex.StatusAttributes?.TryGetValue("x-ms-status-code", out var msCode) == true && int.TryParse(msCode?.ToString(), out var cosmosCode))
                    statusCode = cosmosCode;
                if (statusCode == 409 || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                {
                    fail++;
                    if (fail <= 3) Console.WriteLine($"  Error: {ex.Message}");
                    if (statusCode == 429) Console.WriteLine("  Throttled (429). Consider increasing RU or DelayBetweenStatementsMs.");
                }
            }
            if (delayBetweenStatementsMs > 0)
                await Task.Delay(delayBetweenStatementsMs);
        }

        totalOk += ok;
        totalFail += fail;
        Console.WriteLine($"  OK: {ok}, Failed: {fail} (total OK: {totalOk}, Failed: {totalFail})");

        await File.WriteAllTextAsync(bookmarkFile, batchIndex.ToString());

        if (maxBatches > 0 && batchIndex >= maxBatches)
            break;

        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    }
}

Console.WriteLine();
Console.WriteLine($"Finished. Batches: {batchIndex}, Total OK: {totalOk}, Total Failed: {totalFail}");
WaitForKey();
return totalFail > 0 ? 1 : 0;

// ----- Data generator: same schema as GremlinSeed, with optional site level -----
// IDs: tenant-load-{batch}-{t}; with sites: unit-site-{b}-{t}-{s}, unit-bldg-{b}-{t}-{s}-{bldg}, etc.
static (List<string> Queries, int VertexCount, int EdgeCount) GenerateBatch(
    int batchIndex,
    int tenantsPerBatch,
    int sitesPerTenant,
    int buildingsPerTenant,
    int floorsPerBuilding,
    int roomsPerFloor,
    int gatewaysPerBuilding,
    int equipmentPerGateway,
    int sensorsPerGateway)
{
    var queries = new List<string>();
    var vertexCount = 0;
    var edgeCount = 0;
    var industries = new[] { "manufacturing", "energy", "retail", "healthcare", "logistics" };
    var locations = new[] { "Chicago", "Detroit", "Houston", "Austin", "Seattle" };
    var models = new[] { "IoT-Hub-3000", "IoT-Hub-5000" };
    var protocols = new[] { "mqtt", "modbus", "opcua" };
    var sensorTypes = new[] { "temperature", "pressure", "humidity", "vibration" };
    var equipTypes = new[] { "hvac", "pump", "compressor", "generator", "motor" };

    for (var t = 0; t < tenantsPerBatch; t++)
    {
        var tenantId = $"tenant-load-{batchIndex}-{t}";
        var tenantName = $"Tenant-{batchIndex}-{t}";
        var industry = industries[(batchIndex + t) % industries.Length];

        queries.Add($"g.addV('tenant').property('id', '{tenantId}').property('name', '{tenantName}').property('industry', '{industry}').property('pk', '{tenantId}')");
        vertexCount++;

        if (sitesPerTenant >= 1)
        {
            for (var s = 0; s < sitesPerTenant; s++)
            {
                var siteId = $"unit-site-{batchIndex}-{t}-{s}";
                var siteName = $"Site-{batchIndex}-{t}-{s}";
                var loc = locations[(batchIndex + t + s) % locations.Length];
                queries.Add($"g.addV('unit').property('id', '{siteId}').property('name', '{siteName}').property('type', 'site').property('location', '{loc}').property('pk', '{tenantId}')");
                queries.Add($"g.V('{tenantId}').addE('manages').to(g.V('{siteId}')).property('since', '2024-01-01')");
                vertexCount++;
                edgeCount++;

                for (var b = 0; b < buildingsPerTenant; b++)
                {
                    var bldgId = $"unit-bldg-{batchIndex}-{t}-{s}-{b}";
                    var bldgName = $"Building-{batchIndex}-{t}-{s}-{b}";
                    queries.Add($"g.addV('unit').property('id', '{bldgId}').property('name', '{bldgName}').property('type', 'building').property('location', '{loc}').property('pk', '{tenantId}')");
                    queries.Add($"g.V('{siteId}').addE('contains').to(g.V('{bldgId}')).property('relationship_type', 'structural')");
                    vertexCount++;
                    edgeCount++;

                    AddFloorsAndRooms(queries, ref vertexCount, ref edgeCount, batchIndex, t, s, b, floorsPerBuilding, roomsPerFloor, tenantId, loc);
                    AddGatewaysForBuilding(queries, ref vertexCount, ref edgeCount, batchIndex, t, s, b, gatewaysPerBuilding, equipmentPerGateway, sensorsPerGateway, tenantId, loc, models, protocols, sensorTypes, equipTypes);
                }
            }
        }
        else
        {
            for (var b = 0; b < buildingsPerTenant; b++)
            {
                var bldgId = $"unit-bldg-{batchIndex}-{t}-{b}";
                var bldgName = $"Building-{batchIndex}-{t}-{b}";
                var loc = locations[(batchIndex + t + b) % locations.Length];
                queries.Add($"g.addV('unit').property('id', '{bldgId}').property('name', '{bldgName}').property('type', 'building').property('location', '{loc}').property('pk', '{tenantId}')");
                queries.Add($"g.V('{tenantId}').addE('manages').to(g.V('{bldgId}')).property('since', '2024-01-01')");
                vertexCount++;
                edgeCount++;

                AddFloorsAndRooms(queries, ref vertexCount, ref edgeCount, batchIndex, t, -1, b, floorsPerBuilding, roomsPerFloor, tenantId, loc);
                AddGatewaysForBuilding(queries, ref vertexCount, ref edgeCount, batchIndex, t, -1, b, gatewaysPerBuilding, equipmentPerGateway, sensorsPerGateway, tenantId, loc, models, protocols, sensorTypes, equipTypes);
            }
        }
    }

    return (queries, vertexCount, edgeCount);
}

static void AddFloorsAndRooms(List<string> queries, ref int vertexCount, ref int edgeCount,
    int batchIndex, int t, int s, int b, int floorsPerBuilding, int roomsPerFloor, string tenantId, string loc)
{
    string FloorId(int f) => s >= 0 ? $"unit-floor-{batchIndex}-{t}-{s}-{b}-{f}" : $"unit-floor-{batchIndex}-{t}-{b}-{f}";
    string RoomId(int f, int r) => s >= 0 ? $"unit-room-{batchIndex}-{t}-{s}-{b}-{f}-{r}" : $"unit-room-{batchIndex}-{t}-{b}-{f}-{r}";

    for (var f = 0; f < floorsPerBuilding; f++)
    {
        var floorId = FloorId(f);
        queries.Add($"g.addV('unit').property('id', '{floorId}').property('name', 'Floor-{f + 1}').property('type', 'floor').property('location', '{loc}').property('pk', '{tenantId}')");
        var parentBldg = s >= 0 ? $"unit-bldg-{batchIndex}-{t}-{s}-{b}" : $"unit-bldg-{batchIndex}-{t}-{b}";
        queries.Add($"g.V('{parentBldg}').addE('contains').to(g.V('{floorId}')).property('relationship_type', 'structural')");
        vertexCount++;
        edgeCount++;

        for (var r = 0; r < roomsPerFloor; r++)
        {
            var roomId = RoomId(f, r);
            queries.Add($"g.addV('unit').property('id', '{roomId}').property('name', 'Room-{r + 1}').property('type', 'room').property('location', '{loc}').property('pk', '{tenantId}')");
            queries.Add($"g.V('{floorId}').addE('contains').to(g.V('{roomId}')).property('relationship_type', 'structural')");
            vertexCount++;
            edgeCount++;
        }
    }
}

static void AddGatewaysForBuilding(List<string> queries, ref int vertexCount, ref int edgeCount,
    int batchIndex, int t, int s, int b, int gatewaysPerBuilding, int equipmentPerGateway, int sensorsPerGateway,
    string tenantId, string loc, string[] models, string[] protocols, string[] sensorTypes, string[] equipTypes)
{
    string GwId(int g) => s >= 0 ? $"gw-{batchIndex}-{t}-{s}-{b}-{g}" : $"gw-{batchIndex}-{t}-{b}-{g}";
    string EquipId(int g, int e) => s >= 0 ? $"equip-{batchIndex}-{t}-{s}-{b}-{g}-{e}" : $"equip-{batchIndex}-{t}-{b}-{g}-{e}";
    string SensorId(int g, int sn) => s >= 0 ? $"sensor-{batchIndex}-{t}-{s}-{b}-{g}-{sn}" : $"sensor-{batchIndex}-{t}-{b}-{g}-{sn}";
    var parentBldg = s >= 0 ? $"unit-bldg-{batchIndex}-{t}-{s}-{b}" : $"unit-bldg-{batchIndex}-{t}-{b}";

    for (var g = 0; g < gatewaysPerBuilding; g++)
    {
        var gwId = GwId(g);
        var model = models[g % models.Length];
        var ip = $"10.{batchIndex % 256}.{t}.{g + 1}";
        var status = g % 5 == 0 ? "inactive" : "active";
        queries.Add($"g.addV('gateway').property('id', '{gwId}').property('name', 'GW-{batchIndex}-{t}-{g}').property('model', '{model}').property('status', '{status}').property('ip_address', '{ip}').property('pk', '{tenantId}')");
        queries.Add($"g.V('{parentBldg}').addE('hosts').to(g.V('{gwId}')).property('installed_date', '2023-05-01')");
        vertexCount++;
        edgeCount++;

        var equipIds = new List<string>();
        for (var e = 0; e < equipmentPerGateway; e++)
        {
            var equipId = EquipId(g, e);
            var eqType = equipTypes[e % equipTypes.Length];
            var eqName = $"{char.ToUpper(eqType[0])}{eqType.Substring(1)}-{batchIndex}-{t}-{g}-{e}";
            var run = (e + g) % 3 == 0 ? "stopped" : "running";
            equipIds.Add(equipId);
            queries.Add($"g.addV('equipment').property('id', '{equipId}').property('name', '{eqName}').property('type', '{eqType}').property('manufacturer', 'Vendor').property('install_date', '2023-06-01').property('status', '{run}').property('pk', '{tenantId}')");
            queries.Add($"g.V('{gwId}').addE('connects_to').to(g.V('{equipId}')).property('protocol', '{protocols[e % protocols.Length]}').property('signal_strength', {85 + (e + g) % 15})");
            vertexCount++;
            edgeCount++;
        }

        for (var sn = 0; sn < sensorsPerGateway; sn++)
        {
            var sensorId = SensorId(g, sn);
            var stype = sensorTypes[sn % sensorTypes.Length];
            var abbr = stype.Length >= 4 ? stype.ToUpperInvariant().Substring(0, 4) : stype.ToUpperInvariant();
            var sname = $"{abbr}-{batchIndex}-{t}-{g}-{sn}";
            var targetEquip = equipIds[sn % equipIds.Count];
            queries.Add($"g.addV('sensor').property('id', '{sensorId}').property('name', '{sname}').property('sensor_type', '{stype}').property('unit_of_measure', 'celsius').property('threshold', 80).property('status', 'active').property('pk', '{tenantId}')");
            queries.Add($"g.V('{sensorId}').addE('monitors').to(g.V('{targetEquip}')).property('attached_date', '2024-03-01').property('position', 'default')");
            queries.Add($"g.V('{sensorId}').addE('assigned_to').to(g.V('{gwId}')).property('channel', {(sn % 4) + 1})");
            vertexCount++;
            edgeCount += 2;
        }
    }
}
