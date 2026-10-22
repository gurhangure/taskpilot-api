using Microsoft.AspNetCore.Mvc;
using TaskPilot.Api.Models;

namespace TaskPilot.Api.Controllers;

/// <summary>
/// Provides a lightweight endpoint to confirm that the API can respond to requests.
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Reports that the API is running without checking external dependencies.
    /// </summary>
    /// <returns>An HTTP 200 response containing the API status.</returns>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("ok"));
    }
}
