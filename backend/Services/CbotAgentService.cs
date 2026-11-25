using System.Text.Json;
using backend.Dtos;
using backend.Models;
using backend.Tools;
using Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FrameworkSession = Microsoft.Agents.AI.AgentSession;

namespace backend.Services;

public sealed class CbotAgentService(
    AppDatabaseContext db,
    ConversationsService conversations,
    GeminiClient geminiClient,
    StrategyRefinerAgent strategyRefiner,
    CodeGenerationAgent codeGenerator,
    CompilationAgent compilationAgent,
    BacktestingAgent backtestingAgent)
{
    public const string AgentName = "CbotOrchestratorAgent";
    public const string AgentDescription = "Coordinates the cTrader cBot workflow: clarifies and refines the strategy, obtains strategy and source-code approvals, generates code, and compiles only after approval.";

    private const string Instructions = """
        You are the cTrader cBot workflow orchestrator. Move through the following stages in order and clearly tell the user
        which stage they are in. Do not skip a stage or combine stages that require user input.

        STAGE 1 — UNDERSTAND THE STRATEGY
        Discuss the user's goal and ask focused questions until the trading rules are specific enough to describe. Cover the
        instrument(s), timeframe(s), entry and exit rules, indicators and parameters, position sizing, stop loss/take profit,
        risk limits, trading hours, and execution safeguards where relevant. Ask only the most useful unanswered questions at
        each turn. Never invent trading rules, values, or risk settings. If the user explicitly says to continue with the current
        information, pass only the confirmed information to the refiner and clearly mark unresolved items as unspecified.
        Do not call code generation or compilation in this stage.

        STAGE 2 — REFINE AND PRESENT
        When the user has answered the necessary questions, or explicitly asks you to proceed with the information available,
        call refine_strategy with the complete known strategy, relevant conversation context, and any unresolved items. Use the
        refiner's result as the proposed specification. If it reports missing details that prevent a precise or safe strategy,
        ask the user those questions and wait; after their answers call refine_strategy again. Do not fill gaps yourself.
        Present the refined strategy in a clear, reviewable format. Include important parameters and explicitly state anything
        left unspecified. End by asking whether the user approves this exact strategy for cBot code generation and compilation.
        Stop and wait for the user's answer. Never call generate_cbot_code or compile_cbot_code in the same turn as refinement,
        and never treat a request to continue gathering information as approval.

        STAGE 3 — USER APPROVAL
        Proceed only after the user clearly approves the latest refined specification or explicitly instructs you to generate the
        cBot from that exact specification. If the user requests any strategy changes, return to Stage 2, refine the changes,
        present the updated specification, and ask for approval again. Approval applies only to the latest presented version.

        STAGE 4 — GENERATE AND SHOW SOURCE
        After strategy approval, call generate_cbot_code once with the exact approved refined strategy. If generation succeeds,
        it returns the bot name, a concise description, and complete source code. Stream the complete source to the user in a
        readable C# code block without omitting or truncating any part. State the bot name and show the returned description.
        Then ask whether the user approves compiling this exact source.
        Stop and wait. Do not call compile_cbot_code in the same turn as code generation, and do not compile until the user
        clearly approves the displayed source. If the user asks for source changes, regenerate or update the code, display the
        complete revised source, and ask for compile approval again.

        STAGE 5 — COMPILE AFTER CODE APPROVAL
        Only after the user explicitly approves compiling the latest displayed source, call compile_cbot_code with the bot name
        the exact source code unchanged, and the same description returned during code generation. This delegates the original user prompt and approved code to the compilation
        specialist, which invokes the Docker compilation tool. Relay the specialist's AgentReply to the user. On success, tell
        the user the bot is available on the Bots page. Do not claim success unless the compilation tool reports success. If
        compilation fails, relay the diagnostics and ask whether the user wants code changes. After any change, show the complete
        revised source and get compile approval again. Never silently alter approved trading rules while generating or repairing code.

        Be concise and collaborative throughout, except always include the complete source when requesting code approval. Do not
        promise profitability or imply that a backtest guarantees future results.
        If the current conversation title is "New conversation", call update_conversation_title with a concise 3–7 word topic
        title before responding. This applies to new conversations and any later turn where the title is still the default. Once
        a conversation has a descriptive title, never rename it.
        """;

    public async Task<AgentMessageResponse> SendAsync(
        Guid userId,
        AgentMessageRequest request,
        CancellationToken cancellationToken = default,
        Func<string, Task>? onText = null,
        Func<Guid, Task>? onConversationCreated = null,
        Func<Guid, string, Task>? onConversationTitleUpdated = null,
        Func<string, Task>? onCompilationProgress = null)
    {
        var isNewConversation = request.ConversationId is null;
        var conversation = request.ConversationId is { } conversationId
            ? await conversations.GetOwnedAsync(conversationId, userId, cancellationToken)
                ?? throw new KeyNotFoundException("Conversation not found.")
            : await conversations.CreateAsync(userId, cancellationToken);
        try
        {
        if (isNewConversation && onConversationCreated is not null)
            await onConversationCreated(conversation.Id);

        var sessionRecord = conversation.Session ?? new backend.Models.AgentSession { ConversationId = conversation.Id };
        if (conversation.Session is null)
        {
            conversation.Session = sessionRecord;
            db.AgentSessions.Add(sessionRecord);
        }

        var titleWasUpdated = !isNewConversation;
        var bufferedText = new List<string>();
        async Task OnToolTitleUpdated(Guid id, string title)
        {
            titleWasUpdated = true;
            if (onConversationTitleUpdated is not null)
                await onConversationTitleUpdated(id, title);
            foreach (var text in bufferedText)
                if (onText is not null) await onText(text);
            bufferedText.Clear();
        }

        var initialPrompt = isNewConversation
            ? request.Message.Trim()
            : await conversations.GetInitialUserPromptAsync(conversation.Id, cancellationToken) ?? request.Message.Trim();
        var tools = new CbotAgentTools(conversations, strategyRefiner, codeGenerator, compilationAgent,
            backtestingAgent, conversation.Id, userId, initialPrompt, request.Backtest,
            OnToolTitleUpdated, onCompilationProgress);
        var agentInstructions = request.Backtest is null
            ? Instructions
            : $"{Instructions}\n\nBACKTEST MODE: this turn includes an approved, validated settings bundle and a dedicated run_cbot_backtest tool. Call that tool exactly once, do not call any other tool, do not ask follow-up questions, and do not invent performance results. On success, direct the user to the Backtesting page to review the saved analysis and reports.";
        var agent = geminiClient.CreateAgent(AgentName, AgentDescription, agentInstructions,
            tools.CreateAgentTools(includeConversationTitle:
                string.Equals(conversation.Title, "New conversation", StringComparison.OrdinalIgnoreCase),
                includeBacktesting: request.Backtest is not null));

        FrameworkSession frameworkSession;
        var hasSavedFrameworkSession = TryParseSession(sessionRecord.StateJson, out var savedState);
        if (hasSavedFrameworkSession)
        {
            frameworkSession = await agent.DeserializeSessionAsync(savedState, cancellationToken: cancellationToken);
        }
        else
        {
            frameworkSession = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
            if (!isNewConversation && agent is ChatClientAgent chatAgent &&
                chatAgent.ChatHistoryProvider is InMemoryChatHistoryProvider historyProvider)
            {
                var savedMessages = await conversations.GetMessagesForSessionBootstrapAsync(conversation.Id, cancellationToken);
                var history = savedMessages.Select(message => new ChatMessage(
                    message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, message.Content)).ToList();
                if (history.Count > 0) historyProvider.SetMessages(frameworkSession, history);
            }
        }

        var now = DateTime.UtcNow;
        var reply = new System.Text.StringBuilder();
        var agentPrompt = request.Backtest is null
            ? request.Message.Trim()
            : $"{request.Message.Trim()}\n\nA validated cTrader backtest request for cBot {request.Backtest.BotId} is attached to the backtest tool. Call that tool exactly once now; do not ask for settings or credentials again.";
        await foreach (var update in agent.RunStreamingAsync(agentPrompt, frameworkSession,
                           cancellationToken: cancellationToken))
        {
            var text = update.Text;
            if (string.IsNullOrEmpty(text)) continue;
            reply.Append(text);
            if (!titleWasUpdated) bufferedText.Add(text);
            else if (onText is not null) await onText(text);
        }

        if (isNewConversation && !titleWasUpdated)
            throw new InvalidOperationException("The orchestrator did not update the new conversation title.");

        sessionRecord.Provider = "Microsoft Agent Framework / Google GenAI";
        sessionRecord.ProviderSessionId = null;
        sessionRecord.StateJson = (await agent.SerializeSessionAsync(frameworkSession, cancellationToken: cancellationToken)).GetRawText();
        sessionRecord.UpdatedAt = DateTime.UtcNow;

        db.ConversationMessages.Add(new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = request.Message.Trim(),
            CreatedAt = now
        });
        var assistantMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = reply.ToString(),
            CreatedAt = sessionRecord.UpdatedAt
        };
        db.ConversationMessages.Add(assistantMessage);
        conversation.UpdatedAt = sessionRecord.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);

        return new AgentMessageResponse
        {
            ConversationId = conversation.Id,
            ConversationTitle = conversation.Title,
            MessageId = assistantMessage.Id,
            Reply = reply.ToString(),
            CreatedAt = assistantMessage.CreatedAt
        };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (isNewConversation)
                await conversations.DeleteAsync(conversation.Id, userId, CancellationToken.None);
            throw;
        }
    }

    private static bool TryParseSession(string? stateJson, out JsonElement state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(stateJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(stateJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any())
                return false;
            state = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
