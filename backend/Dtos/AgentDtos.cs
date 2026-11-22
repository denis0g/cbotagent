using System.ComponentModel.DataAnnotations;

namespace backend.Dtos;

public sealed class AgentMessageRequest
{
    public Guid? ConversationId { get; set; }

    public BacktestParameters? Backtest { get; set; }

    [Required, StringLength(12000, MinimumLength = 1)]
    public required string Message { get; set; }
}

public sealed class AgentMessageResponse
{
    public required Guid ConversationId { get; init; }
    public required string ConversationTitle { get; init; }
    public required Guid MessageId { get; init; }
    public required string Reply { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public class ConversationSummaryDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed class ConversationDetailDto : ConversationSummaryDto
{
    public required IReadOnlyList<ConversationMessageDto> Messages { get; init; }
}

public sealed class ConversationMessageDto
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class UpdateConversationTitleToolRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public required string Title { get; init; }
}

public sealed class UpdateConversationTitleToolResponse
{
    public required Guid ConversationId { get; init; }
    public required string Title { get; init; }
}

public sealed class RefineStrategyToolRequest
{
    [Required, StringLength(12000, MinimumLength = 1)]
    public required string StrategyDetails { get; init; }
}

public sealed class RefineStrategyToolResponse
{
    public required string RefinedStrategy { get; init; }
    public required IReadOnlyList<string> MissingDetails { get; init; }
}

public sealed class GenerateCbotToolRequest
{
    [Required, StringLength(20000, MinimumLength = 1)]
    public required string ApprovedStrategy { get; init; }
}

public sealed class GenerateCbotToolResponse
{
    public required string SourceCode { get; init; }
    public required string FileName { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class CompileCbotToolRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public required string BotName { get; init; }

    [Required, StringLength(100000, MinimumLength = 1)]
    public required string SourceCode { get; init; }
    public string Description { get; init; } = string.Empty;

    public Guid? BotId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ConversationId { get; init; }
    public string? InitialPrompt { get; init; }
}

public sealed class CompileCbotToolResponse
{
    public required bool Succeeded { get; init; }
    public required string Output { get; init; }
    public string? AgentReply { get; init; }
    public string? SourceCodePath { get; init; }
    public string? AlgoFilePath { get; init; }
    public Guid? BotId { get; init; }
}

public class CbotSummaryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Guid ConversationId { get; init; }
    public required string ConversationTitle { get; init; }
    public required string InitialPrompt { get; init; }
    public required bool LastCompileSucceeded { get; init; }
    public required bool HasAlgoFile { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed class CbotDetailDto : CbotSummaryDto
{
    public required string SourceCode { get; init; }
    public required string LastCompileOutput { get; init; }
}

public sealed class UpdateCbotSourceRequest
{
    [Required, StringLength(100000, MinimumLength = 1)]
    public required string SourceCode { get; init; }
}
