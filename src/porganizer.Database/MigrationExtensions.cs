using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace porganizer.Database;

public static class MigrationExtensions
{
    /// <summary>
    /// Applies any pending EF Core migrations on application startup.
    /// </summary>
    public static void MigrateDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}
