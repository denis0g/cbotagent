using backend.Dtos;
using backend.Models;
using Data;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public sealed class CbotsService(AppDatabaseContext db, IConfiguration configuration, IHostEnvironment environment)
{
    public async Task<IReadOnlyList<CbotSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Cbots.AsNoTracking()
            .Where(cbot => cbot.UserId == userId)
            .OrderByDescending(cbot => cbot.UpdatedAt)
            .Select(cbot => new CbotSummaryDto
            {
                Id = cbot.Id,
                Name = cbot.Name,
                Description = cbot.Description,
                ConversationId = cbot.ConversationId,
                ConversationTitle = cbot.Conversation.Title,
                InitialPrompt = cbot.InitialPrompt,
                LastCompileSucceeded = cbot.LastCompileSucceeded,
                HasAlgoFile = cbot.AlgoFilePath != null,
                CreatedAt = cbot.CreatedAt,
                UpdatedAt = cbot.UpdatedAt
            })
            .ToListAsync(cancellationToken);

    public async Task<CbotDetailDto?> GetDetailAsync(Guid botId, Guid userId, CancellationToken cancellationToken = default)
    {
        var bot = await db.Cbots.AsNoTracking()
            .Include(cbot => cbot.Conversation)
            .FirstOrDefaultAsync(cbot => cbot.Id == botId && cbot.UserId == userId, cancellationToken);
        if (bot is null) return null;
        return new CbotDetailDto
        {
            Id = bot.Id,
            Name = bot.Name,
            Description = bot.Description,
            ConversationId = bot.ConversationId,
            ConversationTitle = bot.Conversation.Title,
            InitialPrompt = bot.InitialPrompt,
            LastCompileSucceeded = bot.LastCompileSucceeded,
            HasAlgoFile = bot.AlgoFilePath is not null && AlgoFileExists(bot.AlgoFilePath),
            CreatedAt = bot.CreatedAt,
            UpdatedAt = bot.UpdatedAt,
            SourceCode = await File.ReadAllTextAsync(ResolvePath(bot.SourceCodePath), cancellationToken),
            LastCompileOutput = bot.LastCompileOutput
        };
    }

    public async Task<CompileCbotToolResponse> SaveSuccessfulCompilationAsync(
        CompileCbotToolRequest request,
        string dockerSourcePath,
        string dockerAlgoPath,
        string output,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId is not { } userId || request.ConversationId is not { } conversationId)
            return new CompileCbotToolResponse { Succeeded = false, Output = "The bot owner and originating conversation are required." };

        var conversationExists = await db.Conversations.AnyAsync(
            conversation => conversation.Id == conversationId && conversation.UserId == userId, cancellationToken);
        if (!conversationExists)
            return new CompileCbotToolResponse { Succeeded = false, Output = "The originating conversation could not be found." };

        Cbot? bot = request.BotId is { } botId
            ? await db.Cbots.FirstOrDefaultAsync(item => item.Id == botId && item.UserId == userId, cancellationToken)
            : await db.Cbots.FirstOrDefaultAsync(item => item.UserId == userId && item.ConversationId == conversationId && item.Name == request.BotName, cancellationToken);
        if (request.BotId is not null && bot is null)
            return new CompileCbotToolResponse { Succeeded = false, Output = "The requested bot could not be found." };

        var isNew = bot is null;
        bot ??= new Cbot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConversationId = conversationId,
            Name = request.BotName,
            Description = request.Description,
            InitialPrompt = request.InitialPrompt ?? string.Empty,
            SourceCodePath = string.Empty
        };

        if (isNew)
        {
            var directoryName = await GetAvailableDirectoryNameAsync(request.BotName, bot.Id, cancellationToken);
            var directory = Path.Combine(GetStorageRoot(), directoryName);
            Directory.CreateDirectory(directory);
            bot.SourceCodePath = Path.Combine(directoryName, $"{request.BotName}.cs");
        }

        var normalizedSourcePath = bot.SourceCodePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var botDirectory = Path.GetDirectoryName(normalizedSourcePath);
        bot.AlgoFilePath = Path.Combine(botDirectory ?? string.Empty, $"{request.BotName}.algo");
        var sourceDestination = ResolvePath(bot.SourceCodePath);
        var algoDestination = ResolveAlgoPath(bot.AlgoFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceDestination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(algoDestination)!);
        File.Copy(dockerSourcePath, sourceDestination, overwrite: true);
        File.Copy(dockerAlgoPath, algoDestination, overwrite: true);
        bot.Name = request.BotName;
        if (!string.IsNullOrWhiteSpace(request.Description)) bot.Description = request.Description.Trim();
        if (string.IsNullOrWhiteSpace(bot.InitialPrompt)) bot.InitialPrompt = request.InitialPrompt ?? string.Empty;
        bot.LastCompileSucceeded = true;
        bot.LastCompileOutput = output;
        bot.UpdatedAt = DateTime.UtcNow;
        if (isNew) db.Cbots.Add(bot);
        await db.SaveChangesAsync(cancellationToken);

        return new CompileCbotToolResponse
        {
            Succeeded = true,
            Output = output,
            SourceCodePath = bot.SourceCodePath,
            AlgoFilePath = bot.AlgoFilePath,
            BotId = bot.Id
        };
    }

    public async Task<bool> SaveSourceAsync(Guid botId, Guid userId, string sourceCode, CancellationToken cancellationToken = default)
    {
        var bot = await db.Cbots.FirstOrDefaultAsync(item => item.Id == botId && item.UserId == userId, cancellationToken);
        if (bot is null) return false;
        var sourcePath = ResolvePath(bot.SourceCodePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, sourceCode, cancellationToken);
        bot.Description = string.Empty;
        bot.LastCompileSucceeded = false;
        bot.LastCompileOutput = "Source changed since the last successful compilation.";
        bot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CompileCbotToolRequest?> CreateCompileRequestAsync(Guid botId, Guid userId, CancellationToken cancellationToken = default)
    {
        var bot = await db.Cbots.AsNoTracking().FirstOrDefaultAsync(
            item => item.Id == botId && item.UserId == userId, cancellationToken);
        if (bot is null) return null;
        return new CompileCbotToolRequest
        {
            BotId = bot.Id,
            UserId = bot.UserId,
            ConversationId = bot.ConversationId,
            InitialPrompt = bot.InitialPrompt,
            Description = bot.Description,
            BotName = bot.Name,
            SourceCode = await File.ReadAllTextAsync(ResolvePath(bot.SourceCodePath), cancellationToken)
        };
    }

    public async Task<string?> GetArtifactPathAsync(Guid botId, Guid userId, bool algo, CancellationToken cancellationToken = default)
    {
        var bot = await db.Cbots.AsNoTracking().FirstOrDefaultAsync(
            item => item.Id == botId && item.UserId == userId, cancellationToken);
        if (bot is null) return null;
        var relativePath = algo ? bot.AlgoFilePath : bot.SourceCodePath;
        if (relativePath is null) return null;
        if (algo)
        {
            var algoPath = ResolveAlgoPath(relativePath);
            if (File.Exists(algoPath)) return algoPath;

            // Existing records may still point to .algo files stored with source under BotDirectory.
            var legacyPath = ResolvePath(relativePath);
            return File.Exists(legacyPath) ? legacyPath : null;
        }

        var sourcePath = ResolvePath(relativePath);
        return File.Exists(sourcePath) ? sourcePath : null;
    }

    public async Task<bool> SaveFailedCompilationAsync(
        CompileCbotToolRequest request, string output, CancellationToken cancellationToken = default)
    {
        if (request.BotId is not { } botId || request.UserId is not { } userId) return false;
        var bot = await db.Cbots.FirstOrDefaultAsync(item => item.Id == botId && item.UserId == userId, cancellationToken);
        if (bot is null) return false;
        await File.WriteAllTextAsync(ResolvePath(bot.SourceCodePath), request.SourceCode, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Description)) bot.Description = request.Description.Trim();
        bot.LastCompileSucceeded = false;
        bot.LastCompileOutput = output;
        bot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> GetAvailableDirectoryNameAsync(string name, Guid botId, CancellationToken cancellationToken)
    {
        var safeName = SanitizeName(name);
        var candidate = safeName;
        var path = Path.Combine(GetStorageRoot(), candidate);
        if (!Directory.Exists(path)) return candidate;
        var existing = await db.Cbots.AsNoTracking().AnyAsync(
            bot => bot.SourceCodePath.StartsWith(candidate + Path.DirectorySeparatorChar), cancellationToken);
        return existing ? $"{safeName}_{botId:N}" : candidate;
    }

    private string GetStorageRoot()
    {
        var configured = configuration["CTrader:BotDirectory"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var repoRoot = File.Exists(Path.Combine(contentRoot, "backend.csproj"))
            ? Directory.GetParent(contentRoot)?.FullName ?? contentRoot
            : contentRoot;
        return Path.Combine(repoRoot, "Cbots");
    }

    private string GetAlgoStorageRoot()
    {
        var configured = configuration["CTrader:AlgoDirectory"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetFullPath(environment.ContentRootPath), "Algos")
            : Path.GetFullPath(configured, environment.ContentRootPath);
    }

    private bool AlgoFileExists(string relativePath)
    {
        var algoPath = ResolveAlgoPath(relativePath);
        if (File.Exists(algoPath)) return true;
        return File.Exists(ResolvePath(relativePath));
    }

    private string ResolveAlgoPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var root = GetAlgoStorageRoot();
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        var allowedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(allowedRoot, comparison))
            throw new InvalidOperationException("The bot artifact path is outside the configured algo storage directory.");
        return path;
    }

    private string ResolvePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var root = GetStorageRoot();
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        var allowedRoot = Path.GetFullPath(GetStorageRoot()) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(allowedRoot, comparison))
            throw new InvalidOperationException("The bot artifact path is outside the configured storage directory.");
        return path;
    }

    private static string SanitizeName(string name) =>
        new string(name.Trim().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray()).Trim('_', '-');

}
