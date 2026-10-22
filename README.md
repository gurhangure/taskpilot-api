# TaskPilot API

TaskPilot is a task-management portfolio product with an ASP.NET Core Web API and planned native clients for Android (Kotlin/Jetpack Compose) and iOS (Swift/SwiftUI). This repository contains the backend scaffold, integration tests, and a continuous integration workflow.

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
├── global.json
├── README.md
├── TaskPilot.slnx
├── prompts/                       # Local, Git-ignored prompt history
├── src/TaskPilot.Api/
│   ├── TaskPilot.Api.csproj
│   ├── Program.cs
│   ├── Controllers/HealthController.cs
│   └── Models/HealthResponse.cs
└── tests/TaskPilot.Api.IntegrationTests/
    ├── TaskPilot.Api.IntegrationTests.csproj
    └── ApiContractTests.cs
```

The intended architecture is **Controllers → Services → Data access**, using constructor injection as dependencies become necessary. Controllers handle HTTP concerns, services will contain business logic, and data access will handle persistence. Only the health controller and its response model are implemented; services and data access are planned, with no empty layers or additional projects yet. There is no database, authentication, or task API in this step.

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
