using Microsoft.AspNetCore.Mvc;

namespace ResumeMatcher.Api.Controllers;

/// <summary>
/// Health check endpoint.
///
/// WHY THIS EXISTS:
/// Every production API needs a health endpoint. It tells:
/// - Docker: "is the container ready to receive traffic?"
/// - Kubernetes: "should I restart this pod?"
/// - Load balancers: "is this instance healthy?"
/// - Your monitoring tool: "is the API up?"
///
/// In Step 3, we will extend /health to also check:
/// - Database connectivity (is PostgreSQL reachable?)
/// - (Optionally) OpenAI API reachability
///
/// For now, it just confirms the API process is alive.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Returns the current health status of the API.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new HealthResponse(
            Status: "Healthy",
            Version: "1.0.0-step1",
            Timestamp: DateTime.UtcNow
        ));
    }
}

/// <summary>Response shape for the health endpoint.</summary>
public sealed record HealthResponse(string Status, string Version, DateTime Timestamp);
