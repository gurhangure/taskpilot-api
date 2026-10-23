using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskPilot.Api.Data;
using TaskPilot.Api.Services.Tasks;
using Xunit;

namespace TaskPilot.Api.IntegrationTests;

/// <summary>Verifies both startup modes using private SQLite files without manual migration setup.</summary>
public sealed class PackagedStartupTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("taskpilot-packaged-");

    /// <summary>Migrates the selected database on first startup and preserves tasks across subsequent startups.</summary>
    /// <param name="packaged">Whether startup uses packaged storage or the source connection string.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_MigratesNewDatabaseAndPreservesExistingTasks(bool packaged)
    {
        var path = Path.Combine(_directory.FullName, packaged ? "nested" : "", "tasks.db");
        TaskResult? created;
        await using (var factory = CreateFactory(path, packaged))
        {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync("/api/tasks", new { title = "Survive restart" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = await response.Content.ReadFromJsonAsync<TaskResult>();
            Assert.NotNull(created);
            Assert.False(File.Exists(path + ".unused"));
        }

        await using (var factory = CreateFactory(path, packaged))
        {
            using var client = factory.CreateClient();
            Assert.Equal(created, await client.GetFromJsonAsync<TaskResult>($"/api/tasks/{created.Id}"));
        }

        await using var context = new TaskPilotDbContext(new DbContextOptionsBuilder<TaskPilotDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options);
        Assert.Equal("20261022102839_InitialCreate", Assert.Single(await context.Database.GetAppliedMigrationsAsync()));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>Migrates an existing empty source database before the health endpoint can respond.</summary>
    [Fact]
    public async Task SourceStartup_MigratesExistingEmptyDatabase()
    {
        var path = Path.Combine(_directory.FullName, "development.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
        }

        await using var factory = CreateFactory(path, false);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = new TaskPilotDbContext(new DbContextOptionsBuilder<TaskPilotDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options);
        Assert.Equal("20261022102839_InitialCreate", Assert.Single(await context.Database.GetAppliedMigrationsAsync()));
    }

    /// <summary>Rejects an unusable database without resetting its contents or serving requests.</summary>
    /// <param name="packaged">The execution mode whose database must remain intact.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptDatabase_PreventsStartupWithoutReset(bool packaged)
    {
        var path = Path.Combine(_directory.FullName, "invalid.db");
        await File.WriteAllTextAsync(path, "This is not a SQLite database.");
        var before = await File.ReadAllBytesAsync(path);
        await using var factory = CreateFactory(path, packaged);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("TaskPilot database initialization failed", exception.ToString());
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    /// <summary>Stops startup when the intended parent directory is occupied by a file.</summary>
    /// <param name="packaged">The execution mode whose database directory is unusable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDirectory_PreventsStartup(bool packaged)
    {
        var parent = Path.Combine(_directory.FullName, "blocked");
        await File.WriteAllTextAsync(parent, "Keep this file.");
        await using var factory = CreateFactory(Path.Combine(parent, "tasks.db"), packaged);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("TaskPilot database initialization failed", exception.ToString());
        Assert.Equal("Keep this file.", await File.ReadAllTextAsync(parent));
    }

    /// <summary>Creates an application host whose database settings target only this test's temporary directory.</summary>
    private static WebApplicationFactory<Program> CreateFactory(string path, bool packaged) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TaskPilot:Packaged"] = packaged.ToString(),
                    ["TaskPilot:DatabasePath"] = packaged ? path : path + ".unused",
                    ["ConnectionStrings:TaskPilot"] = new SqliteConnectionStringBuilder
                    {
                        DataSource = packaged ? path + ".unused" : path, Pooling = false
                    }.ToString()
                })));

    /// <summary>Releases SQLite pools and deletes only this test's private temporary directory.</summary>
    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(_directory.FullName, "*.db", SearchOption.AllDirectories))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
        }
        _directory.Delete(true);
    }
}
