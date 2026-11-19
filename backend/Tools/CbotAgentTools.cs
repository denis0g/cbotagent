using System.ComponentModel;
using backend.Dtos;
using backend.Services;
using Microsoft.Extensions.AI;

namespace backend.Tools;

/// <summary>Tools owned by the main cBot orchestrator agent.</summary>
public sealed class CbotAgentTools(
    ConversationsService conversations,
    StrategyRefinerAgent strategyRefiner,
    CodeGenerationAgent codeGenerator,
    CompilationAgent compilationAgent,
    Guid conversationId,
    Guid userId,
    string initialPrompt,
    Func<Guid, string, Task>? onConversationTitleUpdated = null,
    Func<string, Task>? onCompilationProgress = null)
{
    public const string ToolGroupName = "CbotAgentTools";
    public const string ToolGroupDescription = "Tools available to the cBot orchestration agent for conversation management and cBot specialist delegation.";

    public IList<AITool> CreateAgentTools(bool includeConversationTitle = true)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(RefineStrategyAsync),
            AIFunctionFactory.Create(GenerateCbotCodeAsync),
            AIFunctionFactory.Create(CompileCbotCodeToolAsync)
        };
        if (includeConversationTitle)
            tools.Insert(0, AIFunctionFactory.Create(UpdateConversationTitleAsync));
        return tools;
    }

    [Description("Set a short, descriptive title for the current trading-strategy conversation.")]
    public async Task<UpdateConversationTitleToolResponse> UpdateConversationTitleAsync(
        [Description("A concise title describing the strategy or task.")] string title,
        CancellationToken cancellationToken = default)
    {
        var result = await conversations.UpdateTitleAsync(conversationId, userId, title, cancellationToken);
        if (onConversationTitleUpdated is not null)
            await onConversationTitleUpdated(result.ConversationId, result.Title);
        return result;
    }

    [Description("Stage 2 only: after requirements are gathered or the user explicitly chooses to proceed with current information, ask the strategy specialist for a structured cBot specification and unresolved details. Do not use this to skip clarification or approval.")]
    public Task<RefineStrategyToolResponse> RefineStrategyAsync(
        [Description("Relevant trading requirements collected in the conversation.")] string strategyDetails,
        CancellationToken cancellationToken = default) => strategyRefiner.RefineAsync(strategyDetails, cancellationToken);

    [Description("Stage 4 only: generate cTrader cBot source from the exact refined strategy the user explicitly approved. Never call before approval. Return the bot name and complete source code.")]
    public Task<GenerateCbotToolResponse> GenerateCbotCodeAsync(
        [Description("The strategy the user explicitly approved.")] string approvedStrategy,
        CancellationToken cancellationToken = default) => codeGenerator.GenerateAsync(approvedStrategy, cancellationToken);

    [Description("Stage 5 only: after the user explicitly approves the complete source code displayed in the conversation, pass that exact bot name and source unchanged to the deterministic compilation service. Never compile in the code-generation turn or before source approval.")]
    public async Task<CompileCbotToolResponse> CompileCbotCodeAsync(
        CompileCbotToolRequest request, CancellationToken cancellationToken = default) =>
        await compilationAgent.CompileAsync(new CompileCbotToolRequest
        {
            BotName = request.BotName,
            SourceCode = request.SourceCode,
            UserId = userId,
            ConversationId = conversationId,
            InitialPrompt = initialPrompt
        }, cancellationToken, onCompilationProgress);

    [Description("Stage 5 only: after the user explicitly approves the complete source code displayed in the conversation, pass that exact bot name and source unchanged to the deterministic compilation service. Never compile in the code-generation turn or before source approval.")]
    public Task<CompileCbotToolResponse> CompileCbotCodeToolAsync(
        [Description("The short name for the cBot. Use letters, numbers, and underscores.")] string botName,
        [Description("The complete C# source code for the cTrader Automate cBot.")] string sourceCode,
        CancellationToken cancellationToken = default) => CompileCbotCodeAsync(
            new CompileCbotToolRequest { BotName = botName, SourceCode = sourceCode }, cancellationToken);
}
