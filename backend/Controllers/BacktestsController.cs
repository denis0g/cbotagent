using System.Security.Claims;
using backend.Services;
using Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/backtests")]
public sealed class BacktestsController(CbotsService cbots, AppDatabaseContext db) : ControllerBase
{
    [HttpGet("{botId:guid}/latest")]
    public async Task<ActionResult<backend.Dtos.BacktestRunDto>> GetLatest(Guid botId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var run = await cbots.GetLatestBacktestAsync(botId, userId.Value, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("runs/{runId:guid}/report/{format}")]
    public async Task<IActionResult> GetReport(Guid runId, string format, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var extension = format.ToLowerInvariant();
        if (extension is not ("html" or "json")) return NotFound();
        var path = await cbots.GetBacktestReportPathAsync(runId, userId.Value, extension, cancellationToken);
        if (path is null) return NotFound();
        return PhysicalFile(path, extension == "html" ? "text/html" : "application/json", enableRangeProcessing: true);
    }

    private async Task<Guid?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return userId;
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return null;
        return await db.Users.Where(user => user.UserName == username)
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);
    }
}
