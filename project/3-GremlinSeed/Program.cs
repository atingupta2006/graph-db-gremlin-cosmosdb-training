// =============================================================================
// GREMLIN SEED — Loads the full IoT graph (both tenants) into Cosmos DB
// =============================================================================
// What this app does: (1) Clears the graph, (2) Inserts all vertices and edges
// for tenant-1 (Acme) and tenant-2 (GlobalTech), (3) Runs verification queries.
// Run from repo root: dotnet run --project project/3-GremlinSeed
//
// .NET notes (if you know Python):
//   - "using X;" = import (like Python's import). We use Gremlin driver and config.
//   - Code below is "top-level statements" — no explicit Main() method; it runs from top to bottom.
//   - "var" = type is inferred (like not writing type in Python).
//   - "await" = wait for async operation (similar to Python async/await).
//   - "static" method = belongs to the program, not an object (like a module-level function).
//   - "ok++" or "fail++" = add 1 to that variable (same as ok += 1 in Python). "++" is the increment operator.
//
// --- Three things that often confuse beginners ---
//
//  1) EXIT CODE (return 0 or return 1)
//     When your program finishes, it tells the operating system (or the terminal) a number:
//       - 0 = success (like sys.exit(0) in Python)
//       - non-zero (e.g. 1) = something went wrong
//     Scripts and CI can check this: "if the exit code is not 0, the run failed."
//
//  2) using (client) { ... }
//     This is NOT the same as "using Gremlin.Net.Driver;" at the top!
//     Here "using" means: "use this client only inside the braces; when we leave the
//     block (normally or by exception), automatically close/clean up the client."
//     In Python you'd write:  with client: ...  (context manager). Same idea.
//
//  3) WaitForKey()
//     A console app that finishes immediately would close the window so fast you
//     couldn't read the output. WaitForKey() prints "Press any key to exit..." and
//     waits until you press a key. So you can read the result before the app exits.
//     We skip it when input is "redirected" (e.g. running in a script), so the app
//     doesn't hang waiting for a key that nobody will press.
//
// =============================================================================

using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;

// ─── Main (entry point) ─────────────────────────────────────────────────────
// Step 1: Load Cosmos DB settings from appsettings.json or environment variables.
//         (hostname, key, databaseName, graphName) is a "tuple" — like returning multiple values in Python.
var (hostname, key, databaseName, graphName) = LoadCosmosConfig();

// Step 2: Create a Gremlin client. Returns null if connection fails (e.g. wrong key or firewall).
//         "GremlinClient?" means "GremlinClient or null" — the ? is .NET's nullable type.
var client = CreateGremlinClient(hostname, databaseName, graphName, key);
if (client == null)
{
    WaitForKey();   // So you can read the error message before the window closes.
    return 1;       // Exit code 1 = "failed" (tells OS/terminal that something went wrong).
}

// Step 3: Clear graph, seed data, run verification.
// "using (client)" = use client only inside this block; when we exit the block (even by error),
// .NET automatically disposes the client (closes connections). Same idea as Python: with client: ...
int exitCode;
using (client)
{
    await ClearGraphAsync(client);                    // Delete all existing vertices/edges.
    var (ok, fail) = await SeedGraphAsync(client);   // Send all Gremlin insert statements.
    Console.WriteLine($"Done. OK: {ok}, Failed: {fail}");  // $"" = string interpolation (like f"" in Python).
    Console.WriteLine();
    Console.WriteLine("--- Verification ---");
    await RunVerificationAsync(client);               // Run read-only queries to confirm data.
    exitCode = fail > 0 ? 1 : 0;   // Any failed statement → exit code 1 (failure), else 0 (success).
}

// Before exiting: wait for a key press so you can read the output (see "WaitForKey" in the header comments).
WaitForKey();
return exitCode;   // This number is what the OS/terminal sees (0 = success, 1 = failure).

// ─── Configuration ───────────────────────────────────────────────────────────
// Read Cosmos DB connection settings. We look in: (1) environment variables, (2) appsettings.json.
// Return type is a "tuple" of four strings — in Python you'd return (a, b, c, d).

static (string Hostname, string Key, string Database, string Graph) LoadCosmosConfig()
{
    // AppContext.BaseDirectory = folder where the .exe runs (so we find appsettings.json there).
    var appDir = AppContext.BaseDirectory;
    var config = new ConfigurationBuilder()
        .SetBasePath(appDir)
        .AddJsonFile("appsettings.json", optional: false)  // optional: false = file must exist.
        .Build();

    // ?? means "if left side is null, use right side". So: try env var first, then config, else throw.
    // Like: hostname_raw = os.environ.get("COSMOS_ENDPOINT") or config["CosmosDb:Hostname"] or raise ...
    var hostnameRaw = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
        ?? config["CosmosDb:Hostname"]
        ?? throw new InvalidOperationException("Set CosmosDb:Hostname in appsettings.json or COSMOS_ENDPOINT.");

    var key = Environment.GetEnvironmentVariable("COSMOS_KEY")
        ?? config["CosmosDb:Key"]
        ?? throw new InvalidOperationException("Set CosmosDb:Key in appsettings.json or COSMOS_KEY.");

    if (string.IsNullOrWhiteSpace(hostnameRaw) || string.IsNullOrWhiteSpace(key))
        throw new InvalidOperationException("CosmosDb:Hostname and Key are required. See appsettings.Example.json.");

    // Default database and graph names if not set in config.
    var database = config["CosmosDb:Database"] ?? "iot-graph-db";
    var graph = config["CosmosDb:Graph"] ?? "asset-graph";
    var hostname = NormalizeGremlinHostname(hostnameRaw);

    return (hostname, key, database, graph);
}

// Convert user input to the hostname format Cosmos Gremlin expects (no https, correct domain).
static string NormalizeGremlinHostname(string raw)
{
    var h = raw
        .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
        .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
        .Replace(":443/", "").Replace(":443", "")
        .TrimEnd('/');

    if (h.Contains(".documents.azure.com", StringComparison.OrdinalIgnoreCase))
        h = h.Replace(".documents.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);
    else if (!h.Contains(".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase) &&
             h.Contains(".cosmos.azure.com", StringComparison.OrdinalIgnoreCase))
        h = h.Replace(".cosmos.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);

    return h;
}

// ─── Connection ──────────────────────────────────────────────────────────────
// Build the Gremlin client that talks to Cosmos DB. Returns null on failure so caller can exit cleanly.

static GremlinClient? CreateGremlinClient(string hostname, string databaseName, string graphName, string authKey)
{
    // Cosmos DB expects username in the form /dbs/<db>/colls/<graph>.
    var username = $"/dbs/{databaseName}/colls/{graphName}";
    var server = new GremlinServer(hostname, 443, true, username, authKey);

    // Local function: accept any SSL certificate (useful in dev/training; in production you'd validate).
    void AcceptServerCertificate(System.Net.WebSockets.ClientWebSocketOptions opts)
    {
        opts.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    }

    try
    {
        return new GremlinClient(
            server,
            new GraphSON2Reader(),
            new GraphSON2Writer(),
            mimeType: GremlinClient.GraphSON2MimeType,
            connectionPoolSettings: new ConnectionPoolSettings { PoolSize = 2 },
            webSocketConfiguration: AcceptServerCertificate);
    }
    catch (Exception ex)
    {
        PrintConnectionError(ex);
        return null;
    }
}

// Walk the chain of inner exceptions and print each message (helps debug connection/SSL issues).
static void PrintConnectionError(Exception ex)
{
    var inner = ex;
    while (inner != null)
    {
        Console.WriteLine($"Connection error: {inner.Message}");
        inner = inner.InnerException;
    }
    Console.WriteLine("Check: Hostname (no https/:443), Key, database/graph names, firewall (443), Gremlin API.");
}

// ─── Clear and seed ─────────────────────────────────────────────────────────
// "async Task" = this method is asynchronous (like async def in Python). We await I/O so the app doesn't block.

static async Task ClearGraphAsync(GremlinClient client)
{
    Console.WriteLine("Clearing graph (dropping all vertices and edges)...");
    try
    {
        await client.SubmitAsync<dynamic>("g.V().drop()");
        Console.WriteLine("Graph cleared.");
    }
    catch (ResponseException ex)
    {
        Console.WriteLine($"Warning during clear: {ex.Message}");
    }
}

// Send all Gremlin insert statements. Returns (success count, failure count).
// SubmitAsync<dynamic> = send a Gremlin string and get results; <dynamic> means "result type can be anything".
static async Task<(int Ok, int Fail)> SeedGraphAsync(GremlinClient client)
{
    var queries = GetSeedQueries();
    Console.WriteLine($"Seeding {queries.Count} statements (both tenants)...");

    // Normal integer variables used as counters. "int ok = 0, fail = 0" = two counters starting at zero.
    // "ok++" means "add 1 to ok" (like ok += 1 in Python). "fail++" means "add 1 to fail".
    int ok = 0, fail = 0;
    for (var i = 0; i < queries.Count; i++)   // "i++" in the for loop also means "add 1 to i" each time around.
    {
        try
        {
            await client.SubmitAsync<dynamic>(queries[i]);
            ok++;   // This query succeeded → count it as one more success.
        }
        catch (ResponseException ex)
        {
            // Cosmos may put HTTP status in StatusAttributes (e.g. 429 = throttling, 409 = conflict).
            var statusCode = (int)ex.StatusCode;
            if (ex.StatusAttributes?.TryGetValue("x-ms-status-code", out var msCode) == true &&
                int.TryParse(msCode?.ToString(), out var cosmosCode))
                statusCode = cosmosCode;

            // 409 = already exists; we treat it as success so re-runs don't fail.
            if (statusCode == 409 || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                ok++;   // "Already exists" is OK for our purpose → count as success.
            else
            {
                fail++;   // Real failure → count it so we can report "Failed: N" and set exit code to 1.
                var preview = queries[i].Length > 80 ? queries[i][..77] + "..." : queries[i];  // [..77] = slice first 77 chars.
                Console.WriteLine($"  FAIL [{i + 1}]: {ex.Message}");
                Console.WriteLine($"    {preview}");
                if (statusCode == 429)
                    Console.WriteLine("    (Throttled — increase RUs or wait, then re-run.)");
            }
        }

        // Progress every 10 statements.
        if ((i + 1) % 10 == 0 || i == queries.Count - 1)
            Console.WriteLine($"  [{i + 1}/{queries.Count}] OK");
    }

    return (ok, fail);
}

// ─── Verification ──────────────────────────────────────────────────────────
// Run read-only Gremlin queries to confirm the data we just inserted (counts, sample traversals).

static async Task RunVerificationAsync(GremlinClient client)
{
    // Array of (description, Gremlin query). We run each and print the result.
    var checks = new (string Description, string Query)[]
    {
        ("Total vertex count", "g.V().count()"),
        ("Total edge count", "g.E().count()"),
        ("Vertices by label", "g.V().groupCount().by(label).unfold().order().by(keys)"),
        ("Tenants (id, name)", "g.V().hasLabel('tenant').project('id','name').by('id').by('name')"),
        ("Units under tenant-1 (manages)", "g.V('tenant-1').out('manages').values('name')"),
        ("Units under tenant-2 (manages)", "g.V('tenant-2').out('manages').values('name')"),
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
            // resultSet?.ToList() = "call ToList() if resultSet is not null, else null". ?? new List = use empty list if null.
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

// Turn a single result (number, dict, JSON object, etc.) into a short string for console output.
static string FormatResult(dynamic r)
{
    if (r == null) return "(null)";
    // "r is Type variable" = pattern matching: if r is this type, bind to variable and use it.
    if (r is IDictionary<string, object> dict)
        return string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
    if (r is Newtonsoft.Json.Linq.JObject jobj)
        return string.Join(", ", jobj.Properties().Select(p => $"{p.Name}={p.Value}"));
    if (r is Newtonsoft.Json.Linq.JValue jval)
        return jval.ToString();
    return r.ToString() ?? "(empty)";
}

// ─── Seed data (both tenants) ───────────────────────────────────────────────
// Each "yield return" adds one Gremlin string to the sequence. Like a Python generator:
//   def get_tenant1_queries():
//       yield "g.addV(...)"
//       yield "g.V(...).addE(...)"

static List<string> GetSeedQueries()
{
    var list = new List<string>();
    list.AddRange(GetTenant1Queries());   // AddRange = add all items from the other collection (like list.extend in Python).
    list.AddRange(GetTenant2Queries());
    return list;
}

// Tenant 1 = Acme. Vertices first (tenant, units, gateways, equipment, sensors), then edges (manages, contains, hosts, etc.).
static IEnumerable<string> GetTenant1Queries()
{
    // Acme: vertices (tenant, units, gateways, equipment, sensors)
    yield return "g.addV('tenant').property('id', 'tenant-1').property('name', 'Acme Corp').property('industry', 'manufacturing').property('pk', 'tenant-1')";
    yield return "g.addV('unit').property('id', 'unit-bldgA').property('name', 'Building-A').property('type', 'building').property('location', 'Chicago').property('pk', 'tenant-1')";
    yield return "g.addV('unit').property('id', 'unit-bldgB').property('name', 'Building-B').property('type', 'building').property('location', 'Detroit').property('pk', 'tenant-1')";
    yield return "g.addV('unit').property('id', 'unit-floor1').property('name', 'Floor-1').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')";
    yield return "g.addV('unit').property('id', 'unit-floor2').property('name', 'Floor-2').property('type', 'floor').property('location', 'Chicago').property('pk', 'tenant-1')";
    yield return "g.addV('gateway').property('id', 'gw-001').property('name', 'GW-001').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.1').property('pk', 'tenant-1')";
    yield return "g.addV('gateway').property('id', 'gw-002').property('name', 'GW-002').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.1.2').property('pk', 'tenant-1')";
    yield return "g.addV('gateway').property('id', 'gw-003').property('name', 'GW-003').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.2.1').property('pk', 'tenant-1')";
    yield return "g.addV('gateway').property('id', 'gw-004').property('name', 'GW-004').property('model', 'IoT-Hub-5000').property('status', 'inactive').property('ip_address', '10.0.2.2').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-hvac101').property('name', 'HVAC-101').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-15').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-hvac102').property('name', 'HVAC-102').property('type', 'hvac').property('manufacturer', 'Carrier').property('install_date', '2023-06-20').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-pump201').property('name', 'Pump-201').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-10').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-pump202').property('name', 'Pump-202').property('type', 'pump').property('manufacturer', 'Grundfos').property('install_date', '2023-08-12').property('status', 'stopped').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-temp001').property('name', 'TEMP-001').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 85).property('status', 'active').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-temp002').property('name', 'TEMP-002').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 90).property('status', 'active').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-pres001').property('name', 'PRES-001').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-pres002').property('name', 'PRES-002').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 160).property('status', 'active').property('pk', 'tenant-1')";
    // Acme: edges (manages, contains, hosts, connects_to, monitors, assigned_to)
    yield return "g.V('tenant-1').addE('manages').to(g.V('unit-bldgA')).property('since', '2024-01-15')";
    yield return "g.V('tenant-1').addE('manages').to(g.V('unit-bldgB')).property('since', '2024-03-01')";
    yield return "g.V('unit-bldgA').addE('contains').to(g.V('unit-floor1')).property('relationship_type', 'structural')";
    yield return "g.V('unit-bldgA').addE('contains').to(g.V('unit-floor2')).property('relationship_type', 'structural')";
    yield return "g.V('unit-bldgA').addE('hosts').to(g.V('gw-001')).property('installed_date', '2023-05-01')";
    yield return "g.V('unit-bldgA').addE('hosts').to(g.V('gw-002')).property('installed_date', '2023-05-01')";
    yield return "g.V('unit-bldgB').addE('hosts').to(g.V('gw-003')).property('installed_date', '2023-07-15')";
    yield return "g.V('unit-bldgB').addE('hosts').to(g.V('gw-004')).property('installed_date', '2023-07-15')";
    yield return "g.V('gw-001').addE('connects_to').to(g.V('equip-hvac101')).property('protocol', 'mqtt').property('signal_strength', 95)";
    yield return "g.V('gw-001').addE('connects_to').to(g.V('equip-pump201')).property('protocol', 'modbus').property('signal_strength', 88)";
    yield return "g.V('gw-002').addE('connects_to').to(g.V('equip-hvac102')).property('protocol', 'mqtt').property('signal_strength', 92)";
    yield return "g.V('gw-002').addE('connects_to').to(g.V('equip-pump202')).property('protocol', 'modbus').property('signal_strength', 85)";
    yield return "g.V('sensor-temp001').addE('monitors').to(g.V('equip-hvac101')).property('attached_date', '2024-03-01').property('position', 'intake')";
    yield return "g.V('sensor-temp001').addE('assigned_to').to(g.V('gw-001')).property('channel', 1)";
    yield return "g.V('sensor-temp002').addE('monitors').to(g.V('equip-hvac102')).property('attached_date', '2024-03-05').property('position', 'exhaust')";
    yield return "g.V('sensor-temp002').addE('assigned_to').to(g.V('gw-002')).property('channel', 1)";
    yield return "g.V('sensor-pres001').addE('monitors').to(g.V('equip-pump201')).property('attached_date', '2024-04-01').property('position', 'outlet')";
    yield return "g.V('sensor-pres001').addE('assigned_to').to(g.V('gw-001')).property('channel', 2)";
    yield return "g.V('sensor-pres002').addE('monitors').to(g.V('equip-pump202')).property('attached_date', '2024-04-05').property('position', 'inlet')";
    yield return "g.V('sensor-pres002').addE('assigned_to').to(g.V('gw-002')).property('channel', 2)";
}

// Tenant 2 = GlobalTech. Same pattern: vertices then edges. Some units (rooms) belong to tenant-1 for hierarchy depth.
static IEnumerable<string> GetTenant2Queries()
{
    // GlobalTech: vertices
    yield return "g.addV('tenant').property('id', 'tenant-2').property('name', 'GlobalTech').property('industry', 'energy').property('pk', 'tenant-2')";
    yield return "g.addV('unit').property('id', 'unit-plant1').property('name', 'Plant-1').property('type', 'plant').property('location', 'Houston').property('pk', 'tenant-2')";
    yield return "g.addV('unit').property('id', 'unit-plant2').property('name', 'Plant-2').property('type', 'plant').property('location', 'Austin').property('pk', 'tenant-2')";
    yield return "g.addV('unit').property('id', 'unit-secA').property('name', 'Section-A').property('type', 'section').property('location', 'Houston').property('pk', 'tenant-2')";
    yield return "g.addV('unit').property('id', 'unit-secB').property('name', 'Section-B').property('type', 'section').property('location', 'Houston').property('pk', 'tenant-2')";
    yield return "g.addV('unit').property('id', 'unit-room101').property('name', 'Room-101').property('type', 'room').property('location', 'Chicago').property('pk', 'tenant-1')";
    yield return "g.addV('unit').property('id', 'unit-room102').property('name', 'Room-102').property('type', 'room').property('location', 'Chicago').property('pk', 'tenant-1')";
    yield return "g.addV('gateway').property('id', 'gw-005').property('name', 'GW-005').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.3.1').property('pk', 'tenant-2')";
    yield return "g.addV('gateway').property('id', 'gw-006').property('name', 'GW-006').property('model', 'IoT-Hub-5000').property('status', 'active').property('ip_address', '10.0.3.2').property('pk', 'tenant-2')";
    yield return "g.addV('gateway').property('id', 'gw-007').property('name', 'GW-007').property('model', 'IoT-Hub-3000').property('status', 'active').property('ip_address', '10.0.4.1').property('pk', 'tenant-2')";
    yield return "g.addV('gateway').property('id', 'gw-008').property('name', 'GW-008').property('model', 'IoT-Hub-3000').property('status', 'inactive').property('ip_address', '10.0.4.2').property('pk', 'tenant-2')";
    yield return "g.addV('equipment').property('id', 'equip-comp301').property('name', 'Compressor-301').property('type', 'compressor').property('manufacturer', 'Atlas Copco').property('install_date', '2023-09-01').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-gen401').property('name', 'Generator-401').property('type', 'generator').property('manufacturer', 'Caterpillar').property('install_date', '2023-10-15').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-boiler501').property('name', 'Boiler-501').property('type', 'boiler').property('manufacturer', 'Cleaver-Brooks').property('install_date', '2023-11-01').property('status', 'stopped').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-motor601').property('name', 'Motor-601').property('type', 'motor').property('manufacturer', 'Siemens').property('install_date', '2024-01-10').property('status', 'running').property('pk', 'tenant-1')";
    yield return "g.addV('equipment').property('id', 'equip-turbine301').property('name', 'Turbine-301').property('type', 'turbine').property('manufacturer', 'GE').property('install_date', '2024-02-15').property('status', 'running').property('pk', 'tenant-2')";
    yield return "g.addV('equipment').property('id', 'equip-turbine302').property('name', 'Turbine-302').property('type', 'turbine').property('manufacturer', 'GE').property('install_date', '2024-02-20').property('status', 'running').property('pk', 'tenant-2')";
    yield return "g.addV('equipment').property('id', 'equip-comp501').property('name', 'Compressor-501').property('type', 'compressor').property('manufacturer', 'Atlas Copco').property('install_date', '2024-03-01').property('status', 'running').property('pk', 'tenant-2')";
    yield return "g.addV('equipment').property('id', 'equip-gen601').property('name', 'Generator-601').property('type', 'generator').property('manufacturer', 'Caterpillar').property('install_date', '2024-03-10').property('status', 'stopped').property('pk', 'tenant-2')";
    yield return "g.addV('sensor').property('id', 'sensor-temp003').property('name', 'TEMP-003').property('sensor_type', 'temperature').property('unit_of_measure', 'celsius').property('threshold', 80).property('status', 'active').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-pres003').property('name', 'PRES-003').property('sensor_type', 'pressure').property('unit_of_measure', 'psi').property('threshold', 150).property('status', 'active').property('pk', 'tenant-1')";
    yield return "g.addV('sensor').property('id', 'sensor-hum001').property('name', 'HUM-001').property('sensor_type', 'humidity').property('unit_of_measure', 'percent').property('threshold', 70).property('status', 'active').property('pk', 'tenant-1')";
    // GlobalTech: edges
    yield return "g.V('tenant-2').addE('manages').to(g.V('unit-plant1')).property('since', '2024-02-01')";
    yield return "g.V('tenant-2').addE('manages').to(g.V('unit-plant2')).property('since', '2024-02-01')";
    yield return "g.V('unit-plant1').addE('contains').to(g.V('unit-secA')).property('relationship_type', 'structural')";
    yield return "g.V('unit-plant1').addE('contains').to(g.V('unit-secB')).property('relationship_type', 'structural')";
    yield return "g.V('unit-floor1').addE('contains').to(g.V('unit-room101')).property('relationship_type', 'structural')";
    yield return "g.V('unit-floor2').addE('contains').to(g.V('unit-room102')).property('relationship_type', 'structural')";
    yield return "g.V('unit-plant1').addE('hosts').to(g.V('gw-005')).property('installed_date', '2024-02-10')";
    yield return "g.V('unit-plant1').addE('hosts').to(g.V('gw-006')).property('installed_date', '2024-02-10')";
    yield return "g.V('unit-plant2').addE('hosts').to(g.V('gw-007')).property('installed_date', '2024-03-01')";
    yield return "g.V('unit-plant2').addE('hosts').to(g.V('gw-008')).property('installed_date', '2024-03-01')";
    yield return "g.V('gw-005').addE('connects_to').to(g.V('equip-turbine301')).property('protocol', 'opcua').property('signal_strength', 97)";
    yield return "g.V('gw-006').addE('connects_to').to(g.V('equip-turbine302')).property('protocol', 'opcua').property('signal_strength', 94)";
    yield return "g.V('gw-007').addE('connects_to').to(g.V('equip-comp501')).property('protocol', 'mqtt').property('signal_strength', 91)";
    yield return "g.V('gw-008').addE('connects_to').to(g.V('equip-gen601')).property('protocol', 'mqtt').property('signal_strength', 86)";
    yield return "g.V('sensor-temp003').addE('monitors').to(g.V('equip-comp301')).property('attached_date', '2024-04-10').property('position', 'housing')";
    yield return "g.V('sensor-temp003').addE('assigned_to').to(g.V('gw-003')).property('channel', 1)";
    yield return "g.V('sensor-pres003').addE('monitors').to(g.V('equip-comp301')).property('attached_date', '2024-04-10').property('position', 'outlet')";
    yield return "g.V('sensor-pres003').addE('assigned_to').to(g.V('gw-003')).property('channel', 2)";
    yield return "g.V('sensor-hum001').addE('monitors').to(g.V('equip-hvac101')).property('attached_date', '2024-04-15').property('position', 'zone')";
    yield return "g.V('sensor-hum001').addE('assigned_to').to(g.V('gw-001')).property('channel', 3)";
}

// ─── UI ─────────────────────────────────────────────────────────────────────
//
// WaitForKey() — Why it exists:
//   When you run the app (e.g. double-click the exe or from Visual Studio), the console window
//   closes as soon as the program ends. You wouldn't have time to read "Done. OK: 85, Failed: 0"
//   or any error messages. So we wait for you to press any key before exiting.
//
//   When we skip it:
//   If input is "redirected" (e.g. running in a build script or CI where nobody is at the
//   keyboard), Console.IsInputRedirected is true — we do NOT wait, so the script can continue.
//
//   What it does:
//   Console.ReadKey() waits for one key press. The try/catch is there because in some
//   environments ReadKey() can throw; we catch and ignore so the app still exits cleanly.

static void WaitForKey()
{
    if (Console.IsInputRedirected) return;
    Console.WriteLine("Press any key to exit...");
    try { Console.ReadKey(); } catch (InvalidOperationException) { }
}
