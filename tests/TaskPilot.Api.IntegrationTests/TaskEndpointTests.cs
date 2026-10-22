using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TaskPilot.Api.Data;
using TaskPilot.Api.Services.Tasks;
using Xunit;

namespace TaskPilot.Api.IntegrationTests;

/// <summary>Exercises task HTTP contracts through the real application and isolated migrated SQLite databases.</summary>
public sealed class TaskEndpointTests : IAsyncLifetime
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("taskpilot-http-");
    private WebApplicationFactory<Program>? _factory;
    private WebApplicationFactory<Program>? _host;
    private HttpClient _client = null!;
    private string DatabasePath => Path.Combine(_directory.FullName, "tasks.db");

    /// <summary>Migrates a private database and starts an application configured to use only that database.</summary>
    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        }.ToString();
        await using (var context = new TaskPilotDbContext(new DbContextOptionsBuilder<TaskPilotDbContext>()
            .UseSqlite(connectionString).Options))
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>();
        _host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:TaskPilot"] = connectionString }));
        });
        _client = _host.CreateClient();
    }

    /// <summary>Disposes the host before removing only this test's SQLite files.</summary>
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        File.Delete(DatabasePath);
        File.Delete(DatabasePath + "-journal");
        File.Delete(DatabasePath + "-wal");
        File.Delete(DatabasePath + "-shm");
        _directory.Delete();
    }

    /// <summary>Verifies all CRUD status codes, creation location, JSON values, and persistence between requests.</summary>
    [Fact]
    public async Task Crud_ReturnsExpectedContractsAndPersistsChanges()
    {
        using var emptyResponse = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<TaskResult[]>())!);

        var before = DateTime.UtcNow;
        using var createdResponse = await _client.PostAsJsonAsync("/api/tasks", new
        {
            title = "Write API tests", description = "Exercise the real pipeline",
            id = Guid.Empty, isCompleted = true, createdAtUtc = "2000-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal("application/json", createdResponse.Content.Headers.ContentType?.MediaType);
        var created = (await createdResponse.Content.ReadFromJsonAsync<TaskResult>())!;
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.False(created.IsCompleted);
        Assert.Equal("Write API tests", created.Title);
        Assert.Equal("Exercise the real pipeline", created.Description);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAtUtc.Kind);
        Assert.InRange(created.CreatedAtUtc, before, DateTime.UtcNow);
        Assert.NotNull(createdResponse.Headers.Location);
        Assert.EndsWith($"/api/tasks/{created.Id}", createdResponse.Headers.Location.ToString());

        using var getResponse = await _client.GetAsync(createdResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created, await getResponse.Content.ReadFromJsonAsync<TaskResult>());
        Assert.Equal(created, Assert.Single((await _client.GetFromJsonAsync<TaskResult[]>("/api/tasks"))!));

        using var updateResponse = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new
        {
            title = "Completed tests", description = (string?)null, isCompleted = true
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var expected = created with { Title = "Completed tests", Description = null, IsCompleted = true };
        Assert.Equal(expected, await updateResponse.Content.ReadFromJsonAsync<TaskResult>());
        Assert.Equal(expected, await _client.GetFromJsonAsync<TaskResult>($"/api/tasks/{created.Id}"));

        using var deleteResponse = await _client.DeleteAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(string.Empty, await deleteResponse.Content.ReadAsStringAsync());
        using var missingResponse = await _client.GetAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<TaskResult[]>("/api/tasks"))!);
    }

    /// <summary>Confirms missing resources produce 404 for retrieval, update, and deletion.</summary>
    [Fact]
    public async Task MissingTasks_ReturnNotFound()
    {
        var path = $"/api/tasks/{Guid.NewGuid()}";
        using var get = await _client.GetAsync(path);
        using var put = await _client.PutAsJsonAsync(path, new { title = "Missing", isCompleted = true });
        using var delete = await _client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<TaskResult[]>("/api/tasks"))!);
    }

    /// <summary>Supplies invalid content, malformed JSON, missing bodies, and incompatible JSON types.</summary>
    /// <returns>Request bodies that must produce standard validation responses for both writes.</returns>
    public static IEnumerable<object[]> InvalidBodies()
    {
        yield return new object[] { "{}" };
        yield return new object[] { "{\"title\":null}" };
        yield return new object[] { "{\"title\":\"\"}" };
        yield return new object[] { "{\"title\":\"   \"}" };
        yield return new object[] { JsonSerializer.Serialize(new { title = new string('t', 201) }) };
        yield return new object[] { JsonSerializer.Serialize(new { title = "Valid", description = new string('d', 2001) }) };
        yield return new object[] { "{\"title\":42}" };
        yield return new object[] { "{\"title\":\"Valid\",\"description\":42}" };
        yield return new object[] { "{" };
        yield return new object[] { "null" };
        yield return new object[] { "" };
    }

    /// <summary>Confirms invalid writes use consistent validation problems and leave stored tasks unchanged.</summary>
    /// <param name="body">The invalid JSON request body.</param>
    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidWrites_ReturnValidationProblemsWithoutChangingTasks(string body)
    {
        using var seed = await _client.PostAsJsonAsync("/api/tasks", new { title = "Keep", description = "Original" });
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);
        var original = (await seed.Content.ReadFromJsonAsync<TaskResult>())!;
        using var postBody = new StringContent(body, Encoding.UTF8, "application/json");
        using var putBody = new StringContent(body, Encoding.UTF8, "application/json");
        using var post = await _client.PostAsync("/api/tasks", postBody);
        using var put = await _client.PutAsync($"/api/tasks/{original.Id}", putBody);
        await AssertValidationProblemAsync(post);
        await AssertValidationProblemAsync(put);
        Assert.Equal(original, Assert.Single((await _client.GetFromJsonAsync<TaskResult[]>("/api/tasks"))!));
    }

    /// <summary>Confirms invalid identifiers and completion types use the same model-validation response format.</summary>
    [Fact]
    public async Task InvalidIdentifiersAndCompletion_ReturnValidationProblems()
    {
        using var get = await _client.GetAsync("/api/tasks/not-a-guid");
        using var delete = await _client.DeleteAsync("/api/tasks/not-a-guid");
        using var put = await _client.PutAsJsonAsync("/api/tasks/not-a-guid", new { title = "Valid" });
        using var invalidCompletion = await _client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}",
            new { title = "Valid", isCompleted = "invalid" });
        await AssertValidationProblemAsync(get);
        await AssertValidationProblemAsync(delete);
        await AssertValidationProblemAsync(put);
        await AssertValidationProblemAsync(invalidCompletion);
    }

    /// <summary>Accepts the exact text limits and documents omitted update fields through observable behavior.</summary>
    [Fact]
    public async Task ValidBoundariesAndOmittedFields_AreAccepted()
    {
        var title = new string('t', 200);
        var description = new string('d', 2000);
        using var post = await _client.PostAsJsonAsync("/api/tasks", new { title, description });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = (await post.Content.ReadFromJsonAsync<TaskResult>())!;
        Assert.Equal(title, created.Title);
        Assert.Equal(description, created.Description);
        using var completed = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title, description, isCompleted = true });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var reopened = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(created with { Description = null }, await reopened.Content.ReadFromJsonAsync<TaskResult>());
    }

    /// <summary>Confirms controller discovery exposes every task operation in the Development OpenAPI document.</summary>
    [Fact]
    public async Task OpenApi_DescribesTaskRoutesAndSuccessCodes()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/tasks").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/tasks").GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        var item = paths.GetProperty("/api/tasks/{id}");
        Assert.True(item.GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(item.GetProperty("put").GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(item.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));
    }

    /// <summary>Checks the shared HTTP validation-problem contract without relying on framework-specific message wording.</summary>
    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.NotEmpty(document.RootElement.GetProperty("errors").EnumerateObject());
    }
}
