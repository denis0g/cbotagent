using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Data;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDatabaseContext>>();

        logger.LogInformation("Applying pending database migrations.");
        await database.Database.MigrateAsync();
        logger.LogInformation("Database migrations are up to date.");
    }
}
