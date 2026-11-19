using System.Security.Claims;
using backend.Dtos;
using backend.Services;
using Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/bots")]
public sealed class CbotsController(CbotsService cbots, CompilationAgent compiler, AppDatabaseContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CbotSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return userId is null ? Unauthorized() : Ok(await cbots.ListAsync(userId.Value, cancellationToken));
    }

    [HttpGet("{botId:guid}")]
    public async Task<ActionResult<CbotDetailDto>> Get(Guid botId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var bot = await cbots.GetDetailAsync(botId, userId.Value, cancellationToken);
        return bot is null ? NotFound() : Ok(bot);
    }

    [HttpPut("{botId:guid}/source")]
    public async Task<IActionResult> SaveSource(Guid botId, UpdateCbotSourceRequest request, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        return await cbots.SaveSourceAsync(botId, userId.Value, request.SourceCode, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("{botId:guid}/compile")]
    public async Task<ActionResult<CompileCbotToolResponse>> Compile(Guid botId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var request = await cbots.CreateCompileRequestAsync(botId, userId.Value, cancellationToken);
        if (request is null) return NotFound();
        return Ok(await compiler.CompileAsync(request, cancellationToken));
    }

    [HttpGet("{botId:guid}/download/source")]
    public async Task<IActionResult> DownloadSource(Guid botId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var path = await cbots.GetArtifactPathAsync(botId, userId.Value, algo: false, cancellationToken);
        return path is null ? NotFound() : PhysicalFile(path, "text/plain", Path.GetFileName(path));
    }

    [HttpGet("{botId:guid}/download/algo")]
    public async Task<IActionResult> DownloadAlgo(Guid botId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var path = await cbots.GetArtifactPathAsync(botId, userId.Value, algo: true, cancellationToken);
        return path is null ? NotFound() : PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
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
