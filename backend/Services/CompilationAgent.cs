using backend.Dtos;
using backend.Tools;

namespace backend.Services;

/// <summary>Deterministically coordinates compilation after the user approves generated source.</summary>
public sealed class CompilationAgent(CompilationAgentTools compilerTool)
{
    public const string AgentName = "CTraderCompilationAgent";
    public const string AgentDescription = "Compiles cTrader cBots using the .NET 10 SDK Docker container.";

    public Task<CompileCbotToolResponse> CompileAsync(
        CompileCbotToolRequest request,
        CancellationToken cancellationToken = default,
        Func<string, Task>? onProgress = null) =>
        compilerTool.CompileAsync(request, cancellationToken, onProgress);
}
