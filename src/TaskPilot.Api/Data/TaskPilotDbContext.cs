using Microsoft.EntityFrameworkCore;
using TaskPilot.Api.Data.Configurations;
using TaskPilot.Api.Entities;

namespace TaskPilot.Api.Data;

/// <summary>
/// Provides EF Core access to persisted tasks and their relational schema.
/// </summary>
public sealed class TaskPilotDbContext : DbContext
{
    /// <summary>
    /// Creates a context using the provider and connection supplied by the caller.
    /// </summary>
    public TaskPilotDbContext(DbContextOptions<TaskPilotDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>
    /// Applies the task schema configuration to this context's model.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
    }
}
