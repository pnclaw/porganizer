using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Common;

public static class AppDbContextExtensions
{
    public static Task<AppSettings> GetSettingsAsync(this AppDbContext db, CancellationToken ct = default)
        => db.AppSettings.OrderBy(s => s.Id).FirstAsync(ct);
}
