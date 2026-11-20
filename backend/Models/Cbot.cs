namespace backend.Models;

public sealed class Cbot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InitialPrompt { get; set; } = string.Empty;
    public string SourceCodePath { get; set; } = string.Empty;
    public string? AlgoFilePath { get; set; }
    public bool LastCompileSucceeded { get; set; }
    public string LastCompileOutput { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
