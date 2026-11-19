namespace backend.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    public AgentSession? Session { get; set; }
    public ICollection<Cbot> Cbots { get; set; } = new List<Cbot>();
}
