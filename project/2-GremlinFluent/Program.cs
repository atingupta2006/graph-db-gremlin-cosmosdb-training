using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;

static void WaitForKey()
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(); } catch (InvalidOperationException) { }
    }
}

// Same config as GremlinTraining: app directory so it works when run from repo root
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

// Normalize: strip protocol and port
var hostname = hostnameRaw
    .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
    .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
    .Replace(":443/", "")
    .Replace(":443", "")
    .TrimEnd('/');

// CosmosClient uses the document endpoint; Gremlin uses the Gremlin endpoint.
// Support both hostname formats (e.g. account.documents.azure.com or account.gremlin.cosmos.azure.com).
string documentEndpoint;
string gremlinHostname;
if (hostname.Contains(".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase))
{
    gremlinHostname = hostname;
    documentEndpoint = "https://" + hostname.Replace(".gremlin.cosmos.azure.com", ".documents.azure.com", StringComparison.OrdinalIgnoreCase);
}
else
{
    if (hostname.Contains(".documents.azure.com", StringComparison.OrdinalIgnoreCase))
    {
        documentEndpoint = "https://" + hostname;
        gremlinHostname = hostname.Replace(".documents.azure.com", ".gremlin.cosmos.azure.com", StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        documentEndpoint = "https://" + hostname + ".documents.azure.com";
        gremlinHostname = hostname + ".gremlin.cosmos.azure.com";
    }
}

// 1) Ensure database and graph (container) exist using Cosmos SQL SDK
Console.WriteLine("Ensuring database and graph exist...");
const string partitionKeyPath = "/pk";
const int throughput = 400;

using (var cosmosClient = new CosmosClient(documentEndpoint, authKey))
{
    var dbResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName, throughput);
    var database = dbResponse.Database;
    if (dbResponse.StatusCode == System.Net.HttpStatusCode.Created)
        Console.WriteLine($"  Created database: {databaseName}");
    else
        Console.WriteLine($"  Database exists: {databaseName}");

    var containerResponse = await database.CreateContainerIfNotExistsAsync(
        new ContainerProperties(graphName, partitionKeyPath),
        throughput);
    if (containerResponse.StatusCode == System.Net.HttpStatusCode.Created)
        Console.WriteLine($"  Created graph: {graphName}");
    else
        Console.WriteLine($"  Graph exists: {graphName}");
}

Console.WriteLine();

// 2) Connect via Gremlin and run traversals (string-based; Cosmos DB does not support bytecode)
var username = $"/dbs/{databaseName}/colls/{graphName}";
var server = new GremlinServer(gremlinHostname, 443, true, username, authKey);

void AcceptServerCertificate(System.Net.WebSockets.ClientWebSocketOptions options)
    => options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

GremlinClient client;
try
{
    var poolSettings = new ConnectionPoolSettings { PoolSize = 2 };
    client = new GremlinClient(
        server,
        new GraphSON2Reader(),
        new GraphSON2Writer(),
        mimeType: GremlinClient.GraphSON2MimeType,
        connectionPoolSettings: poolSettings,
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
    Console.WriteLine("For Gremlin API use the Gremlin endpoint from Azure Portal (e.g. your-account.gremlin.cosmos.azure.com), not the SQL .documents.azure.com endpoint.");
    WaitForKey();
    return;
}

using (client)
{
    Console.WriteLine("Connecting via Gremlin (string queries)...");

    try
    {
        Console.WriteLine("  Traversal: g.V().limit(1)");
        var first = await client.SubmitAsync<Dictionary<string, object>>("g.V().limit(1)");
        foreach (var v in first)
            Console.WriteLine($"    -> {Newtonsoft.Json.JsonConvert.SerializeObject(v)}");
        Console.WriteLine();

        Console.WriteLine("  Traversal: g.V().hasLabel('tenant').count()");
        var tenantCountResult = await client.SubmitAsync<long>("g.V().hasLabel('tenant').count()");
        var tenantCount = tenantCountResult.FirstOrDefault();
        Console.WriteLine($"    -> {tenantCount}");
        Console.WriteLine("Done.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

WaitForKey();
