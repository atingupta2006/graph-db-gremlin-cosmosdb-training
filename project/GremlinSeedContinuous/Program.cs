using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;

// Loads graph data into a separate container at a set interval; same schema as GremlinSeed.

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
var buildingsPerTenant = int.TryParse(config["Continuous:BuildingsPerTenant"], out var bp) ? Math.Max(1, bp) : 2;
var floorsPerBuilding = int.TryParse(config["Continuous:FloorsPerBuilding"], out var fp) ? Math.Max(1, fp) : 2;
var roomsPerFloor = int.TryParse(config["Continuous:RoomsPerFloor"], out var rp) ? Math.Max(0, rp) : 2;
var gatewaysPerBuilding = int.TryParse(config["Continuous:GatewaysPerBuilding"], out var gp) ? Math.Max(1, gp) : 2;
var equipmentPerGateway = int.TryParse(config["Continuous:EquipmentPerGateway"], out var ep) ? Math.Max(1, ep) : 2;
var sensorsPerGateway = int.TryParse(config["Continuous:SensorsPerGateway"], out var sp) ? Math.Max(1, sp) : 2;
var maxBatches = int.TryParse(config["Continuous:MaxBatches"], out var mb) ? Math.Max(0, mb) : 0;

var throughput = int.TryParse(config["Continuous:ContainerThroughput"], out var ru) && ru > 0 ? ru : 400;

// Ensure we do not target the lab graph by mistake
if (string.Equals(graphName, "asset-graph", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Continuous load must use a different container than asset-graph. Set CosmosDb:Graph to e.g. asset-graph-load.");
    return 1;
}

// Normalize hostname for Gremlin
var hostname = hostnameRaw
    .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
    .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
    .Replace(":443/", "")
    .Replace(":443", "")
    .TrimEnd('/');

if (hostname.Contains(".documents.azure.com", StringComparison.OrdinalIgnoreCase))
    hostname = hostname.Replace(".documents.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);
else if (!hostname.Contains(".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase) && hostname.Contains(".cosmos.azure.com", StringComparison.OrdinalIgnoreCase))
    hostname = hostname.Replace(".cosmos.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);

// SQL endpoint for Cosmos SDK (container create)
var accountName = hostname.Replace(".gremlin.cosmos.azure.com", "", StringComparison.OrdinalIgnoreCase);
var sqlEndpoint = $"https://{accountName}.documents.azure.com";

Console.WriteLine($"Database: {databaseName}, Target graph (container): {graphName}");
Console.WriteLine($"Interval: {intervalSeconds}s, Tenants per batch: {tenantsPerBatch}, Max batches: {(maxBatches == 0 ? "unlimited (Ctrl+C to stop)" : maxBatches.ToString())}");
Console.WriteLine();

// 1) Create database and container if not exist (Cosmos SDK)
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
    // Account throughput limit (e.g. 1000 RU total); try creating container without dedicated throughput (uses shared DB throughput if set)
    try
    {
        containerResponse = await database.CreateContainerIfNotExistsAsync(containerProps, throughput: null);
    }
    catch (CosmosException)
    {
        Console.WriteLine("Could not create container: account throughput limit reached.");
        Console.WriteLine("Create the container manually in Azure Portal (asset-graph-load, partition key /pk) with shared database throughput, then run again.");
        WaitForKey();
        return 1;
    }
}
if (containerResponse == null)
{
    Console.WriteLine("Container creation failed.");
    WaitForKey();
    return 1;
}
Console.WriteLine($"Container '{graphName}' ready (partition key /pk).");
Console.WriteLine();

// 2) Gremlin client for the target graph
var username = $"/dbs/{databaseName}/colls/{graphName}";
var server = new GremlinServer(hostname, 443, true, username, authKey);

void AcceptServerCertificate(System.Net.WebSockets.ClientWebSocketOptions options)
{
    options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
}

GremlinClient gremlinClient;
try
{
    gremlinClient = new GremlinClient(
        server,
        new GraphSON2Reader(),
        new GraphSON2Writer(),
        mimeType: GremlinClient.GraphSON2MimeType,
        connectionPoolSettings: new ConnectionPoolSettings { PoolSize = 2 },
        webSocketConfiguration: AcceptServerCertificate);
}
catch (Exception ex)
{
    var inner = ex;
    while (inner != null) { Console.WriteLine($"Connection error: {inner.Message}"); inner = inner.InnerException; }
    Console.WriteLine("Check hostname, key, firewall, and Gremlin API.");
    WaitForKey();
    return 1;
}

var batchIndex = 0;
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
                if (ex.StatusAttributes?.TryGetValue("x-ms-status-code", out var msCode) == true &&
                    int.TryParse(msCode?.ToString(), out var cosmosCode))
                    statusCode = cosmosCode;
                if (statusCode == 409 || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                {
                    fail++;
                    if (fail <= 3)
                        Console.WriteLine($"  Error: {ex.Message}");
                    if (statusCode == 429)
                        Console.WriteLine("  Throttled (429). Consider increasing container RU or IntervalSeconds.");
                }
            }
        }

        totalOk += ok;
        totalFail += fail;
        Console.WriteLine($"  OK: {ok}, Failed: {fail} (total OK: {totalOk}, Failed: {totalFail})");

        if (maxBatches > 0 && batchIndex >= maxBatches)
            break;

        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    }
}

Console.WriteLine();
Console.WriteLine($"Finished. Batches: {batchIndex}, Total OK: {totalOk}, Total Failed: {totalFail}");
WaitForKey();
return totalFail > 0 ? 1 : 0;

// ----- Data generator (same schema as GremlinSeed, scaled) -----
// All data is from scratch: no reads from and no references to the existing lab graph (asset-graph).
// Vertices and edges use only IDs generated in this batch; every addE references vertices created earlier in the same batch.
static (List<string> Queries, int VertexCount, int EdgeCount) GenerateBatch(
    int batchIndex,
    int tenantsPerBatch,
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

        var buildingIds = new List<string>();
        for (var b = 0; b < buildingsPerTenant; b++)
        {
            var bldgId = $"unit-bldg-{batchIndex}-{t}-{b}";
            var bldgName = $"Building-{batchIndex}-{t}-{b}";
            var loc = locations[(batchIndex + t + b) % locations.Length];
            buildingIds.Add(bldgId);
            queries.Add($"g.addV('unit').property('id', '{bldgId}').property('name', '{bldgName}').property('type', 'building').property('location', '{loc}').property('pk', '{tenantId}')");
            queries.Add($"g.V('{tenantId}').addE('manages').to(g.V('{bldgId}')).property('since', '2024-01-01')");
            vertexCount++;
            edgeCount++;

            var floorIds = new List<string>();
            for (var f = 0; f < floorsPerBuilding; f++)
            {
                var floorId = $"unit-floor-{batchIndex}-{t}-{b}-{f}";
                floorIds.Add(floorId);
                queries.Add($"g.addV('unit').property('id', '{floorId}').property('name', 'Floor-{f + 1}').property('type', 'floor').property('location', '{loc}').property('pk', '{tenantId}')");
                queries.Add($"g.V('{bldgId}').addE('contains').to(g.V('{floorId}')).property('relationship_type', 'structural')");
                vertexCount++;
                edgeCount++;

                for (var r = 0; r < roomsPerFloor; r++)
                {
                    var roomId = $"unit-room-{batchIndex}-{t}-{b}-{f}-{r}";
                    queries.Add($"g.addV('unit').property('id', '{roomId}').property('name', 'Room-{r + 1}').property('type', 'room').property('location', '{loc}').property('pk', '{tenantId}')");
                    queries.Add($"g.V('{floorId}').addE('contains').to(g.V('{roomId}')).property('relationship_type', 'structural')");
                    vertexCount++;
                    edgeCount++;
                }
            }

            for (var g = 0; g < gatewaysPerBuilding; g++)
            {
                var gwId = $"gw-{batchIndex}-{t}-{b}-{g}";
                var model = models[g % models.Length];
                var ip = $"10.{batchIndex % 256}.{t}.{g + 1}";
                var status = g % 5 == 0 ? "inactive" : "active";
                queries.Add($"g.addV('gateway').property('id', '{gwId}').property('name', 'GW-{batchIndex}-{t}-{b}-{g}').property('model', '{model}').property('status', '{status}').property('ip_address', '{ip}').property('pk', '{tenantId}')");
                queries.Add($"g.V('{bldgId}').addE('hosts').to(g.V('{gwId}')).property('installed_date', '2023-05-01')");
                vertexCount++;
                edgeCount++;

                var equipIds = new List<string>();
                for (var e = 0; e < equipmentPerGateway; e++)
                {
                    var equipId = $"equip-{batchIndex}-{t}-{b}-{g}-{e}";
                    var eqType = equipTypes[e % equipTypes.Length];
                    var eqName = $"{char.ToUpper(eqType[0])}{eqType.Substring(1)}-{batchIndex}-{t}-{b}-{g}-{e}";
                    var run = (e + g) % 3 == 0 ? "stopped" : "running";
                    equipIds.Add(equipId);
                    queries.Add($"g.addV('equipment').property('id', '{equipId}').property('name', '{eqName}').property('type', '{eqType}').property('manufacturer', 'Vendor').property('install_date', '2023-06-01').property('status', '{run}').property('pk', '{tenantId}')");
                    queries.Add($"g.V('{gwId}').addE('connects_to').to(g.V('{equipId}')).property('protocol', '{protocols[e % protocols.Length]}').property('signal_strength', {85 + (e + g) % 15})");
                    vertexCount++;
                    edgeCount++;
                }

                for (var s = 0; s < sensorsPerGateway; s++)
                {
                    var sensorId = $"sensor-{batchIndex}-{t}-{b}-{g}-{s}";
                    var stype = sensorTypes[s % sensorTypes.Length];
                    var abbr = stype.Length >= 4 ? stype.ToUpperInvariant().Substring(0, 4) : stype.ToUpperInvariant();
                    var sname = $"{abbr}-{batchIndex}-{t}-{b}-{g}-{s}";
                    var targetEquip = equipIds[s % equipIds.Count];
                    queries.Add($"g.addV('sensor').property('id', '{sensorId}').property('name', '{sname}').property('sensor_type', '{stype}').property('unit_of_measure', 'celsius').property('threshold', 80).property('status', 'active').property('pk', '{tenantId}')");
                    queries.Add($"g.V('{sensorId}').addE('monitors').to(g.V('{targetEquip}')).property('attached_date', '2024-03-01').property('position', 'default')");
                    queries.Add($"g.V('{sensorId}').addE('assigned_to').to(g.V('{gwId}')).property('channel', {(s % 4) + 1})");
                    vertexCount++;
                    edgeCount += 2;
                }
            }
        }
    }

    return (queries, vertexCount, edgeCount);
}
