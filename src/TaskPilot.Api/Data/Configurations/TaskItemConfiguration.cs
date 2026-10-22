using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskPilot.Api.Entities;

namespace TaskPilot.Api.Data.Configurations;

/// <summary>
/// Defines task storage, intended text limits, and UTC timestamp materialization.
/// </summary>
public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    /// <summary>
    /// Configures required fields and preserves UTC semantics when reading SQLite timestamps.
    /// </summary>
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Property(task => task.Title).IsRequired().HasMaxLength(200);
        builder.Property(task => task.Description).IsRequired(false).HasMaxLength(2000);
        builder.Property(task => task.IsCompleted).IsRequired();
        builder.Property(task => task.CreatedAtUtc)
            .IsRequired()
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
