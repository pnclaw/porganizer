using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using porganizer.Api.Features.AppLogs;
using porganizer.Api.Features.Library;
using porganizer.Database;

namespace porganizer.Api.Tests;

/// <summary>
/// Spins up the real application against a temporary SQLite database.
/// Create one instance per test class via IAsyncLifetime for full isolation.
/// Pass authUsername/authPassword to enable authentication in the test host.
/// </summary>
public sealed class PorganizerApiFactory(
    string? authUsername = null,
    string? authPassword = null) : WebApplicationFactory<Program>
{
    private readonly string _testId = Guid.NewGuid().ToString("N");

    private string DbPath         => Path.Combine(Path.GetTempPath(), $"porganizer-test-{_testId}.db");
    private string ThumbnailPath  => Path.Combine(Path.GetTempPath(), $"porganizer-test-{_testId}-thumbnails");
    private string PreviewPath    => Path.Combine(Path.GetTempPath(), $"porganizer-test-{_testId}-previews");
    public  string LogsPath       => Path.Combine(Path.GetTempPath(), $"porganizer-test-{_testId}-logs");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (authUsername is not null)
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Enabled"]  = "true",
                    ["Auth:Username"] = authUsername,
                    ["Auth:Password"] = authPassword ?? string.Empty,
                }));
        }

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var hostedService in hostedServices)
                services.Remove(hostedService);

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={DbPath}"));

            // Give each factory its own cache directories so parallel test classes
            // cannot interfere with each other's thumbnail, preview, and log files.
            services.Configure<ThumbnailOptions>(opts => opts.CachePath = ThumbnailPath);
            services.Configure<PreviewOptions>(opts => opts.CachePath = PreviewPath);
            services.Configure<AppLogsOptions>(opts => opts.LogsDirectory = LogsPath);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(DbPath))
            File.Delete(DbPath);
        if (Directory.Exists(ThumbnailPath))
            Directory.Delete(ThumbnailPath, recursive: true);
        if (Directory.Exists(PreviewPath))
            Directory.Delete(PreviewPath, recursive: true);
        if (Directory.Exists(LogsPath))
            Directory.Delete(LogsPath, recursive: true);
    }
}
