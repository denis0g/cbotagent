using System.ComponentModel;
using backend.Dtos;

namespace backend.Tools;

/// <summary>Exposes compilation to the compilation specialist with request ownership kept outside the model.</summary>
public sealed class CompilationAgentTool(
    CompilationAgentTools compiler,
    Guid? userId,
    Guid? conversationId,
    string? initialPrompt,
    string description,
    Guid? botId,
    Func<string, Task>? onProgress)
{
    public CompileCbotToolResponse? Result { get; private set; }

    [Description("Build the supplied cTrader cBot source code using the configured .NET Docker compiler. Provide only the bot name and complete C# source code. Do not change the source.")]
    public async Task<CompileCbotToolResponse> CompileCbotAsync(
        [Description("The bot name, used for the solution, project, assembly, and .algo artifact name.")] string botName,
        [Description("The complete, user-approved C# source code for this cTrader cBot.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        Result = await compiler.CompileAsync(new CompileCbotToolRequest
        {
            BotName = botName,
            SourceCode = sourceCode,
            BotId = botId,
            UserId = userId,
            ConversationId = conversationId,
            InitialPrompt = initialPrompt,
            Description = description
        }, cancellationToken, onProgress);
        return Result;
    }
}
