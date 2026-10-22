namespace TaskPilot.Api.Services.Tasks;

/// <summary>Manages task persistence and business rules without transport-specific concerns.</summary>
public interface ITaskService
{
    /// <summary>Creates an incomplete task with a server-generated identity and UTC creation time.</summary>
    /// <param name="input">The new task's editable content.</param>
    /// <param name="cancellationToken">Cancels asynchronous database operations.</param>
    /// <returns>The saved task snapshot.</returns>
    /// <exception cref="ArgumentException">The input is null or its content violates the title or description limits.</exception>
    Task<TaskResult> CreateAsync(CreateTaskInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists tasks in ascending creation-time order, breaking ties by identity.</summary>
    /// <param name="cancellationToken">Cancels asynchronous database operations.</param>
    /// <returns>Task snapshots, or an empty list when no tasks exist.</returns>
    Task<IReadOnlyList<TaskResult>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves a task by its identity.</summary>
    /// <param name="id">The task identity to look up.</param>
    /// <param name="cancellationToken">Cancels asynchronous database operations.</param>
    /// <returns>The task snapshot, or null when the task does not exist.</returns>
    Task<TaskResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces editable fields while preserving identity and creation time.</summary>
    /// <param name="id">The task identity to update.</param>
    /// <param name="input">The replacement content and completion state, validated before lookup.</param>
    /// <param name="cancellationToken">Cancels asynchronous database operations.</param>
    /// <returns>The saved snapshot, or null when the task does not exist.</returns>
    /// <exception cref="ArgumentException">The input is null or its content violates the title or description limits.</exception>
    Task<TaskResult?> UpdateAsync(Guid id, UpdateTaskInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes an existing task.</summary>
    /// <param name="id">The task identity to delete.</param>
    /// <param name="cancellationToken">Cancels asynchronous database operations.</param>
    /// <returns>True when a task was deleted; false when the task does not exist.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
