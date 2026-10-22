using Microsoft.AspNetCore.Mvc;
using TaskPilot.Api.Models.Tasks;
using TaskPilot.Api.Services.Tasks;

namespace TaskPilot.Api.Controllers;

/// <summary>Translates task HTTP requests and service results without owning persistence or business rules.</summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ITaskService _taskService;

    /// <summary>Creates the controller with the application's task service.</summary>
    /// <param name="taskService">The service responsible for task rules and persistence.</param>
    public TasksController(ITaskService taskService)
    {
        _taskService = taskService;
    }

    /// <summary>Creates an incomplete task and provides its retrieval URL.</summary>
    /// <param name="request">The validated title and optional description.</param>
    /// <param name="cancellationToken">Propagates request cancellation to the service.</param>
    /// <returns>HTTP 201 with the saved task and a Location header.</returns>
    [HttpPost]
    [ProducesResponseType<TaskResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskResult>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await _taskService.CreateAsync(new CreateTaskInput(request.Title, request.Description), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    /// <summary>Lists all tasks in the service's stable creation order.</summary>
    /// <param name="cancellationToken">Propagates request cancellation to the service.</param>
    /// <returns>HTTP 200 with task snapshots, including an empty array when no tasks exist.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TaskResult>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TaskResult>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _taskService.ListAsync(cancellationToken));
    }

    /// <summary>Retrieves a task by its server-assigned identity.</summary>
    /// <param name="id">The task identity to retrieve.</param>
    /// <param name="cancellationToken">Propagates request cancellation to the service.</param>
    /// <returns>HTTP 200 with the task, or HTTP 404 when it is missing.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<TaskResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResult>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var task = await _taskService.GetByIdAsync(id, cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    /// <summary>Replaces a task's editable fields while preserving its identity and creation time.</summary>
    /// <param name="id">The task identity to update.</param>
    /// <param name="request">The validated replacement content and completion state.</param>
    /// <param name="cancellationToken">Propagates request cancellation to the service.</param>
    /// <returns>HTTP 200 with the saved task, or HTTP 404 when it is missing.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType<TaskResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResult>> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await _taskService.UpdateAsync(id,
            new UpdateTaskInput(request.Title, request.Description, request.IsCompleted), cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    /// <summary>Deletes an existing task.</summary>
    /// <param name="id">The task identity to delete.</param>
    /// <param name="cancellationToken">Propagates request cancellation to the service.</param>
    /// <returns>HTTP 204 with no body, or HTTP 404 when the task is missing.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _taskService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
