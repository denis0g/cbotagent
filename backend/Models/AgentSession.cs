namespace backend.Models;

public class AgentSession
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public string Provider { get; set; } = "Google Gemini";
    public string? ProviderSessionId { get; set; }
    public string StateJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
