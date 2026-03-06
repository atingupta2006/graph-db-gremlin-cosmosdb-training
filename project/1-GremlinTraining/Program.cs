using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Configuration;

// Load config: appsettings.json from app directory (so it works when run from repo root), optional override from env
var appDir = AppContext.BaseDirectory;
var config = new ConfigurationBuilder()
    .SetBasePath(appDir)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var hostname = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? config["CosmosDb:Hostname"]
    ?? throw new InvalidOperationException("Set CosmosDb:Hostname in appsettings.json or COSMOS_ENDPOINT env var.");

var authKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
    ?? config["CosmosDb:Key"]
    ?? throw new InvalidOperationException("Set CosmosDb:Key in appsettings.json or COSMOS_KEY env var.");

if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(authKey))
    throw new InvalidOperationException("CosmosDb:Hostname and CosmosDb:Key must be set in appsettings.json (or use env vars). See appsettings.Example.json.");

var databaseName = config["CosmosDb:Database"] ?? "iot-graph-db";
var graphName = config["CosmosDb:Graph"] ?? "asset-graph";

// Hostname only: no protocol, no port (e.g. your-account.gremlin.cosmos.azure.com)
hostname = hostname
    .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
    .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
    .Replace(":443/", "")
    .Replace(":443", "")
    .TrimEnd('/');

// Cosmos Gremlin API username format: /dbs/{database}/colls/{graph}
var username = $"/dbs/{databaseName}/colls/{graphName}";

var server = new GremlinServer(
    hostname,
    port: 443,
    enableSsl: true,
    username: username,
    password: authKey);

// For training/dev: accept server certificate (e.g. when corporate proxy or local trust causes SSL errors).
// Do not use in production.
void AcceptServerCertificate(System.Net.WebSockets.ClientWebSocketOptions options)
{
    options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
}

GremlinClient client;
try
{
    var poolSettings = new ConnectionPoolSettings { PoolSize = 2 };
    // Cosmos DB supports GraphSON v2 only; must set mimeType so the client does not send v3.
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
        if (inner.InnerException != null) inner = inner.InnerException;
        else break;
    }
    Console.WriteLine("Check: Hostname (no https/:443), Primary Key, database/graph names, firewall (outbound 443), and that the Cosmos DB account uses Gremlin API.");
    Console.WriteLine("For Gremlin API use the Gremlin endpoint hostname from Azure Portal (e.g. your-account.gremlin.cosmos.azure.com), not the SQL .documents.azure.com endpoint.");
    if (Console.IsInputRedirected == false) { Console.WriteLine("Press any key to exit..."); Console.ReadKey(); }
    return;
}

using (client)
{
Console.WriteLine("Connecting to Cosmos DB (Gremlin)...");

async Task RunQueryAsync(GremlinClient c, string query)
{
    Console.WriteLine($"  Query: {query}");
    var results = await c.SubmitAsync<dynamic>(query);
    foreach (var result in results)
        Console.WriteLine($"    -> {result}");
    if (results.StatusAttributes?.TryGetValue("x-ms-request-charge", out var ru) == true)
        Console.WriteLine($"    RU: {ru}");
}

try
{
    await RunQueryAsync(client, "g.V().limit(1)");
    Console.WriteLine();

    await RunQueryAsync(client, "g.V().hasLabel('tenant').count()");

    Console.WriteLine("Done.");
}
catch (ResponseException ex)
{
    Console.WriteLine($"Gremlin error ({(int)ex.StatusCode}): {ex.Message}");
    if (ex.StatusCode == ResponseStatusCode.Unauthorized) Console.WriteLine("  -> Check CosmosDb:Key (Primary Key).");
    if ((int)ex.StatusCode == 404) Console.WriteLine("  -> Check database and graph names (CosmosDb:Database, CosmosDb:Graph).");
    if ((int)ex.StatusCode == 429) Console.WriteLine("  -> Throttled; reduce RU usage or increase throughput.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request timed out.");
}
catch (Exception ex)
{
    var inner = ex.InnerException ?? ex;
    Console.WriteLine($"Error: {inner.Message}");
}

}
if (Console.IsInputRedirected == false) { Console.WriteLine("Press any key to exit..."); Console.ReadKey(); }
