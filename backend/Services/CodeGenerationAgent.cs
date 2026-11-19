using backend.Dtos;
using Microsoft.Agents.AI;

namespace backend.Services;

public sealed class CodeGenerationAgent
{
    public const string AgentName = "CTraderCodeGenerationAgent";
    public const string AgentDescription = "Generates cTrader Automate cBot source code from a strategy the user has approved.";

    private const string Instructions = """
        You generate compilable C# cTrader Automate cBot source code using the official cAlgo API.
        Return only a JSON object with `fileName` and `sourceCode`. Use the Robot lifecycle and API correctly, include explicit
        risk controls from the approved strategy, and do not add trading rules or parameters absent from the approved strategy.
        """;

    private readonly AIAgent agent;

    public CodeGenerationAgent(GeminiClient gemini)
    {
        agent = gemini.CreateAgent(AgentName, AgentDescription, Instructions);
    }

    public async Task<GenerateCbotToolResponse> GenerateAsync(string approvedStrategy, CancellationToken cancellationToken = default)
    {
        var turn = await agent.RunAsync($"Generate a cBot for this user-approved strategy:\n{approvedStrategy}",
            cancellationToken: cancellationToken);
        var response = turn.Text;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<GenerateCbotToolResponse>(response,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Code generation returned an empty response.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException("Code generation response was not valid JSON.", exception);
        }
    }
}
