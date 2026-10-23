# TaskPilot API

TaskPilot is a task-management portfolio product with an ASP.NET Core Web API and planned native clients for Android (Kotlin/Jetpack Compose) and iOS (Swift/SwiftUI). This repository contains the backend scaffold, an EF Core SQLite persistence foundation, a task service layer, unit and integration tests, and a continuous integration workflow.

## Prerequisites

- Install the **.NET SDK**, not only the runtime, matching `global.json` (10.0.401 or a later 10.0.4xx patch). Choose Arm64 for Apple Silicon or the appropriate architecture for your computer. Use the official [macOS, Windows, and Linux installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/).
- Install Git and curl. On Windows, use PowerShell and `curl.exe` explicitly to avoid the Windows PowerShell `curl` alias. The examples below distinguish PowerShell from bash/zsh syntax.
- Access to NuGet is required to restore dependencies. SQLite is supplied by the existing EF Core provider; no database server, SQLite CLI, Docker, or mobile SDK is needed to run the API.
- Verify installation with `dotnet --info`, `git --version`, and `curl --version` (`curl.exe --version` on Windows). Run repository commands from the repository root.

`global.json` targets SDK 10.0.401, allows the latest patch in its feature band, and disables prerelease SDKs. The API targets `net10.0`.

## Current structure

```text
taskpilot-api/
├── .github/workflows/ci.yml
├── .gitignore
├── AGENTS.md                     # Optional local-only instructions; ignored and untracked
├── dotnet-tools.json               # Repository-local EF CLI, pinned to 10.0.13
├── global.json
├── README.md
├── TaskPilot.slnx
├── prompts/                       # Local, Git-ignored prompt history
├── src/TaskPilot.Api/
│   ├── TaskPilot.Api.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Entities/TaskItem.cs
│   ├── Services/Tasks/            # Service interface, implementation, inputs, and result
│   ├── Data/TaskPilotDbContext.cs
│   ├── Data/Configurations/TaskItemConfiguration.cs
│   ├── Data/Migrations/           # InitialCreate and model snapshot
│   ├── Controllers/HealthController.cs
│   ├── Controllers/TasksController.cs
│   ├── Models/Tasks/             # Validated create and update requests
│   └── Models/HealthResponse.cs
└── tests/
    ├── TaskPilot.Api.UnitTests/
    │   ├── TaskPilot.Api.UnitTests.csproj
    │   └── TaskContentValidatorTests.cs
    └── TaskPilot.Api.IntegrationTests/
        ├── TaskPilot.Api.IntegrationTests.csproj
        ├── ApiContractTests.cs
        ├── PersistenceTests.cs
        ├── TaskServiceTests.cs
        └── TaskEndpointTests.cs
```

The architecture is **Controllers → Services → Data access**, using constructor injection. `TasksController` translates HTTP requests into calls to `ITaskService` and maps results to response status codes. It does not access the database directly. `Program.cs` registers controllers and maps their attribute routes with `MapControllers`. There are no repositories, authentication, or custom unit-of-work wrappers.

## Task service

`ITaskService` and `TaskService` live under `Services/Tasks`. The service is registered with a scoped lifetime matching `TaskPilotDbContext`, which it receives directly through constructor injection. It owns task creation defaults, content validation, and persistence. The controller translates service results into HTTP responses; the service has no HTTP dependencies.

| Operation | Input | Result |
| --- | --- | --- |
| `CreateAsync` | `CreateTaskInput` with title and optional description | Saved `TaskResult` |
| `ListAsync` | None | All snapshots ordered by creation time, then ID; empty list when no tasks exist |
| `GetByIdAsync` | Task ID | Snapshot, or `null` if missing |
| `UpdateAsync` | Task ID and `UpdateTaskInput` | Saved snapshot, or `null` if missing |
| `DeleteAsync` | Task ID | `true` if deleted, `false` if missing |

All operations accept an optional `CancellationToken` and use EF Core asynchronous database APIs. Each successful mutation saves before returning. Results are immutable `TaskResult` snapshots; the public contract exposes neither tracked entities nor queryables. Missing tasks are normal absence results rather than exceptions or HTTP response types.

Creation generates a new GUID, sets `CreatedAtUtc` with `DateTime.UtcNow`, and initializes `IsCompleted` to false. Updates replace title, description, and completion state while preserving identity and creation time; a null description clears it. Create and update reject null inputs, blank titles, titles longer than 200 characters, and descriptions longer than 2000 characters with `ArgumentException` (including `ArgumentNullException` for null input objects). Content is preserved without trimming. Update validates content before looking up the task. Cancellation and database failures propagate to the caller.

## Task REST API

All routes use JSON and return the existing `TaskResult` contract. Request cancellation is forwarded to every service operation.

| Method and path | Success | Missing task |
| --- | --- | --- |
| `POST /api/tasks` | 201 with task JSON and a `Location` retrieval URL | Not applicable |
| `GET /api/tasks` | 200 with an array (empty when no tasks exist) | Not applicable |
| `GET /api/tasks/{id}` | 200 with task JSON | 404 |
| `PUT /api/tasks/{id}` | 200 with updated task JSON | 404 |
| `DELETE /api/tasks/{id}` | 204 with no body | 404 |

Create and update require a nonblank `title` of at most 200 characters. `description` is optional and limited to 2000 characters. PUT replaces editable fields: a null or omitted description clears it, and omitted `isCompleted` defaults to false. IDs and creation timestamps are server-owned; POST always creates an incomplete task. Unknown JSON fields are ignored. Invalid GUIDs, malformed JSON, missing request bodies, and invalid field values return 400 with the standard `ValidationProblemDetails` JSON format (`application/problem+json`). Unsupported media types return the framework's 415 response.

After starting the API as described below (startup applies migrations automatically):

```sh
curl -i -X POST http://localhost:5080/api/tasks \
  -H 'Content-Type: application/json' \
  -d '{"title":"Write API tests","description":"Cover CRUD behavior"}'
```

Example response (201 Created, with `Location: http://localhost:5080/api/tasks/6fd063b7-808c-4e66-9faf-dfe653ab608c`; generated values vary):

```json
{
  "id": "6fd063b7-808c-4e66-9faf-dfe653ab608c",
  "title": "Write API tests",
  "description": "Cover CRUD behavior",
  "isCompleted": false,
  "createdAtUtc": "2026-10-22T12:00:00Z"
}
```

Use the returned ID for subsequent requests:

```sh
task_id='6fd063b7-808c-4e66-9faf-dfe653ab608c'
curl -i http://localhost:5080/api/tasks
curl -i "http://localhost:5080/api/tasks/$task_id"
curl -i -X PUT "http://localhost:5080/api/tasks/$task_id" \
  -H 'Content-Type: application/json' \
  -d '{"title":"Write API tests","description":null,"isCompleted":true}'
curl -i -X DELETE "http://localhost:5080/api/tasks/$task_id"
```

GET by ID and PUT return the same object shape as POST. PUT above returns `description: null` and `isCompleted: true` while preserving `id` and `createdAtUtc`. GET collection wraps task objects in an array. DELETE returns 204 without a body; retrieving or deleting that ID again returns 404.

For example, POST with `{"title":""}` returns 400 with this validation-problem shape (additional fields and messages may vary):

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Title": ["The Title field is required."] }
}
```

## Persistence and migrations

`TaskItem` holds a server-assigned `Guid` ID, required title (200 characters), optional description (2000 characters), completion state (initially false), and UTC creation time. The task service assigns IDs and UTC timestamps. Code writing directly through the context must supply `CreatedAtUtc` in UTC; the EF mapping restores `DateTimeKind.Utc` on reads because SQLite storage does not preserve that .NET metadata.

`TaskPilotDbContext` exposes `Tasks` and tracks database changes. `TaskItemConfiguration` defines the primary key, required fields, and intended text lengths. SQLite does **not** enforce string lengths merely because `HasMaxLength` is configured; the task service enforces these limits for service callers. [SQLite type limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types)

Dependency injection registers the context with the named `TaskPilot` connection string in `appsettings.json`: `Data Source=taskpilot.db`. SQLite resolves relative paths against the process working directory. With the documented `dotnet run --project src/TaskPilot.Api/TaskPilot.Api.csproj ...` command, that directory is `src/TaskPilot.Api`, so the file is `src/TaskPilot.Api/taskpilot.db`. Override the connection string through `ConnectionStrings__TaskPilot` when needed.

The EF CLI-generated `InitialCreate` migration creates the `Tasks` table with all five fields, its primary key, and nullable description. The model snapshot records the schema for future migration comparisons. Both source runs and packaged execution apply pending migrations automatically before accepting HTTP requests. The existing hosted initializer resolves the same scoped `TaskPilotDbContext` used by the API, so migrations use its exact configured connection string. A missing database is created; existing databases are never deleted, recreated, or reset. EF migration history prevents completed migrations from being reapplied on repeated startup. Configuration, directory, or migration failures abort startup with a clear initialization error; cancellation propagates normally.

The EF CLI is needed only to author migrations, not to run the API. For migration authoring, restore the pinned local tool from the repository root:

```sh
dotnet tool restore
```

The initial migration was generated with this command (it is already committed; do not regenerate it):

```sh
dotnet ef migrations add InitialCreate --project src/TaskPilot.Api/TaskPilot.Api.csproj --startup-project src/TaskPilot.Api/TaskPilot.Api.csproj --output-dir Data/Migrations
```

For future model changes, use a new descriptive migration name in that command. Review generated migrations before applying them. Follow the cross-platform [local setup](#run-locally) below to start the API against an explicit absolute database path; startup applies committed migrations without an EF CLI update command. Do not regenerate the initial migration.

Startup applies pending migrations without deleting or resetting an existing database. Local database files and journal/WAL/SHM companions are ignored by Git; migration source files are versioned. Production SQLite requires persistent storage for its database files. Hosting and automated deployment are not configured.

## Build, test, and publish

CI runs the following sequence from the repository root. Under the repository's Test Execution Policy, agents run tests through GitHub Actions by default and need explicit user authorization to run tests locally. Local builds are used only to resolve concrete compilation concerns.

```sh
dotnet restore TaskPilot.slnx
dotnet build TaskPilot.slnx --configuration Release --no-restore
dotnet test tests/TaskPilot.Api.UnitTests/TaskPilot.Api.UnitTests.csproj --configuration Release --no-build --logger "trx;LogFileName=unit-tests.trx" --results-directory artifacts/test-results
dotnet test tests/TaskPilot.Api.IntegrationTests/TaskPilot.Api.IntegrationTests.csproj --configuration Release --no-build --logger "trx;LogFileName=integration-tests.trx" --results-directory artifacts/test-results
dotnet publish src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --no-build --output artifacts/publish
```

`TaskPilot.Api.UnitTests` uses the same xUnit, test SDK, and runner versions as the integration project. Its task-content cases directly exercise the internal, pure `TaskContentValidator` without an HTTP host, EF Core context, SQLite database, filesystem access, or mocks. Coverage includes null/empty/ASCII and Unicode whitespace titles, title lengths 1/199/200/201, surrounding whitespace, UTF-16 length semantics, optional descriptions, description lengths 1999/2000/2001, and title-error precedence when both fields are invalid.

`TaskService` calls this validator before persistence in create and update. The extraction preserves exception types, messages, parameter names, validation order, and string-length behavior. The validator remains internal, with access granted only to the unit-test assembly through `InternalsVisibleTo`; no new public service contract or dependency is introduced. HTTP DTO validation attributes remain unchanged.

Unit tests check content decisions in isolation. Integration tests retain responsibility for service persistence, migrations, cancellation, application wiring, and HTTP status/validation response contracts. Integration tests overlap at boundary inputs, which verifies that the rules remain connected to persistence and HTTP behavior rather than replacing the unit tests.

The three xUnit integration tests use `WebApplicationFactory` to run the actual application pipeline in memory, without an external server. They verify the health endpoint's HTTP 200, JSON content type, and status property; the Development OpenAPI document and its health operation; and HTTP 404 for OpenAPI in Production. Each test explicitly selects its environment, uses a private temporary SQLite database initialized by startup, and disposes its hosts and HTTP client before database cleanup. The public partial `Program` declaration provides test access to the top-level application entry point.

Historical verification: all three initial tests passed locally before the CI-first test execution policy was adopted. This does not establish the result of any later CI run.

The persistence integration test uses the actual SQLite provider and a uniquely named temporary database. It applies the committed migrations with `MigrateAsync`, saves a task, disposes the writing context, and verifies all fields and UTC timestamp kind through a fresh context. Pooling is disabled and only that test's temporary files are cleaned up after contexts are disposed. It never uses the developer database.

`TaskServiceTests` applies the same migrations to a separate temporary SQLite database for every test case. Coverage includes creation defaults and unique IDs, listing and retrieval, updates preserving server fields, description clearing, deletion, missing tasks, invalid content, exact length boundaries, cancellation, and scoped application DI registration. Persistence assertions use fresh contexts. The DI test overrides the application connection string with its own isolated database. The integration-test CI command continues to discover this suite. Service tests are not run locally; their execution status must be checked in GitHub Actions for the relevant commit.

CI runs the unit suite first and then the integration suite after the solution build. Both must pass before API publishing. Distinct `unit-tests.trx` and `integration-tests.trx` files are uploaded together as the `test-results` artifact, including available results when a step fails. A failed earlier step prevents later test/publish steps from running. No automated tests are run locally by agents without explicit authorization.

TRX results are written to `artifacts/test-results/` and published API files to `artifacts/publish/`. These generated files are Git-ignored. Publishing creates application files; it does not deploy them.

`TaskEndpointTests` hosts the real HTTP pipeline with a separate migrated SQLite database per test case. It verifies CRUD responses and persistence across requests, creation Location URLs, server-owned fields, validation-problem responses, missing tasks, exact text limits, update defaults, and OpenAPI task operations. Hosts and contexts are disposed before temporary database cleanup. These tests never access the developer database and are executed through the same CI command, not locally by agents.

## Continuous integration and planned deployment

The GitHub Actions workflow runs on pushes and pull requests targeting `main`, or manually through `workflow_dispatch`. Its build/test job uses an Ubuntu runner with only `contents: read` permissions and installs the SDK specified by `global.json`. It restores, builds in Release, runs unit and integration tests, uploads available TRX results even when tests fail, and publishes and uploads the API only after successful tests. Dependent distribution jobs publish and execute the four self-contained packages on matching native runners.

This is CI configuration, not evidence of a successful GitHub Actions run. Hosting and automated deployment are not configured yet. Continuous deployment (CD) will be implemented once the hosting target is selected.

## Download and run a self-contained distribution

No .NET SDK, runtime, Python, SQLite CLI, or database server is required on the recipient's computer. Source development still uses the SDK and manual migrations described below.

Open [GitHub Actions CI](https://github.com/gurhangure/taskpilot-api/actions/workflows/ci.yml), select a successful run for the desired commit on main, and download the matching artifact from its **Artifacts** section (GitHub sign-in required). Artifacts expire after 30 days; rerun CI if needed. The older `taskpilot-api` artifact is framework-dependent and intended for developers; choose a platform-specific artifact below for standalone use.

| Artifact | Archive inside artifact ZIP | Native verification runner |
| --- | --- | --- |
| taskpilot-api-win-x64 | taskpilot-api-win-x64.zip | windows-2025 (x64) |
| taskpilot-api-osx-arm64 | taskpilot-api-osx-arm64.tar.gz | macos-15 (Apple Silicon) |
| taskpilot-api-osx-x64 | taskpilot-api-osx-x64.tar.gz | macos-15-intel (Intel) |
| taskpilot-api-linux-x64 | taskpilot-api-linux-x64.tar.gz | ubuntu-24.04 (x64) |

Extract the downloaded artifact ZIP, then extract the archive inside it. Keep all extracted files together, including `appsettings.json`. Unix tar archives preserve executable permissions. These are console applications, not installers or desktop apps.

Windows x64, from PowerShell in the extracted package directory:

```powershell
.\TaskPilot.Api.exe --urls http://127.0.0.1:5080
```

Run from a terminal so startup errors remain visible. The executable is unsigned; SmartScreen or organization policy may block it. Verify the repository and workflow provenance before allowing a trusted download, and follow your organization's policy. Do not disable security protections or run as administrator.

macOS Apple Silicon or Intel, using the matching package:

```sh
tar -xzf taskpilot-api-osx-arm64.tar.gz # Use osx-x64 on Intel.
chmod u+x TaskPilot.Api
./TaskPilot.Api --urls http://127.0.0.1:5080
```

Packages are not Developer ID signed or notarized. Gatekeeper may block downloaded executables. After verifying the source, use macOS System Settings > Privacy & Security to allow the specific application if offered; managed Macs may require administrator approval. Do not disable Gatekeeper globally. A permission error may require the chmod command above; an architecture error requires the other macOS package.

Linux x64:

```sh
tar -xzf taskpilot-api-linux-x64.tar.gz
chmod u+x TaskPilot.Api
./TaskPilot.Api --urls http://127.0.0.1:5080
```

Linux needs a supported glibc-based OS and the [native OS dependencies for .NET](https://learn.microsoft.com/en-us/dotnet/core/install/linux). Self-contained publishing includes .NET, but not all system libraries. Alpine/musl, Linux ARM, and Windows ARM are not distribution targets. CI verifies Ubuntu 24.04, Windows Server 2025, and macOS 15; it does not establish compatibility with every desktop OS version.

Check `http://127.0.0.1:5080/api/health` for `{"status":"ok"}`. There is no home-page UI. Stop with Ctrl+C. Without `--urls` or other URL configuration, the default is `http://localhost:5000`. Set `--urls` or `ASPNETCORE_URLS` to select another port or a specific trusted LAN address; see [network access](#optional-local-network-access) before connecting a phone. Configuration files are loaded beside the distributed executable even when launched from another directory; environment variables and command-line settings retain their normal precedence.

The Distribution profile compiles packaged mode into the executable: no opt-in flag is needed, and `TaskPilot:Packaged=false` cannot disable it in these binaries. Normal builds and ordinary publishing retain the original opt-in behavior (`--TaskPilot:Packaged=true`) and source runs now migrate automatically while preserving their existing connection-string and location behavior.

### Publishing and executable verification

CI retains the existing solution build, unit tests, integration tests, and framework-dependent publish. After that job succeeds, four independent jobs publish with this command, replacing the RID as appropriate:

```sh
dotnet publish src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --runtime osx-arm64 -p:PublishProfile=Distribution --output artifacts/package/osx-arm64
```

The profile uses self-contained **single-file** publishing on all four targets, with native runtime/SQLite libraries bundled for extraction. Configuration JSON stays external and editable. EF migrations remain compiled in the application assembly. Trimming and Native AOT are disabled to retain reflection-based EF migration discovery and MVC behavior. A writable native bundle extraction cache is required; `DOTNET_BUNDLE_EXTRACT_BASE_DIR` can select a private writable directory. See [Microsoft's single-file deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/).

Publishing is build verification only. In a separate runtime step, `scripts/verify-distribution.py` archives and extracts the package, launches the actual native executable from another working directory with an isolated temporary SQLite path configured in its JSON sidecar, checks health and CRUD/validation, inspects EF migration history, stops gracefully, restarts against the same database, and checks persistence. Unix uses SIGTERM; Windows uses CTRL_BREAK. Failure to publish, launch, migrate, answer requests, preserve data, or shut down cleanly fails the job and prevents that distribution upload. Runtime logs and a success summary are uploaded separately as `runtime-verification-RID`. No automated tests are run locally by agents.

All four targets have native execution jobs; no target is intentionally build-only. A successful publish alone is not a runtime pass: inspect the runtime step and result artifact for the selected commit. No GitHub Releases, deployment, signing, notarization, or installers are created.

### Packaged database storage

On first run, a startup initializer creates the data directory and calls EF Core `MigrateAsync` using the committed migrations. It completes before the HTTP server accepts requests. Later starts apply only pending migrations and retain existing tasks. The initializer never calls `EnsureCreated`, deletes a database, or resets data. Directory, configuration, or migration failures log a clear initialization error and abort host startup; no health endpoint is served as if initialization succeeded. Startup cancellation propagates normally. Repair the path/permissions or investigate database compatibility rather than deleting data to bypass an error.

Default storage is `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskPilot", "taskpilot.db")` for the user running the process:

| OS | Typical packaged database location |
| --- | --- |
| macOS (.NET 10) | `~/Library/Application Support/TaskPilot/taskpilot.db` |
| Windows | `%LOCALAPPDATA%\TaskPilot\taskpilot.db` |
| Linux | `$XDG_DATA_HOME/TaskPilot/taskpilot.db`, normally `~/.local/share/TaskPilot/taskpilot.db` |

The OS supplies the application-data root; unavailable roots cause startup failure rather than falling back to the executable directory. The selected account needs write access to the directory for SQLite journal/WAL/SHM files as well as the database. Do not run as administrator/root just to make an unwritable location work. No developer database is copied or migrated into this location automatically.

To override storage, supply a fully qualified SQLite file path. Relative and blank overrides are rejected. The parent directory is created if needed:

```sh
# From the package directory; the path is derived from the current user's home.
./TaskPilot.Api --TaskPilot:Packaged=true --TaskPilot:DatabasePath="$HOME/TaskPilotData/tasks.db" --urls http://127.0.0.1:5081
```

The equivalent environment variable is `TaskPilot__DatabasePath`. In packaged mode this setting (or the application-data default) deliberately takes precedence over `ConnectionStrings__TaskPilot`, so the development `Data Source=taskpilot.db` cannot accidentally put a database beside the executable. Outside packaged mode, `TaskPilot:DatabasePath` is unused and the existing named connection string behaves as before. Connection string options are not a packaged configuration surface in this initial foundation.

Use one API process per database during upgrades, stop it and back up the database before replacing a package, and review future migrations for data-loss risk. Automatic migrations do not provide backups, rollback, compatibility with older binaries, or coordination for a multi-instance deployment. Never replace an existing schema with `EnsureCreated` or reset a database after a migration failure.

`PackagedDatabaseTests` checks path selection and configuration isolation without I/O. `PackagedStartupTests` uses temporary SQLite files to verify first-run migrations in both modes, data persistence and migration history after restart, initialization of an existing empty source database, configuration precedence, corrupt-file failure, and unusable-directory failure. These host-based suites are supplemented by the native executable verification in each distribution job. Consult the selected CI run for actual execution results.

## Run locally

The following setup is for a human developer. Agents still follow the CI-first test policy; the explicitly requested manual HTTP smoke test does not run the automated test suite. Start with a loopback URL, and opt into LAN access only when you need a device connection.

Restore dependencies and compile the API from the repository root:

```sh
dotnet restore TaskPilot.slnx
dotnet build src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --no-restore
```

Select a database and URL in the **same terminal** used to start the application. These paths are derived from your checkout, not hardcoded to a machine. The directory must exist and be writable. The environment variable overrides the relative connection string in `appsettings.json` for the API and its startup migrations. Source mode preserves all connection-string options and requires the parent directory to exist; packaged mode continues to create its selected data directory.

macOS/Linux (bash or zsh):

```sh
export ConnectionStrings__TaskPilot="Data Source=$PWD/src/TaskPilot.Api/taskpilot.db"
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5080
```

Windows (PowerShell):

```powershell
$databasePath = Join-Path (Get-Location).Path 'src/TaskPilot.Api/taskpilot.db'
$env:ConnectionStrings__TaskPilot = "Data Source=$databasePath"
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
```

For a disposable smoke test, **replace the database setting above before startup**. This avoids using or resetting the developer database:

```sh
# macOS/Linux
mkdir -p artifacts
smoke_directory=$(mktemp -d "$PWD/artifacts/manual-smoke.XXXXXX")
export ConnectionStrings__TaskPilot="Data Source=$smoke_directory/tasks.db"
```

```powershell
# Windows PowerShell
$smokeDirectory = Join-Path (Get-Location).Path ('artifacts/manual-smoke.' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null
$env:ConnectionStrings__TaskPilot = "Data Source=$(Join-Path $smokeDirectory 'tasks.db')"
```

Then start the API in either shell. No EF CLI command is required; initialization must succeed before the API starts listening.

```sh
dotnet run --project src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --no-build --no-launch-profile
```

The migration creates a new database when absent and applies pending migrations when it already exists. It does not reset the database. Check the startup log for `Now listening on: http://127.0.0.1:5080`. Choose another unused port by changing `ASPNETCORE_URLS`; alternatively append `--urls http://127.0.0.1:5081` to `dotnet run` to override the URL for that invocation. No URL or firewall settings are changed automatically.

Stop the API with Ctrl+C. Environment variables remain in the current shell until cleared or the terminal is closed. After stopping, you may remove only the disposable directory you created (including any SQLite WAL/SHM files); never delete the normal developer database for a smoke test. The whole `artifacts/` directory is Git-ignored.

## Manual HTTP smoke test

Keep the API running and open a second terminal at the repository root. Use the same port as the startup log. `-i` prints response headers and bodies; inspect the HTTP status rather than curl's exit code, because curl normally exits successfully even for HTTP 400/404.

macOS/Linux:

```sh
base_url=http://127.0.0.1:5080
curl -i "$base_url/api/health"
curl -i -X POST "$base_url/api/tasks" -H 'Content-Type: application/json' -d '{"title":"Smoke task","description":"Temporary data"}'
# Copy the returned task ID. The Location header should point to its GET URL.
task_id='REPLACE_WITH_RETURNED_ID'
curl -i "$base_url/api/tasks"
curl -i "$base_url/api/tasks/$task_id"
curl -i -X PUT "$base_url/api/tasks/$task_id" -H 'Content-Type: application/json' -d '{"title":"Smoke complete","description":null,"isCompleted":true}'
curl -i -X DELETE "$base_url/api/tasks/$task_id"
curl -i "$base_url/api/tasks/$task_id"
curl -i -X POST "$base_url/api/tasks" -H 'Content-Type: application/json' -d '{"title":""}'
```

Windows PowerShell uses request files to avoid differences in native-command JSON quoting between PowerShell versions:

```powershell
$baseUrl = 'http://127.0.0.1:5080'
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
'{"title":"Smoke task","description":"Temporary data"}' | Set-Content -Encoding ascii artifacts/smoke-create.json
'{"title":"Smoke complete","description":null,"isCompleted":true}' | Set-Content -Encoding ascii artifacts/smoke-update.json
'{"title":""}' | Set-Content -Encoding ascii artifacts/smoke-invalid.json
curl.exe -i "$baseUrl/api/health"
curl.exe -i -X POST "$baseUrl/api/tasks" -H 'Content-Type: application/json' --data-binary '@artifacts/smoke-create.json'
$taskId = 'REPLACE_WITH_RETURNED_ID'
curl.exe -i "$baseUrl/api/tasks"
curl.exe -i "$baseUrl/api/tasks/$taskId"
curl.exe -i -X PUT "$baseUrl/api/tasks/$taskId" -H 'Content-Type: application/json' --data-binary '@artifacts/smoke-update.json'
curl.exe -i -X DELETE "$baseUrl/api/tasks/$taskId"
curl.exe -i "$baseUrl/api/tasks/$taskId"
curl.exe -i -X POST "$baseUrl/api/tasks" -H 'Content-Type: application/json' --data-binary '@artifacts/smoke-invalid.json'
```

| Request | Expected manual check |
| --- | --- |
| Health | 200 and `{"status":"ok"}`; this does not check the database |
| Create | 201, task JSON, nonempty generated ID, UTC creation time, `isCompleted: false`, and a matching `Location` URL |
| List | 200 and an array containing the created task |
| Get by ID | 200 and the same saved task |
| Update | 200, changed title, null description, completed state, unchanged ID and creation time |
| Delete | 204 and an empty body |
| Get deleted task | 404 problem response |
| Invalid create | 400 `application/problem+json` with a title validation error |

Development also exposes `GET /openapi/v1.json`. Production does not. No Swagger UI is installed.

Manual verification on macOS/Apple Silicon on 2026-10-22 used an isolated migrated SQLite database and a loopback listener. All seven task scenarios above returned their expected statuses (201, 200, 200, 200, 204, 404, 400); response fields, Location, and empty delete body were inspected. No automated tests were run locally. Windows/Linux startup and physical-device connectivity have not been exercised in this verification.

## Optional local network access

| Address | Meaning |
| --- | --- |
| `localhost` | The device making the request; Kestrel attempts IPv4 and IPv6 loopback binding |
| `127.0.0.1` | IPv4 loopback on the current device; a phone using it reaches itself, not your computer |
| Host LAN IPv4 address | The computer's address on the private Wi-Fi/Ethernet network; reachable by allowed peers |
| `0.0.0.0` | A server binding to every IPv4 interface, not a client destination; avoid for this setup |

Find the host's active private-network IPv4 address in macOS System Settings > Network, Windows `ipconfig`, or Linux `ip -4 addr`. Select the Wi-Fi/Ethernet interface shared with the phone, not a VPN, loopback, or public-facing interface. DHCP can change this address. Use a configurable debug base URL in mobile apps rather than committing a personal address.

Stop the loopback instance. In the configured API terminal, replace the URL with your host's actual private LAN address. The address below is only an example:

```sh
# macOS/Linux; replace the example address before running.
export ASPNETCORE_URLS=http://192.168.1.50:5080
```

```powershell
# Windows PowerShell; replace the example address before running.
$env:ASPNETCORE_URLS = 'http://192.168.1.50:5080'
```

Run the same `dotnet run ... --no-launch-profile` command above and confirm the listener in the log. Binding only to that address means loopback is no longer served by this instance; use the LAN URL for host-side requests too. Test `http://HOST_LAN_IP:5080/api/health` from the phone on the same trusted network. For explicit dual binding, Kestrel accepts semicolon-separated URLs, but do not use wildcards or public interfaces. [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0)

Keep firewalls enabled. If needed, manually allow only the API process/selected inbound TCP port from the test device or trusted subnet. On Windows restrict the rule to the Private network profile; on macOS review the application firewall and any Local Network privacy prompt; on Linux review the active firewall (for example ufw or firewalld). Do not add router port forwarding, internet tunnels, or broad inbound rules. Guest Wi-Fi/client isolation, VPN routing, corporate policies, and host sleep can prevent connectivity even on the same SSID. Running inside WSL adds a separate networking boundary; the Windows instructions assume a native Windows .NET process.

Plain HTTP provides no encryption or server authentication. Anyone who can reach this unauthenticated API can read, modify, and delete tasks, and local network traffic may be observed or altered. Use disposable data on a trusted network, stop the listener after testing, and keep Development diagnostics off untrusted networks. HTTPS and authentication are required before use with sensitive data or external hosting; this task does not configure either or expose the API to the internet.

### Android and iOS clients

- **Physical Android and iOS devices:** use `http://HOST_LAN_IP:PORT` as the debug base URL, with the host bound to that private address and both devices on a reachable trusted network. Do not use `localhost` or `0.0.0.0` as the phone's server address.
- **Android Emulator:** `http://10.0.2.2:PORT` is the standard emulator alias for the host's IPv4 loopback listener. This is distinct from physical-device LAN access. [Emulator networking](https://developer.android.com/studio/run/emulator-networking-address)
- **Android app configuration:** declare the `INTERNET` permission. Apps targeting Android 9/API 28 or later default to blocking cleartext HTTP. If needed for this disposable setup, use a narrowly scoped network security configuration in the debug build only; do not disable transport protections in release builds. Check additional local-network permission requirements for the chosen Android OS/target SDK during client implementation. [Network security configuration](https://developer.android.com/privacy-and-security/security-config)
- **iOS:** add `NSLocalNetworkUsageDescription` with a clear explanation and handle the user's Local Network permission decision. ATS transport policy is separate from that permission: configure only the debug local-network/IP exception needed for your deployment target, following Apple's current guidance, rather than globally disabling ATS. IP-address behavior differs across OS versions. [Local network privacy](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy) and [ATS local networking](https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsallowslocalnetworking)
- **iOS Simulator on the API's Mac:** can use the Mac's loopback URL; a simulator on another machine needs the API host's LAN address. Simulator success does not verify physical-device permissions: Apple's simulator does not implement local-network privacy enforcement. [Simulator connectivity](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/local-web-services) and [Apple privacy guidance](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy)

Native clients do not need browser CORS permissions. A future browser/WebView client may require a separately reviewed CORS policy. A development HTTPS certificate trusted on the host is not automatically trusted by a phone and may not cover its LAN address; proper certificates and device trust remain future setup work.

## Troubleshooting and current limitations

| Symptom | Check |
| --- | --- |
| SDK not found | Install the SDK architecture and feature band in `global.json`; reopen the terminal and inspect `dotnet --info` |
| `dotnet ef` not found | Run `dotnet tool restore` from the repository root; check NuGet connectivity |
| `--no-build` fails or old behavior runs | Build the API in Release after source changes; the command does not compile automatically |
| Database initialization fails / SQLite schema error | Check the startup error, selected source connection string or packaged path, parent-directory write access, and migration compatibility; startup migrates automatically and never resets a database |
| SQLite cannot open file / database locked | Check parent-directory permissions and other processes using that exact file; do not delete or reset a developer database |
| Address already in use | Choose another port; do not terminate processes you did not start |
| Connection refused or timeout | Check startup logs, scheme/port, host binding, firewall scope, VPN, Wi-Fi isolation, and whether the host is awake |
| HTTP 400 or 415 | Inspect validation errors; send valid JSON with `Content-Type: application/json` |
| Phone fails while host curl succeeds | Check the phone's base URL, LAN reachability, Android cleartext policy or iOS ATS/Local Network permission |
| HTTPS/certificate error | These examples use HTTP; switching the scheme alone does not configure HTTPS or device trust |
| OpenAPI returns 404 | Set `DOTNET_ENVIRONMENT=Development` before startup; only JSON documentation is available |

The API currently has no authentication, user isolation, pagination, mobile clients, or production hosting/CD. Automatic migration startup is available in source and packaged modes; production upgrades still require backups, reviewed migrations, writable persistent storage, and one API process per database during migration. Device connectivity, mobile permissions, and HTTPS trust still need validation during Android/iOS development. SQLite requires writable persistent storage for any eventual hosted deployment. Distribution CI verifies native loopback HTTP on the four target platforms; physical Android/iOS connectivity remains unverified. API contracts and network binding defaults are unchanged. Unsigned packages, HTTP-only examples, and unauthenticated access remain limitations before mobile development.

## Local instructions and prompt history

`AGENTS.md` remains local-only, Git-ignored, and untracked. Optional numbered files under ignored `prompts/` are local historical records, not active instructions. Neither private instructions nor prompt contents should be committed or published.
