using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;

// Cosmos DB Gremlin API supports GraphSON v2 only. Gremlin.Net 3.4.x is recommended; 3.5+ has known compatibility issues with Cosmos DB.

static void WaitForKey()
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(); } catch (InvalidOperationException) { }
    }
}

// Load config from app directory (same as GremlinTraining) so it works when run from repo root
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
var graphName = config["CosmosDb:Graph"] ?? "asset-graph";

// Normalize: strip protocol and port; support both .documents.azure.com and .gremlin.cosmos.azure.com (same as GremlinFluent)
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

var username = $"/dbs/{databaseName}/colls/{graphName}";
var server = new GremlinServer(hostname, 443, true, username, authKey);

void AcceptServerCertificate(System.Net.WebSockets.ClientWebSocketOptions options)
{
    options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
}

GremlinClient client;
try
{
    client = new GremlinClient(
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
    while (inner != null)
    {
        Console.WriteLine($"Connection error: {inner.Message}");
        inner = inner.InnerException;
    }
    Console.WriteLine("Check: Hostname (no https/:443), Primary Key, database/graph names, firewall (outbound 443), and that the Cosmos DB account uses Gremlin API.");
    Console.WriteLine("For Gremlin API use the Gremlin endpoint from Azure Portal (e.g. your-account.gremlin.cosmos.azure.com), or the SQL endpoint (we convert to Gremlin).");
    WaitForKey();
    return 1;
}

var exitCode = 0;
using (client)
{
    var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--minimal";
    if (mode != "--minimal" && mode != "--day4")
    {
        Console.WriteLine("Usage: GremlinSeed [--minimal|--day4]");
        Console.WriteLine("  --minimal  Day 01-03 base data (tenant-1, units, gateways, equipment, sensors, edges)");
        Console.WriteLine("  --day4     Minimal + Day 04 data (tenant-2, plants, sections, rooms, gw 005-008, extra equipment/sensors)");
        return 0;
    }

    var queries = GetMinimalQueries();
    if (mode == "--day4")
        queries = queries.Concat(GetDay4Queries()).ToList();

    Console.WriteLine($"Running {queries.Count} Gremlin statements ({mode})...");
    var ok = 0;
    var fail = 0;
    for (var i = 0; i < queries.Count; i++)
    {
        var q = queries[i];
        var shortQ = q.Length > 80 ? q[..77] + "..." : q;
        try
        {
            await client.SubmitAsync<dynamic>(q);
            ok++;
            if ((i + 1) % 10 == 0 || i == queries.Count - 1)
                Console.WriteLine($"  [{i + 1}/{queries.Count}] OK");
        }
        catch (ResponseException ex)
        {
            var statusCode = (int)ex.StatusCode;
            if (ex.StatusAttributes?.TryGetValue("x-ms-status-code", out var msCode) == true &&
                int.TryParse(msCode?.ToString(), out var cosmosCode))
                statusCode = cosmosCode;
            var isConflict = statusCode == 409 || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

            if (isConflict)
            {
                // Already exists — idempotent: treat as success so re-runs don't fail
                ok++;
                if ((i + 1) % 10 == 0 || i == queries.Count - 1)
                    Console.WriteLine($"  [{i + 1}/{queries.Count}] OK");
            }
            else
            {
                fail++;
                Console.WriteLine($"  FAIL [{i + 1}]: {ex.Message}");
                Console.WriteLine($"    Query: {shortQ}");
                if (statusCode == 429)
                    Console.WriteLine("    (Cosmos DB throttling — wait a moment or increase RUs; then re-run.)");
            }
        }
    }

    Console.WriteLine($"Done. OK: {ok}, Failed: {fail}");
    exitCode = fail > 0 ? 1 : 0;

    // Verification: run read-only queries to check the data
    Console.WriteLine();
    Console.WriteLine("--- Verification queries ---");
    await RunVerificationQueries(client);
}
WaitForKey();
return exitCode;

static async Task RunVerificationQueries(GremlinClient client)
{
    var checks = new (string Description, string Query)[]
    {
        ("Total vertex count", "g.V().count()"),
        ("Total edge count", "g.E().count()"),
        ("Vertices by label", "g.V().groupCount().by(label).unfold().order().by(keys)"),
        ("Tenants (id, name)", "g.V().hasLabel('tenant').project('id','name').by('id').by('name')"),
        ("Units under tenant-1 (manages)", "g.V('tenant-1').out('manages').values('name')"),
        ("Units under tenant-2 (manages) [only if --day4 was run]", "g.V('tenant-2').out('manages').values('name')"),
        ("Gateways in Building-A (hosts)", "g.V('unit-bldgA').out('hosts').values('name')"),
        ("Equipment connected to gw-001", "g.V('gw-001').out('connects_to').values('name')"),
        ("Sensors assigned to gw-001", "g.V('gw-001').in('assigned_to').hasLabel('sensor').values('name')"),
        ("Sensor TEMP-001 monitors (equipment)", "g.V('sensor-temp001').out('monitors').values('name')"),
        ("Active gateways count", "g.V().hasLabel('gateway').has('status','active').count()"),
        ("Temperature sensors", "g.V().hasLabel('sensor').has('sensor_type','temperature').values('name')"),
    };

    foreach (var (description, query) in checks)
    {
        try
        {
            var resultSet = await client.SubmitAsync<dynamic>(query);
            var list = resultSet?.ToList() ?? new List<dynamic>();
            Console.WriteLine($"  {description}:");
            if (list.Count == 0)
                Console.WriteLine("    (empty)");
            else if (list.Count == 1 && list[0] is not IDictionary<string, object> && list[0] is not Newtonsoft.Json.Linq.JObject)
                Console.WriteLine($"    {FormatResult(list[0])}");
            else
                foreach (var r in list)
                    Console.WriteLine($"    {FormatResult(r)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {description}: ERROR — {ex.Message}");
        }
    }
}

static string FormatResult(dynamic r)
{
    if (r == null) return "(null)";
    if (r is IDictionary<string, object> dict)
        return string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
    if (r is Newtonsoft.Json.Linq.JObject jobj)
        return string.Join(", ", jobj.Properties().Select(p => $"{p.Name}={p.Value}"));
    if (r is Newtonsoft.Json.Linq.JValue jval)
        return jval.ToString();
    return r.ToString() ?? "(empty)";
}

static List<string> GetMinimalQueries()
{
    return new List<string>
    {
        // Vertices - tenant, units
        "g.addV('tenant').property('id', 'tenant-1').property('name', 'Acme Corp').property('industry', 'manufacturing').property('pk', 'tenant-1')",
        "g.addV('unit').property('id', 'unit-bldgA').property('name', 'Building-A').property('type', 'building').property('location', 'Chicago').property('pk', 'tenant-1')",
        "g.addV('unit').property('id', 'unit-bldgB').property('name', 'Building-B').property('type', 'building').property('location', 'Detroit').property('pk', 'tenant-1')",
        "g.addV('unit').property('id', 'unit-floor1').property('name', 'Floor-1').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')",
        "g.addV('unit').property('id', 'unit-floor2').property('name', 'Floor-2').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')",
        // Gateways
        "g.addV('gateway').property('id', 'gw-001').property('name', 'GW-001').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.1').property('pk', 'tenant-1')",
        "g.addV('gateway').property('id', 'gw-002').property('name', 'GW-002').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.2').property('pk', 'tenant-1')",
        "g.addV('gateway').property('id', 'gw-003').property('name', 'GW-003').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.2.1').property('pk', 'tenant-1')",
        "g.addV('gateway').property('id', 'gw-004').property('name', 'GW-004').property('model', 'IoT-Hub-5000').property('status', 'inactive').property('ip_address', '10.0.2.2').property('pk', 'tenant-1')",
        // Equipment
        "g.addV('equipment').property('id', 'equip-hvac101').property('name', 'HVAC-101').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-15').property('status', 'running').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-hvac102').property('name', 'HVAC-102').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-20').property('status', 'running').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-pump201').property('name', 'Pump-201').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-10').property('status', 'running').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-pump202').property('name', 'Pump-202').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-12').property('status', 'stopped').property('pk', 'tenant-1')",
        // Sensors
        "g.addV('sensor').property('id', 'sensor-temp001').property('name', 'TEMP-001').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 85).property('status', 'active').property('pk', 'tenant-1')",
        "g.addV('sensor').property('id', 'sensor-temp002').property('name', 'TEMP-002').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 90).property('status', 'active').property('pk', 'tenant-1')",
        "g.addV('sensor').property('id', 'sensor-pres001').property('name', 'PRES-001').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('pk', 'tenant-1')",
        "g.addV('sensor').property('id', 'sensor-pres002').property('name', 'PRES-002').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 160).property('status', 'active').property('pk', 'tenant-1')",
        // Edges - manages, contains
        "g.V('tenant-1').addE('manages').to(g.V('unit-bldgA')).property('since', '2024-01-15')",
        "g.V('tenant-1').addE('manages').to(g.V('unit-bldgB')).property('since', '2024-03-01')",
        "g.V('unit-bldgA').addE('contains').to(g.V('unit-floor1')).property('relationship_type', 'structural')",
        "g.V('unit-bldgA').addE('contains').to(g.V('unit-floor2')).property('relationship_type', 'structural')",
        // hosts
        "g.V('unit-bldgA').addE('hosts').to(g.V('gw-001')).property('installed_date', '2023-05-01')",
        "g.V('unit-bldgA').addE('hosts').to(g.V('gw-002')).property('installed_date', '2023-05-01')",
        "g.V('unit-bldgB').addE('hosts').to(g.V('gw-003')).property('installed_date', '2023-07-15')",
        "g.V('unit-bldgB').addE('hosts').to(g.V('gw-004')).property('installed_date', '2023-07-15')",
        // connects_to
        "g.V('gw-001').addE('connects_to').to(g.V('equip-hvac101')).property('protocol', 'mqtt').property('signal_strength', 95)",
        "g.V('gw-001').addE('connects_to').to(g.V('equip-pump201')).property('protocol', 'modbus').property('signal_strength', 88)",
        "g.V('gw-002').addE('connects_to').to(g.V('equip-hvac102')).property('protocol', 'mqtt').property('signal_strength', 92)",
        "g.V('gw-002').addE('connects_to').to(g.V('equip-pump202')).property('protocol', 'modbus').property('signal_strength', 85)",
        // monitors, assigned_to
        "g.V('sensor-temp001').addE('monitors').to(g.V('equip-hvac101')).property('attached_date', '2024-03-01').property('position', 'intake')",
        "g.V('sensor-temp001').addE('assigned_to').to(g.V('gw-001')).property('channel', 1)",
        "g.V('sensor-temp002').addE('monitors').to(g.V('equip-hvac102')).property('attached_date', '2024-03-05').property('position', 'exhaust')",
        "g.V('sensor-temp002').addE('assigned_to').to(g.V('gw-002')).property('channel', 1)",
        "g.V('sensor-pres001').addE('monitors').to(g.V('equip-pump201')).property('attached_date', '2024-04-01').property('position', 'outlet')",
        "g.V('sensor-pres001').addE('assigned_to').to(g.V('gw-001')).property('channel', 2)",
        "g.V('sensor-pres002').addE('monitors').to(g.V('equip-pump202')).property('attached_date', '2024-04-05').property('position', 'inlet')",
        "g.V('sensor-pres002').addE('assigned_to').to(g.V('gw-002')).property('channel', 2)",
    };
}

static List<string> GetDay4Queries()
{
    return new List<string>
    {
        // Lab 1 - tenant-2, units
        "g.addV('tenant').property('id', 'tenant-2').property('name', 'GlobalTech').property('industry', 'energy').property('pk', 'tenant-2')",
        "g.addV('unit').property('id', 'unit-plant1').property('name', 'Plant-1').property('type', 'plant').property('location', 'Houston').property('pk', 'tenant-2')",
        "g.addV('unit').property('id', 'unit-plant2').property('name', 'Plant-2').property('type', 'plant').property('location', 'Austin').property('pk', 'tenant-2')",
        "g.addV('unit').property('id', 'unit-secA').property('name', 'Section-A').property('type', 'section').property('location', 'Houston').property('pk', 'tenant-2')",
        "g.addV('unit').property('id', 'unit-secB').property('name', 'Section-B').property('type', 'section').property('location', 'Houston').property('pk', 'tenant-2')",
        "g.V('tenant-2').addE('manages').to(g.V('unit-plant1')).property('since', '2024-02-01')",
        "g.V('tenant-2').addE('manages').to(g.V('unit-plant2')).property('since', '2024-02-01')",
        "g.V('unit-plant1').addE('contains').to(g.V('unit-secA')).property('relationship_type', 'structural')",
        "g.V('unit-plant1').addE('contains').to(g.V('unit-secB')).property('relationship_type', 'structural')",
        "g.addV('unit').property('id', 'unit-room101').property('name', 'Room-101').property('type', 'room').property('location', 'Chicago').property('pk', 'tenant-1')",
        "g.addV('unit').property('id', 'unit-room102').property('name', 'Room-102').property('type', 'room').property('location', 'Chicago').property('pk', 'tenant-1')",
        "g.V('unit-floor1').addE('contains').to(g.V('unit-room101')).property('relationship_type', 'structural')",
        "g.V('unit-floor2').addE('contains').to(g.V('unit-room102')).property('relationship_type', 'structural')",
        // Lab 2 - gateways 005-008
        "g.addV('gateway').property('id', 'gw-005').property('name', 'GW-005').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.3.1').property('pk', 'tenant-2')",
        "g.addV('gateway').property('id', 'gw-006').property('name', 'GW-006').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.3.2').property('pk', 'tenant-2')",
        "g.addV('gateway').property('id', 'gw-007').property('name', 'GW-007').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.4.1').property('pk', 'tenant-2')",
        "g.addV('gateway').property('id', 'gw-008').property('name', 'GW-008').property('model', 'IoT-Hub-3000').property('status', 'inactive').property('ip_address', '10.0.4.2').property('pk', 'tenant-2')",
        // Acme equipment
        "g.addV('equipment').property('id', 'equip-comp301').property('name', 'Compressor-301').property('type', 'compressor').property('manufacturer', 'Atlas Copco').property('install_date', '2023-09-01').property('status', 'running').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-gen401').property('name', 'Generator-401').property('type', 'generator').property('manufacturer', 'Caterpillar').property('install_date', '2023-10-15').property('status', 'running').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-boiler501').property('name', 'Boiler-501').property('type', 'boiler').property('manufacturer', 'Cleaver-Brooks').property('install_date', '2023-11-01').property('status', 'stopped').property('pk', 'tenant-1')",
        "g.addV('equipment').property('id', 'equip-motor601').property('name', 'Motor-601').property('type', 'motor').property('manufacturer', 'Siemens').property('install_date', '2024-01-10').property('status', 'running').property('pk', 'tenant-1')",
        // GlobalTech equipment
        "g.addV('equipment').property('id', 'equip-turbine301').property('name', 'Turbine-301').property('type', 'turbine').property('manufacturer', 'GE').property('install_date', '2024-02-15').property('status', 'running').property('pk', 'tenant-2')",
        "g.addV('equipment').property('id', 'equip-turbine302').property('name', 'Turbine-302').property('type', 'turbine').property('manufacturer', 'GE').property('install_date', '2024-02-20').property('status', 'running').property('pk', 'tenant-2')",
        "g.addV('equipment').property('id', 'equip-comp501').property('name', 'Compressor-501').property('type', 'compressor').property('manufacturer', 'Atlas Copco').property('install_date', '2024-03-01').property('status', 'running').property('pk', 'tenant-2')",
        "g.addV('equipment').property('id', 'equip-gen601').property('name', 'Generator-601').property('type', 'generator').property('manufacturer', 'Caterpillar').property('install_date', '2024-03-10').property('status', 'stopped').property('pk', 'tenant-2')",
        // hosts (plants -> gateways)
        "g.V('unit-plant1').addE('hosts').to(g.V('gw-005')).property('installed_date', '2024-02-10')",
        "g.V('unit-plant1').addE('hosts').to(g.V('gw-006')).property('installed_date', '2024-02-10')",
        "g.V('unit-plant2').addE('hosts').to(g.V('gw-007')).property('installed_date', '2024-03-01')",
        "g.V('unit-plant2').addE('hosts').to(g.V('gw-008')).property('installed_date', '2024-03-01')",
        // connects_to (gateways -> equipment)
        "g.V('gw-005').addE('connects_to').to(g.V('equip-turbine301')).property('protocol', 'opcua').property('signal_strength', 97)",
        "g.V('gw-006').addE('connects_to').to(g.V('equip-turbine302')).property('protocol', 'opcua').property('signal_strength', 94)",
        "g.V('gw-007').addE('connects_to').to(g.V('equip-comp501')).property('protocol', 'mqtt').property('signal_strength', 91)",
        "g.V('gw-008').addE('connects_to').to(g.V('equip-gen601')).property('protocol', 'mqtt').property('signal_strength', 86)",
        // Lab 3 - sensors and edges
        "g.addV('sensor').property('id', 'sensor-temp003').property('name', 'TEMP-003').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 80).property('status', 'active').property('pk', 'tenant-1')",
        "g.addV('sensor').property('id', 'sensor-pres003').property('name', 'PRES-003').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('pk', 'tenant-1')",
        "g.addV('sensor').property('id', 'sensor-hum001').property('name', 'HUM-001').property('sensor_type', 'humidity').property('unit_of_measure', 'percent').property('threshold', 70).property('status', 'active').property('pk', 'tenant-1')",
        "g.V('sensor-temp003').addE('monitors').to(g.V('equip-comp301')).property('attached_date', '2024-04-10').property('position', 'housing')",
        "g.V('sensor-temp003').addE('assigned_to').to(g.V('gw-003')).property('channel', 1)",
        "g.V('sensor-pres003').addE('monitors').to(g.V('equip-comp301')).property('attached_date', '2024-04-10').property('position', 'outlet')",
        "g.V('sensor-pres003').addE('assigned_to').to(g.V('gw-003')).property('channel', 2)",
        "g.V('sensor-hum001').addE('monitors').to(g.V('equip-hvac101')).property('attached_date', '2024-04-15').property('position', 'zone')",
        "g.V('sensor-hum001').addE('assigned_to').to(g.V('gw-001')).property('channel', 3)",
    };
}
