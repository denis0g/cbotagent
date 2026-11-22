using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class AppDatabaseContext : DbContext
{
    public AppDatabaseContext(DbContextOptions<AppDatabaseContext> options) : base(options)
    {
        
    }

    public DbSet<User> Users {get; set;}
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<ConversationMessage> ConversationMessages { get; set; }
    public DbSet<AgentSession> AgentSessions { get; set; }
    public DbSet<Cbot> Cbots { get; set; }
    public DbSet<BacktestRun> BacktestRuns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentSession>().HasKey(session => session.ConversationId);

        modelBuilder.Entity<Conversation>()
            .HasOne(conversation => conversation.User)
            .WithMany()
            .HasForeignKey(conversation => conversation.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Conversation>()
            .HasMany(conversation => conversation.Messages)
            .WithOne(message => message.Conversation)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Conversation>()
            .HasOne(conversation => conversation.Session)
            .WithOne(session => session.Conversation)
            .HasForeignKey<AgentSession>(session => session.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Conversation>()
            .HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt });
        modelBuilder.Entity<ConversationMessage>()
            .HasIndex(message => new { message.ConversationId, message.CreatedAt });

        modelBuilder.Entity<Cbot>()
            .HasOne(cbot => cbot.User)
            .WithMany()
            .HasForeignKey(cbot => cbot.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Cbot>()
            .HasOne(cbot => cbot.Conversation)
            .WithMany(conversation => conversation.Cbots)
            .HasForeignKey(cbot => cbot.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Cbot>()
            .HasIndex(cbot => new { cbot.UserId, cbot.UpdatedAt });

        modelBuilder.Entity<BacktestRun>()
            .HasOne(run => run.Cbot)
            .WithMany(cbot => cbot.BacktestRuns)
            .HasForeignKey(run => run.CbotId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BacktestRun>()
            .HasIndex(run => new { run.CbotId, run.StartedAt });
    }
}
