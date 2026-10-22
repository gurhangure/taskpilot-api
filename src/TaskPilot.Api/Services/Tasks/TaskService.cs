using Microsoft.EntityFrameworkCore;
using TaskPilot.Api.Data;
using TaskPilot.Api.Entities;

namespace TaskPilot.Api.Services.Tasks;

/// <summary>Applies task rules and persists changes through the scoped EF Core context.</summary>
public sealed class TaskService : ITaskService
{
    private readonly TaskPilotDbContext _context;

    /// <summary>Creates a service using the context supplied by dependency injection.</summary>
    /// <param name="context">The scoped task persistence context.</param>
    public TaskService(TaskPilotDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<TaskResult> CreateAsync(CreateTaskInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        ValidateContent(input.Title, input.Description);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = input.Title,
            Description = input.Description,
            IsCompleted = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.Tasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);
        return ToResult(task);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskResult>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Tasks.AsNoTracking()
            .OrderBy(task => task.CreatedAtUtc).ThenBy(task => task.Id)
            .Select(task => new TaskResult(task.Id, task.Title, task.Description, task.IsCompleted, task.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TaskResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks.AsNoTracking()
            .SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        return task is null ? null : ToResult(task);
    }

    /// <inheritdoc />
    public async Task<TaskResult?> UpdateAsync(Guid id, UpdateTaskInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        ValidateContent(input.Title, input.Description);
        var task = await _context.Tasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        task.Title = input.Title;
        task.Description = input.Description;
        task.IsCompleted = input.IsCompleted;
        await _context.SaveChangesAsync(cancellationToken);
        return ToResult(task);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (task is null)
        {
            return false;
        }

        _context.Tasks.Remove(task);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Rejects invalid content before it can alter persisted tasks.</summary>
    /// <exception cref="ArgumentException">The title is blank or either field exceeds its storage limit.</exception>
    private static void ValidateContent(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("A nonblank title of at most 200 characters is required.", nameof(title));
        }

        if (description?.Length > 2000)
        {
            throw new ArgumentException("The description must contain at most 2000 characters.", nameof(description));
        }
    }

    /// <summary>Copies saved values into an immutable service result.</summary>
    private static TaskResult ToResult(TaskItem task) =>
        new(task.Id, task.Title, task.Description, task.IsCompleted, task.CreatedAtUtc);
}
