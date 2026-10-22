using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskPilot.Api.Data;
using TaskPilot.Api.Services.Tasks;
using Xunit;

namespace TaskPilot.Api.IntegrationTests;

/// <summary>Verifies service behavior against isolated SQLite databases using committed migrations.</summary>
public sealed class TaskServiceTests : IAsyncLifetime
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("taskpilot-service-");
    private string DatabasePath => Path.Combine(_directory.FullName, "tasks.db");
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Pooling = false
    }.ToString();

    /// <summary>Applies real migrations to this test's private database before service operations.</summary>
    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Removes only this test's database and SQLite companion files after contexts are disposed.</summary>
    public Task DisposeAsync()
    {
        File.Delete(DatabasePath);
        File.Delete(DatabasePath + "-journal");
        File.Delete(DatabasePath + "-wal");
        File.Delete(DatabasePath + "-shm");
        _directory.Delete();
        return Task.CompletedTask;
    }

    /// <summary>Confirms creation defaults, unique identities, UTC timestamps, and reads through a fresh context.</summary>
    [Fact]
    public async Task CreateAndRead_PersistServerAssignedFields()
    {
        TaskResult first;
        TaskResult second;
        var before = DateTime.UtcNow;
        await using (var context = CreateContext())
        {
            var service = new TaskService(context);
            Assert.Empty(await service.ListAsync());
            first = await service.CreateAsync(new CreateTaskInput("First task", "Details"));
            second = await service.CreateAsync(new CreateTaskInput("Second task"));
        }

        var after = DateTime.UtcNow;
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal("First task", first.Title);
        Assert.Equal("Details", first.Description);
        Assert.Null(second.Description);
        Assert.InRange(first.CreatedAtUtc, before, after);
        Assert.InRange(second.CreatedAtUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAtUtc.Kind);
        await using var readContext = CreateContext();
        var reader = new TaskService(readContext);
        Assert.Equal(first, await reader.GetByIdAsync(first.Id));
        var results = await reader.ListAsync();
        Assert.Equal(2, results.Count);
        Assert.Contains(first, results);
        Assert.Contains(second, results);
        Assert.True(results[0].CreatedAtUtc <= results[1].CreatedAtUtc);
        Assert.All(results, task => Assert.Equal(DateTimeKind.Utc, task.CreatedAtUtc.Kind));
    }

    /// <summary>Confirms updates replace editable fields, clear descriptions, and preserve server-assigned fields.</summary>
    [Fact]
    public async Task Update_PersistsReplacementAndPreservesIdentityAndCreationTime()
    {
        TaskResult original;
        await using (var context = CreateContext())
        {
            original = await new TaskService(context).CreateAsync(new CreateTaskInput("Original", "Remove me"));
        }

        await using (var context = CreateContext())
        {
            var updated = await new TaskService(context).UpdateAsync(original.Id, new UpdateTaskInput("Updated", null, true));
            Assert.Equal(original with { Title = "Updated", Description = null, IsCompleted = true }, updated);
        }

        await using (var context = CreateContext())
        {
            var service = new TaskService(context);
            var persisted = await service.GetByIdAsync(original.Id);
            Assert.Equal(original with { Title = "Updated", Description = null, IsCompleted = true }, persisted);
            await service.UpdateAsync(original.Id, new UpdateTaskInput("Reopened", "Restored", false));
        }

        await using var readContext = CreateContext();
        Assert.Equal(original with { Title = "Reopened", Description = "Restored" },
            await new TaskService(readContext).GetByIdAsync(original.Id));
    }

    /// <summary>Confirms deletion persists and missing identities have consistent absence results.</summary>
    [Fact]
    public async Task DeleteAndMissingTasks_ReturnAbsenceWithoutChangingOtherTasks()
    {
        TaskResult removed;
        TaskResult retained;
        await using (var context = CreateContext())
        {
            var service = new TaskService(context);
            removed = await service.CreateAsync(new CreateTaskInput("Remove"));
            retained = await service.CreateAsync(new CreateTaskInput("Keep"));
            Assert.True(await service.DeleteAsync(removed.Id));
        }

        await using var readContext = CreateContext();
        var reader = new TaskService(readContext);
        foreach (var id in new[] { removed.Id, Guid.NewGuid(), Guid.Empty })
        {
            Assert.Null(await reader.GetByIdAsync(id));
            Assert.Null(await reader.UpdateAsync(id, new UpdateTaskInput("Missing", null, true)));
            Assert.False(await reader.DeleteAsync(id));
        }

        Assert.Equal(retained, Assert.Single(await reader.ListAsync()));
    }

    /// <summary>Rejects blank and oversized content without creating or modifying stored tasks.</summary>
    /// <param name="title">The invalid title, or a valid title when testing description length.</param>
    /// <param name="descriptionLength">The description length to validate.</param>
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Valid", 2001)]
    public async Task InvalidContent_DoesNotChangePersistence(string? title, int descriptionLength)
    {
        await AssertInvalidContentAsync(title!, new string('d', descriptionLength));
    }

    /// <summary>Rejects titles beyond the mapped limit even though SQLite does not enforce that limit.</summary>
    [Fact]
    public async Task OversizedTitle_DoesNotChangePersistence()
    {
        await AssertInvalidContentAsync(new string('t', 201), null);
    }

    /// <summary>Accepts exact content limits and rejects null input objects.</summary>
    [Fact]
    public async Task ContentLimits_AcceptBoundariesAndRejectNullInputs()
    {
        await using var context = CreateContext();
        var service = new TaskService(context);
        var title = new string('t', 200);
        var description = new string('d', 2000);
        var created = await service.CreateAsync(new CreateTaskInput(title, description));
        Assert.Equal(title, created.Title);
        Assert.Equal(description, created.Description);
        Assert.NotNull(await service.UpdateAsync(created.Id, new UpdateTaskInput(title, description, true)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateAsync(created.Id, null!));
    }

    /// <summary>Confirms every operation observes cancellation without changing saved content.</summary>
    [Fact]
    public async Task CancelledOperations_DoNotChangePersistence()
    {
        TaskResult original;
        await using (var context = CreateContext())
        {
            var service = new TaskService(context);
            original = await service.CreateAsync(new CreateTaskInput("Keep"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var token = cancellation.Token;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(new CreateTaskInput("Cancelled"), token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListAsync(token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(original.Id, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(original.Id, new UpdateTaskInput("Cancelled", null, true), token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(original.Id, token));
        }

        await using var readContext = CreateContext();
        Assert.Equal(original, Assert.Single(await new TaskService(readContext).ListAsync()));
    }

    /// <summary>Resolves the actual application registration with one service per scope and an isolated database.</summary>
    [Fact]
    public async Task DependencyInjection_RegistersScopedService()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:TaskPilot"] = ConnectionString })));
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ITaskService>();
        var second = secondScope.ServiceProvider.GetRequiredService<ITaskService>();
        Assert.IsType<TaskService>(first);
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<ITaskService>());
        Assert.NotSame(first, second);
        var created = await first.CreateAsync(new CreateTaskInput("Resolved through DI"));
        Assert.Equal(created, await second.GetByIdAsync(created.Id));
    }

    /// <summary>Verifies invalid creates and updates leave the original task unchanged through a fresh context.</summary>
    private async Task AssertInvalidContentAsync(string title, string? description)
    {
        TaskResult original;
        await using (var context = CreateContext())
        {
            var service = new TaskService(context);
            original = await service.CreateAsync(new CreateTaskInput("Original", "Keep"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateTaskInput(title, description)));
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(original.Id, new UpdateTaskInput(title, description, true)));
        }

        await using var readContext = CreateContext();
        Assert.Equal(original, Assert.Single(await new TaskService(readContext).ListAsync()));
    }

    /// <summary>Creates a new context targeting only this test's temporary SQLite database.</summary>
    private TaskPilotDbContext CreateContext() => new(new DbContextOptionsBuilder<TaskPilotDbContext>()
        .UseSqlite(ConnectionString).Options);
}
