using backend.Dtos;
using backend.Models;
using Data;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public sealed class ConversationsService(AppDatabaseContext db)
{
    public async Task<Conversation> CreateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var conversation = new Conversation { UserId = userId, Session = new AgentSession() };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public Task<Conversation?> GetOwnedAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default) =>
        db.Conversations.Include(conversation => conversation.Session)
            .FirstOrDefaultAsync(conversation => conversation.Id == conversationId && conversation.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesForSessionBootstrapAsync(
        Guid conversationId, CancellationToken cancellationToken = default) =>
        await db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<string?> GetInitialUserPromptAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.Role == "user")
            .OrderBy(message => message.CreatedAt)
            .Select(message => message.Content)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Conversations.Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Select(conversation => new ConversationSummaryDto
            {
                Id = conversation.Id,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt
            }).ToListAsync(cancellationToken);

    public async Task<ConversationDetailDto?> GetDetailAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations
            .Include(item => item.Messages.OrderBy(message => message.CreatedAt))
            .FirstOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);
        if (conversation is null) return null;

        return new ConversationDetailDto
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = conversation.Messages.Select(message => new ConversationMessageDto
            {
                Id = message.Id,
                Role = message.Role,
                Content = message.Content,
                CreatedAt = message.CreatedAt
            }).ToList()
        };
    }

    public async Task<UpdateConversationTitleToolResponse> UpdateTitleAsync(
        Guid conversationId, Guid userId, string title, CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedAsync(conversationId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        var hasMessages = await db.ConversationMessages.AnyAsync(
            message => message.ConversationId == conversationId, cancellationToken);
        if (conversation.Title == "New conversation" && !hasMessages)
        {
            conversation.Title = title.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return new UpdateConversationTitleToolResponse { ConversationId = conversation.Id, Title = conversation.Title };
    }

    public async Task<bool> DeleteAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            item => item.Id == conversationId && item.UserId == userId, cancellationToken);
        if (conversation is null) return false;
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
