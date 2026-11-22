using System.ComponentModel;
using backend.Dtos;

namespace backend.Tools;

/// <summary>Tool exposed only to the backtesting specialist for its active request.</summary>
public sealed class BacktestingAgentTool(
    BacktestingDockerTool dockerTool,
    Guid botId,
    Guid userId,
    BacktestParameters parameters)
{
    public BacktestResult? Result { get; private set; }

    public sealed class ToolResponse
    {
        public required bool Succeeded { get; init; }
        public required string Message { get; init; }
        public Guid? RunId { get; init; }
    }

    [Description("Run the user's configured backtest for the selected cBot using the Spotware cTrader Console Docker image. Call exactly once and do not change the selected settings. The backend performs report analysis after this tool returns.")]
    public async Task<ToolResponse> RunBacktestAsync(CancellationToken cancellationToken = default)
    {
        Result = await dockerTool.RunAsync(botId, userId, parameters, cancellationToken);
        return new ToolResponse
        {
            Succeeded = Result.Succeeded,
            Message = Result.Succeeded ? "Backtest completed; HTML and JSON reports were saved." : Result.Output,
            RunId = Result.RunId
        };
    }
}
