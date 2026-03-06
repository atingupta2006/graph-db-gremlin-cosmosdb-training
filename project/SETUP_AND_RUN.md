# Environment setup and running the projects

Use this guide to set up your machine and run **GremlinTraining**, **GremlinFluent**, and **GremlinSeed** against Azure Cosmos DB (Gremlin API). You can follow it manually or use the commands as a checklist.

---

## 1. Install .NET SDK (required)

The projects target **.NET 10**. You need the **.NET 10 SDK** (not only the runtime) so you can build and run them.

### Option A — Install with winget (recommended on Windows)

Open **PowerShell** or **Command Prompt** and run:

```powershell
winget install Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
```

If `winget` is not available, install [App Installer](https://aka.ms/getwinget) first, or use Option B.

### Option B — Download the installer

1. Go to: **https://dotnet.microsoft.com/en-us/download/dotnet/10.0**
2. Under **.NET SDK 10.x**, download the **Windows** installer (e.g. **x64**).
3. Run the installer and complete the steps.
4. Close and reopen any terminal/IDE so the new `dotnet` is on the PATH.

### Verify installation

Open a **new** terminal and run:

```powershell
dotnet --version
```

You should see something like `10.0.xxx`. If you see a version **&lt; 10**, either install the .NET 10 SDK or change the projects to an SDK you have (see **Alternative: use .NET 8 or 9** at the end).

---

## 2. Open the project folder

Open the **repository root** in your editor or file explorer. The folder that contains the `project` folder and `hands-on` folder is the repo root.

Example: if the repo is at `C:\25-Trainings\2-Confirmed\17-GraphDB\GH`, then:

- Repo root: `C:\25-Trainings\2-Confirmed\17-GraphDB\GH`
- Projects: `C:\25-Trainings\2-Confirmed\17-GraphDB\GH\project\GremlinTraining`, `GremlinFluent`, `GremlinSeed`

You can open the repo root in VS Code, Visual Studio, or a separate window as you prefer.

---

## 3. Configure Cosmos DB (all projects)

Each project needs your **Cosmos DB Gremlin** endpoint and key. Use the same values for all three.

1. In each project folder there is an **`appsettings.Example.json`**. Copy it to **`appsettings.json`** (create the file if it does not exist):

   - `project\GremlinTraining\appsettings.json`
   - `project\GremlinFluent\appsettings.json`
   - `project\GremlinSeed\appsettings.json`

2. Edit each **`appsettings.json`** and set:

   - **Hostname**: your Cosmos DB Gremlin host, e.g. `your-account-name.gremlin.cosmos.azure.com`  
     (no `https://`, no `:443`)
   - **Key**: the **Primary Key** from the Azure portal (Keys blade for your Cosmos DB account)

   Example:

   ```json
   {
     "CosmosDb": {
       "Hostname": "your-account.gremlin.cosmos.azure.com",
       "Key": "your-primary-key-here",
       "Database": "iot-graph-db",
       "Graph": "asset-graph"
     }
   }
   ```

3. Ensure the Cosmos DB account uses the **Gremlin API**, and that the **database** and **graph** (container) exist. Default names used here: database **`iot-graph-db`**, graph **`asset-graph`**, partition key **`/pk`**.

---

## 4. Restore and build (one-time)

From the **repo root**, run:

```powershell
cd project
dotnet restore
dotnet build
```

Or build each project:

```powershell
dotnet build project\GremlinTraining\GremlinTraining.csproj
dotnet build project\GremlinFluent\GremlinFluent.csproj
dotnet build project\GremlinSeed\GremlinSeed.csproj
```

If all three build successfully, you are ready to run.

---

## 5. Run the projects

All commands below are from the **repo root** (`GH` or wherever your repo lives).

### GremlinTraining (raw Gremlin strings)

```powershell
dotnet run --project project\GremlinTraining\GremlinTraining.csproj
```

Expected: connects to Cosmos DB, runs a couple of sample queries, prints output and RU. If you see connection errors, check Hostname, Key, firewall, and that the account is Gremlin API.

---

### GremlinFluent (fluent API)

```powershell
dotnet run --project project\GremlinFluent\GremlinFluent.csproj
```

Expected: connects and runs traversals built in C# (e.g. `g.V().Limit(1)`, tenant count).

---

### GremlinSeed (load graph data)

**Minimal data (Day 01–03 style):**

```powershell
dotnet run --project project\GremlinSeed\GremlinSeed.csproj -- --minimal
```

**Full Day 04 data (two tenants, extra units, gateways, equipment, sensors):**

```powershell
dotnet run --project project\GremlinSeed\GremlinSeed.csproj -- --day4
```

Expected: progress lines like `[10/xx] OK` and finally `Done. OK: xx, Failed: 0`. Run **once** on an empty graph; running again can create duplicate vertices. If you get **429** (throttling), wait a few seconds or increase the container RU and try again.

---

## 6. Quick checklist (manual run)

- [ ] .NET 10 SDK installed; `dotnet --version` shows 10.x  
- [ ] Repo opened (folder that contains `project` and `hands-on`)  
- [ ] `appsettings.json` created in GremlinTraining, GremlinFluent, GremlinSeed (from Example)  
- [ ] Hostname and Key set in each `appsettings.json`  
- [ ] `dotnet restore` and `dotnet build` from `project` (or repo root) succeed  
- [ ] GremlinTraining runs and prints query results  
- [ ] GremlinFluent runs and prints traversal results  
- [ ] GremlinSeed runs with `--day4` and finishes with `Failed: 0`  
- [ ] In Azure Portal, Data Explorer → your DB → graph → Gremlin tab: run `g.V().count()` and see your loaded data  

---

## 7. Optional: environment variables instead of appsettings

You can override config with environment variables so you don’t put the key in files:

- **COSMOS_ENDPOINT**: same as Hostname (e.g. `your-account.gremlin.cosmos.azure.com`)
- **COSMOS_KEY**: Primary Key

Set them in your shell or in the project’s launch profile; the apps read these first, then fall back to `appsettings.json`.

---

## 8. Alternative: use .NET 8 or 9

If you cannot install .NET 10, you can target .NET 8 or 9 instead:

1. Open each **`.csproj`** under `project\` (GremlinTraining, GremlinFluent, GremlinSeed).
2. Change `<TargetFramework>net10.0</TargetFramework>` to either:
   - `net9.0`, or  
   - `net8.0`
3. Save and run `dotnet restore` and `dotnet build` again.

Install the matching SDK (e.g. .NET 8 or 9) from https://dotnet.microsoft.com/download if needed.

---

## 9. Troubleshooting

| Issue | What to check |
|-------|----------------|
| `dotnet` not found | Install .NET SDK; close and reopen the terminal. |
| Build fails (e.g. SDK not found) | Install .NET 10 SDK or change `TargetFramework` to net8.0/net9.0 and install that SDK. |
| Connection error / 401 | Key (Primary Key) and Hostname; no `https://` in Hostname. |
| 404 | Database and graph names (e.g. `iot-graph-db`, `asset-graph`). |
| 429 | Throttling; wait and retry or increase container RUs. |
| SSL/certificate error | The projects use a dev-only SSL callback; if you still see cert errors, check proxy/firewall. |

For more detail on each app, see the **README.md** inside `GremlinTraining`, `GremlinFluent`, and `GremlinSeed`.
