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
[Route("api/[controller]")]
public sealed class AgentController(
    CbotAgentService agent,
    ConversationsService conversations,
    AppDatabaseContext db,
    ILogger<AgentController> logger,
    IHostEnvironment environment) : ControllerBase
{
    private static readonly System.Text.Json.JsonSerializerOptions StreamJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [HttpPost("messages")]
    public async Task Send(AgentMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        try
        {
            var result = await agent.SendAsync(
                userId.Value,
                request,
                cancellationToken,
                onText: text => WriteStreamEventAsync("delta", new { text }, cancellationToken),
                onConversationCreated: id => WriteStreamEventAsync("conversation", new { conversationId = id }, cancellationToken),
                onConversationTitleUpdated: (id, title) => WriteStreamEventAsync("title", new { conversationId = id, title }, cancellationToken),
                onCompilationProgress: status => WriteStreamEventAsync("progress", new { status }, cancellationToken));
            await WriteStreamEventAsync("done", result, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status404NotFound;
            else await WriteStreamErrorAsync("Conversation not found.", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The frontend cancels the fetch when the user leaves this conversation.
            // Do not write an error event to a stream the client has intentionally closed.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent request failed for conversation {ConversationId}.", request.ConversationId);
            if (!Response.HasStarted)
            {
                var statusCode = exception is InvalidOperationException && exception.Message.Contains("Gemini:ApiKey", StringComparison.Ordinal)
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway;
                Response.StatusCode = statusCode;
                Response.ContentType = "application/problem+json";
                await Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = statusCode,
                    Title = "The agent could not complete the request.",
                    Detail = environment.IsDevelopment()
                        ? exception.GetBaseException().Message
                        : "Please try again. If this continues, check the backend logs."
                }, cancellationToken);
            }
            else
                await WriteStreamErrorAsync("The agent could not complete this request. Please try again.", cancellationToken);
        }
    }

    private async Task WriteStreamErrorAsync(string message, CancellationToken cancellationToken)
    {
        await WriteStreamEventAsync("error", new { message }, cancellationToken);
    }

    private async Task WriteStreamEventAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(data, StreamJsonOptions);
        await Response.WriteAsync($"event: {eventName}\ndata: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return userId is null ? Unauthorized() : Ok(await conversations.ListAsync(userId.Value, cancellationToken));
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<ActionResult<ConversationDetailDto>> Get(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var detail = await conversations.GetDetailAsync(conversationId, userId.Value, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    public async Task<IActionResult> Delete(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();
        var deleted = await conversations.DeleteAsync(conversationId, userId.Value, cancellationToken);
        return deleted ? NoContent() : NotFound();
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
