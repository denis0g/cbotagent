using backend.Dtos;
using backend.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace backend.Services;

/// <summary>Specialist agent that runs a cTrader backtest and stores a local report-based review.</summary>
public sealed class BacktestingAgent(
    GeminiClient gemini,
    BacktestingDockerTool dockerTool,
    CbotsService cbots,
    BacktestReportAnalyzer analyzer)
{
    private const string Instructions = """
        You are the cTrader backtesting specialist. Call the available Docker backtest tool exactly once with the user's
        prevalidated settings. Do not alter settings or ask follow-up questions. Report whether the run completed and reports
        were saved. Do not claim a strategy will perform similarly in the future. The saved JSON report is reviewed locally
        by the backend after the tool returns.
        """;

    public async Task<BacktestResult> RunAsync(
        Guid botId, Guid userId, BacktestParameters parameters, CancellationToken cancellationToken = default)
    {
        var tool = new BacktestingAgentTool(dockerTool, botId, userId, parameters);
        var agent = gemini.CreateAgent("CTraderBacktestingAgent",
            "Runs a cTrader cBot backtest and saves its HTML and JSON reports.",
            Instructions, [AIFunctionFactory.Create(tool.RunBacktestAsync)]);
        await agent.RunAsync("Run the user's configured cTrader backtest now.", cancellationToken: cancellationToken);
        var result = tool.Result ?? new BacktestResult
        {
            Succeeded = false,
            Output = "The backtesting specialist did not invoke the Docker backtest tool."
        };
        if (!result.Succeeded || result.RunId is not { } runId || string.IsNullOrWhiteSpace(result.AnalysisData))
            return result with { AnalysisData = null };

        BacktestAnalysis analysis;
        try
        {
            analysis = analyzer.Analyze(result.AnalysisData, parameters.Symbol, parameters.Period);
        }
        catch (System.Text.Json.JsonException)
        {
            analysis = new BacktestAnalysis(
                "The reports were saved, but the JSON report could not be read for a reliable overview.",
                ["Review the saved HTML and JSON reports and validate the report data before drawing conclusions."]);
        }
        await cbots.SaveBacktestAnalysisAsync(runId, userId, analysis.Overview,
            analysis.SuggestedImprovements, cancellationToken);
        return result with
        {
            AnalysisData = null,
            PerformanceOverview = analysis.Overview,
            SuggestedImprovements = analysis.SuggestedImprovements
        };
    }
}
