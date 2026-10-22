# TaskPilot API

TaskPilot is a task-management portfolio product with an ASP.NET Core Web API and planned native clients for Android (Kotlin/Jetpack Compose) and iOS (Swift/SwiftUI). This repository contains the backend scaffold, an EF Core SQLite persistence foundation, a task service layer, integration tests, and a continuous integration workflow.

## Prerequisites

- .NET SDK 10.0.401 (developed on macOS with Apple Silicon).
- Git and curl for repository management and local verification.

`global.json` targets SDK 10.0.401, allows the latest patch in its feature band, and disables prerelease SDKs. The API targets `net10.0`.

## Current structure

```text
taskpilot-api/
├── .github/workflows/ci.yml
├── .gitignore
├── AGENTS.md
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
│   └── Models/HealthResponse.cs
└── tests/TaskPilot.Api.IntegrationTests/
    ├── TaskPilot.Api.IntegrationTests.csproj
    ├── ApiContractTests.cs
    ├── PersistenceTests.cs
    └── TaskServiceTests.cs
```

The architecture is **Controllers → Services → Data access**, using constructor injection. The health controller and response model are implemented, along with the task entity, EF Core data access, and task service layer. Task HTTP endpoints are still planned; there are no task controllers, CRUD endpoints, repositories, authentication, or custom unit-of-work wrappers.

## Task service

`ITaskService` and `TaskService` live under `Services/Tasks`. The service is registered with a scoped lifetime matching `TaskPilotDbContext`, which it receives directly through constructor injection. It owns task creation defaults, content validation, and persistence. Future controllers will translate service results into HTTP responses; the service has no HTTP dependencies.

| Operation | Input | Result |
| --- | --- | --- |
| `CreateAsync` | `CreateTaskInput` with title and optional description | Saved `TaskResult` |
| `ListAsync` | None | All snapshots ordered by creation time, then ID; empty list when no tasks exist |
| `GetByIdAsync` | Task ID | Snapshot, or `null` if missing |
| `UpdateAsync` | Task ID and `UpdateTaskInput` | Saved snapshot, or `null` if missing |
| `DeleteAsync` | Task ID | `true` if deleted, `false` if missing |

All operations accept an optional `CancellationToken` and use EF Core asynchronous database APIs. Each successful mutation saves before returning. Results are immutable `TaskResult` snapshots; the public contract exposes neither tracked entities nor queryables. Missing tasks are normal absence results rather than exceptions or HTTP response types.

Creation generates a new GUID, sets `CreatedAtUtc` with `DateTime.UtcNow`, and initializes `IsCompleted` to false. Updates replace title, description, and completion state while preserving identity and creation time; a null description clears it. Create and update reject null inputs, blank titles, titles longer than 200 characters, and descriptions longer than 2000 characters with `ArgumentException` (including `ArgumentNullException` for null input objects). Content is preserved without trimming. Update validates content before looking up the task. Cancellation and database failures propagate to the caller.

## Persistence and migrations

`TaskItem` holds a server-assigned `Guid` ID, required title (200 characters), optional description (2000 characters), completion state (initially false), and UTC creation time. The task service assigns IDs and UTC timestamps. Code writing directly through the context must supply `CreatedAtUtc` in UTC; the EF mapping restores `DateTimeKind.Utc` on reads because SQLite storage does not preserve that .NET metadata.

`TaskPilotDbContext` exposes `Tasks` and tracks database changes. `TaskItemConfiguration` defines the primary key, required fields, and intended text lengths. SQLite does **not** enforce string lengths merely because `HasMaxLength` is configured; the task service enforces these limits for service callers. [SQLite type limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types)

Dependency injection registers the context with the named `TaskPilot` connection string in `appsettings.json`: `Data Source=taskpilot.db`. SQLite resolves relative paths against the process working directory. With the documented `dotnet run --project src/TaskPilot.Api/TaskPilot.Api.csproj ...` command, that directory is `src/TaskPilot.Api`, so the file is `src/TaskPilot.Api/taskpilot.db`. Override the connection string through `ConnectionStrings__TaskPilot` when needed.

The EF CLI-generated `InitialCreate` migration creates the `Tasks` table with all five fields, its primary key, and nullable description. The model snapshot records the schema for future migration comparisons. Startup does not apply migrations or create/recreate a database.

From the repository root, restore the pinned local tool:

```sh
dotnet tool restore
```

The initial migration was generated with this command (it is already committed; do not regenerate it):

```sh
dotnet ef migrations add InitialCreate --project src/TaskPilot.Api/TaskPilot.Api.csproj --startup-project src/TaskPilot.Api/TaskPilot.Api.csproj --output-dir Data/Migrations
```

For future model changes, use a new descriptive migration name in that command. Review generated migrations before applying them. To explicitly apply the committed migrations to the local developer database from the repository root on macOS, use an absolute connection path so the CLI and application target the same file:

```sh
dotnet ef database update --project src/TaskPilot.Api/TaskPilot.Api.csproj --startup-project src/TaskPilot.Api/TaskPilot.Api.csproj --connection "Data Source=$PWD/src/TaskPilot.Api/taskpilot.db"
```

This command applies migrations without deleting or resetting an existing database. Local database files and journal/WAL/SHM companions are ignored by Git; migration source files are versioned. Production SQLite requires persistent storage for its database files. Hosting and automated deployment are not configured.

## Build, test, and publish

CI runs the following sequence from the repository root. Under the repository's Test Execution Policy, agents run tests through GitHub Actions by default and need explicit user authorization to run tests locally. Local builds are used only to resolve concrete compilation concerns.

```sh
dotnet restore TaskPilot.slnx
dotnet build TaskPilot.slnx --configuration Release --no-restore
dotnet test tests/TaskPilot.Api.IntegrationTests/TaskPilot.Api.IntegrationTests.csproj --configuration Release --no-build --logger "trx;LogFileName=integration-tests.trx" --results-directory artifacts/test-results
dotnet publish src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --no-build --output artifacts/publish
```

The three xUnit integration tests use `WebApplicationFactory` to run the actual application pipeline in memory, without an external server. They verify the health endpoint's HTTP 200, JSON content type, and status property; the Development OpenAPI document and its health operation; and HTTP 404 for OpenAPI in Production. Each test explicitly selects its environment and disposes its hosts and HTTP client. The public partial `Program` declaration provides test access to the top-level application entry point.

Historical verification: all three initial tests passed locally before the CI-first test execution policy was adopted. This does not establish the result of any later CI run.

The persistence integration test uses the actual SQLite provider and a uniquely named temporary database. It applies the committed migrations with `MigrateAsync`, saves a task, disposes the writing context, and verifies all fields and UTC timestamp kind through a fresh context. Pooling is disabled and only that test's temporary files are cleaned up after contexts are disposed. It never uses the developer database.

`TaskServiceTests` applies the same migrations to a separate temporary SQLite database for every test case. Coverage includes creation defaults and unique IDs, listing and retrieval, updates preserving server fields, description clearing, deletion, missing tasks, invalid content, exact length boundaries, cancellation, and scoped application DI registration. Persistence assertions use fresh contexts. The DI test overrides the application connection string with its own isolated database. The existing CI command discovers all tests without workflow changes. Service tests are not run locally; their execution status must be checked in GitHub Actions for the relevant commit.

TRX results are written to `artifacts/test-results/` and published API files to `artifacts/publish/`. These generated files are Git-ignored. Publishing creates application files; it does not deploy them.

## Continuous integration and planned deployment

The GitHub Actions workflow runs on pushes and pull requests targeting `main`, or manually through `workflow_dispatch`. It uses an Ubuntu runner with only `contents: read` permissions and installs the SDK specified by `global.json`. It restores, builds in Release, runs integration tests, uploads available TRX results even when tests fail, and publishes and uploads the API only after successful tests.

This is CI configuration, not evidence of a successful GitHub Actions run. Hosting and automated deployment are not configured yet. Continuous deployment (CD) will be implemented once the hosting target is selected.

## Run locally

After the Release build above, from the repository root:

```sh
DOTNET_ENVIRONMENT=Development dotnet run --project src/TaskPilot.Api/TaskPilot.Api.csproj --configuration Release --no-build --no-launch-profile --urls http://localhost:5080
```

The explicit HTTP address is for local development only, not future production hosting configuration. Stop the API with Ctrl+C.

In another terminal:

```sh
curl -i http://localhost:5080/api/health
```

Expected: HTTP 200 with `{"status":"ok"}`. This endpoint confirms the API responds; it does not check external dependencies.

Built-in OpenAPI document generation is registered and exposed only in Development. With the command above, access the JSON document in a browser or with:

```sh
curl -i http://localhost:5080/openapi/v1.json
```

No Swagger UI or other documentation UI is installed.

## Prompt history

`AGENTS.md` contains project rules and the local prompt history convention. Full implementation prompts are stored in numbered files under Git-ignored `prompts/`; they are historical records, with secrets redacted. Keep `AGENTS.md` in version control and prompt history local.
