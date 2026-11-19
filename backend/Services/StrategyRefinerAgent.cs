using backend.Dtos;
using Microsoft.Agents.AI;

namespace backend.Services;

public sealed class StrategyRefinerAgent
{
    public const string AgentName = "StrategyRefinerAgent";
    public const string AgentDescription = "Turns the user's trading discussion into a precise cTrader cBot strategy specification and identifies unresolved requirements.";

    private const string Instructions = """
        You are a trading strategy specification refiner for cTrader cBots. Convert the supplied discussion notes into a precise,
        implementation-ready strategy only where facts are present. Do not fill gaps with assumptions. Return a JSON object with
        `refinedStrategy` (a structured specification) and `missingDetails` (an array of questions or unresolved items). Include
        instrument, timeframe, entry, exit, risk, sizing, trading hours, and execution safeguards when known.
        """;

    private readonly AIAgent agent;

    public StrategyRefinerAgent(GeminiClient gemini)
    {
        agent = gemini.CreateAgent(AgentName, AgentDescription, Instructions);
    }

    public async Task<RefineStrategyToolResponse> RefineAsync(string strategyDetails, CancellationToken cancellationToken = default)
    {
        var turn = await agent.RunAsync($"Refine the following conversation details:\n{strategyDetails}",
            cancellationToken: cancellationToken);
        var response = turn.Text;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RefineStrategyToolResponse>(response,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                ?? new RefineStrategyToolResponse { RefinedStrategy = response, MissingDetails = [] };
        }
        catch (System.Text.Json.JsonException)
        {
            return new RefineStrategyToolResponse { RefinedStrategy = response, MissingDetails = [] };
        }
    }
}
