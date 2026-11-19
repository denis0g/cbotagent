using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace backend.Services;

/// <summary>Creates Microsoft Agent Framework agents backed by Google's Gemini chat client.</summary>
public sealed class GeminiClient(IConfiguration configuration, ILoggerFactory loggerFactory)
{
    private const string Model = "gemini-3.5-flash-lite";

    public AIAgent CreateAgent(
        string name,
        string description,
        string instructions,
        IList<AITool>? tools = null)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");

        var googleClient = new Client(apiKey: apiKey, enterprise: false);
        var chatClient = googleClient.AsIChatClient(Model);
        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Id = name,
            Name = name,
            Description = description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools
            },
            ChatHistoryProvider = new InMemoryChatHistoryProvider()
        }, loggerFactory);
    }
}
