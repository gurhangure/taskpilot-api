using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace TaskPilot.Api.IntegrationTests;

/// <summary>
/// Verifies public HTTP contracts through application startup, routing, and response serialization.
/// </summary>
public sealed class ApiContractTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("taskpilot-contract-");

    /// <summary>Configures an isolated source database for the HTTP contract host.</summary>
    private WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TaskPilot:Packaged"] = "false",
                    ["ConnectionStrings:TaskPilot"] = new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(_directory.FullName, "contracts.db"), Pooling = false
                    }.ToString()
                }));
        });

    /// <summary>Removes only this test's private database after its host has been disposed.</summary>
    public void Dispose() => _directory.Delete(true);

    /// <summary>
    /// Confirms that the health route returns the expected JSON contract in Production.
    /// </summary>
    [Fact]
    public async Task Health_ReturnsOkWithJsonStatus()
    {
        await using var host = CreateFactory(Environments.Production);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// Confirms that Development exposes an OpenAPI document describing the health operation.
    /// </summary>
    [Fact]
    public async Task OpenApi_InDevelopment_DescribesHealthEndpoint()
    {
        await using var host = CreateFactory(Environments.Development);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.StartsWith("3.", root.GetProperty("openapi").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("info").GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("info").GetProperty("version").GetString()));
        var operation = root.GetProperty("paths").GetProperty("/api/health").GetProperty("get");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
    }

    /// <summary>
    /// Confirms that Production does not expose the development OpenAPI route.
    /// </summary>
    [Fact]
    public async Task OpenApi_InProduction_ReturnsNotFound()
    {
        await using var host = CreateFactory(Environments.Production);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
