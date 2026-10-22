using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskPilot.Api.Data;
using TaskPilot.Api.Entities;
using Xunit;

namespace TaskPilot.Api.IntegrationTests;

/// <summary>
/// Verifies real SQLite migrations and persistence across independent context instances.
/// </summary>
public sealed class PersistenceTests
{
    /// <summary>
    /// Applies migrations to an isolated database and reads saved task values through a fresh context.
    /// </summary>
    [Fact]
    public async Task Migrations_AllowTaskToPersistAcrossContexts()
    {
        var directory = Directory.CreateTempSubdirectory("taskpilot-persistence-");
        var databasePath = Path.Combine(directory.FullName, "tasks.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<TaskPilotDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "Verify SQLite persistence",
            Description = "Read this task through a fresh context.",
            CreatedAtUtc = new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Utc)
        };

        try
        {
            await using (var writeContext = new TaskPilotDbContext(options))
            {
                await writeContext.Database.MigrateAsync();
                writeContext.Tasks.Add(task);
                await writeContext.SaveChangesAsync();
            }

            await using (var readContext = new TaskPilotDbContext(options))
            {
                var persisted = await readContext.Tasks.AsNoTracking().SingleAsync();
                Assert.Equal(task.Id, persisted.Id);
                Assert.Equal(task.Title, persisted.Title);
                Assert.Equal(task.Description, persisted.Description);
                Assert.False(persisted.IsCompleted);
                Assert.Equal(task.CreatedAtUtc, persisted.CreatedAtUtc);
                Assert.Equal(DateTimeKind.Utc, persisted.CreatedAtUtc.Kind);
            }
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(databasePath + "-journal");
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
            directory.Delete();
        }
    }
}
