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
    BacktestingAgent backtestingAgent,
    Guid conversationId,
    Guid userId,
    string initialPrompt,
    BacktestParameters? backtestParameters = null,
    Func<Guid, string, Task>? onConversationTitleUpdated = null,
    Func<string, Task>? onCompilationProgress = null)
{
    public sealed class BacktestToolSummary
    {
        public required bool Succeeded { get; init; }
        public required string Message { get; init; }
        public Guid? RunId { get; init; }
    }

    public const string ToolGroupName = "CbotAgentTools";
    public const string ToolGroupDescription = "Tools available to the cBot orchestration agent for conversation management and cBot specialist delegation.";

    public IList<AITool> CreateAgentTools(bool includeConversationTitle = true, bool includeBacktesting = false)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(RefineStrategyAsync),
            AIFunctionFactory.Create(GenerateCbotCodeAsync),
            AIFunctionFactory.Create(CompileCbotCodeToolAsync)
        };
        if (includeConversationTitle)
            tools.Insert(0, AIFunctionFactory.Create(UpdateConversationTitleAsync));
        if (includeBacktesting)
            tools.Add(AIFunctionFactory.Create(RunCbotBacktestAsync));
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

    [Description("Stage 4 only: generate cTrader cBot source and a concise plain-language description from the exact refined strategy the user explicitly approved. Never call before approval.")]
    public Task<GenerateCbotToolResponse> GenerateCbotCodeAsync(
        [Description("The strategy the user explicitly approved.")] string approvedStrategy,
        CancellationToken cancellationToken = default) => codeGenerator.GenerateAsync(approvedStrategy, cancellationToken);

    [Description("Stage 5 only: after the user explicitly approves the complete source code displayed in the conversation, delegate that exact bot name and source unchanged to the cTrader compilation specialist. Never compile in the code-generation turn or before source approval.")]
    public Task<CompileCbotToolResponse> CompileCbotCodeAsync(
        CompileCbotToolRequest request, CancellationToken cancellationToken = default)
        => compilationAgent.CompileAsync(new CompileCbotToolRequest
        {
            BotName = request.BotName,
            SourceCode = request.SourceCode,
            Description = request.Description,
            UserId = userId,
            ConversationId = conversationId,
            InitialPrompt = initialPrompt
        }, cancellationToken, onCompilationProgress);

    [Description("Stage 5 only: after the user explicitly approves the complete source code displayed in the conversation, pass the exact bot name and full approved source unchanged to the cTrader compilation specialist. Also pass the description returned by code generation when available; if unavailable, the server generates one from the source.")]
    public Task<CompileCbotToolResponse> CompileCbotCodeToolAsync(
        [Description("The short name for the cBot. Use letters, numbers, and underscores.")] string botName,
        [Description("The complete C# source code for the cTrader Automate cBot.")] string sourceCode,
        [Description("The 2–3 sentence description returned with this bot's generated source code, if available.")] string? description = null,
        CancellationToken cancellationToken = default) => CompileCbotCodeAsync(
            new CompileCbotToolRequest { BotName = botName, SourceCode = sourceCode, Description = description ?? string.Empty }, cancellationToken);

    [Description("Backtest mode only: delegate the selected bot and its user-supplied settings to the backtesting specialist. Call once when the user requested a backtest. Do not modify the settings or claim results before it returns.")]
    public async Task<BacktestToolSummary> RunCbotBacktestAsync(CancellationToken cancellationToken = default)
    {
        if (backtestParameters is null)
            throw new InvalidOperationException("No backtest settings were supplied for this conversation turn.");
        var result = await backtestingAgent.RunAsync(
            backtestParameters.BotId, userId, backtestParameters, cancellationToken);
        return new BacktestToolSummary
        {
            Succeeded = result.Succeeded,
            Message = result.Succeeded
                ? "Backtest completed and both report files were saved. The user can review the analysis and reports on the Backtesting page."
                : result.Output,
            RunId = result.RunId
        };
    }
}
