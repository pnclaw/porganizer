using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Auth;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.Indexers.Scraping;
using porganizer.Api.Middleware;
using porganizer.Database;
using Serilog;
using Serilog.Core;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

var logsPath = Path.GetFullPath(
    Environment.GetEnvironmentVariable("LOGS_PATH") ?? "logs/app-.log");

var logsDir = Path.GetDirectoryName(logsPath)!;
Directory.CreateDirectory(logsDir);

// LoggingLevelSwitch allows runtime log level changes without restart
var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
builder.Services.AddSingleton(levelSwitch);

// Serilog — level controlled by LoggingLevelSwitch; framework namespaces stay at Warning
builder.Host.UseSerilog((_, _, lc) => lc
    .MinimumLevel.ControlledBy(levelSwitch)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: logsPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

builder.Services.Configure<porganizer.Api.Features.AppLogs.AppLogsOptions>(opts => opts.LogsDirectory = logsDir);
builder.Services.AddScoped<porganizer.Api.Features.AppLogs.IAppLogsService, porganizer.Api.Features.AppLogs.AppLogsService>();

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services
    .AddOptions<AuthOptions>()
    .BindConfiguration(AuthOptions.SectionName)
    .ValidateOnStart();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();
builder.Services.AddScoped<IndexerScrapeService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.PrdbFavoritesService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbActorSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbVideoDetailSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbLatestPreDbSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbWantedVideoSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.FavoritesWantedVideoSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.AutoWantedVideoSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbDownloadedFromIndexerSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbVideoFilehashSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbIndexerFilehashSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Prdb.Sync.PrdbVideoUserImageSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.Indexers.Matching.IndexerRowMatchService>();
builder.Services.AddScoped<porganizer.Api.Features.WantedFulfillment.WantedVideoFulfillmentService>();
builder.Services.AddScoped<porganizer.Api.Features.Indexers.Scraping.IndexerBackfillService>();
builder.Services.AddHostedService<IndexerScraperBackgroundService>();
builder.Services.AddScoped<DownloadClientTester>();
builder.Services.AddScoped<DownloadClientSender>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.DownloadLogFileSyncService>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.DownloadFileMoveService>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.DownloadPollService>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.DownloadLibraryFolderService>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.SabnzbdPoller>();
builder.Services.AddScoped<porganizer.Api.Features.DownloadClients.NzbgetPoller>();
builder.Services.AddScoped<porganizer.Api.Features.Library.LibraryIndexingService>();
builder.Services.AddScoped<porganizer.Api.Features.Library.LibraryIndexQueueService>();
builder.Services.AddScoped<porganizer.Api.Common.Prdb.IPrdbUserImageCheckService, porganizer.Api.Common.Prdb.PrdbUserImageCheckService>();
builder.Services.AddScoped<porganizer.Api.Features.Library.IThumbnailGenerationService, porganizer.Api.Features.Library.ThumbnailGenerationService>();
builder.Services.AddSingleton<porganizer.Api.Features.Library.ThumbnailQueueService>();
builder.Services.AddScoped<porganizer.Api.Features.Library.IPreviewImageGenerationService, porganizer.Api.Features.Library.PreviewImageGenerationService>();
builder.Services.AddSingleton<porganizer.Api.Features.Library.PreviewQueueService>();
builder.Services.AddScoped<porganizer.Api.Features.Library.VideoUserImageUpload.IVideoUserImageUploadService, porganizer.Api.Features.Library.VideoUserImageUpload.VideoUserImageUploadService>();
builder.Services.AddSingleton<porganizer.Api.Features.Library.VideoUserImageUpload.VideoUserImageUploadQueueService>();
builder.Services.AddScoped<porganizer.Api.Features.Library.Cleanup.ILibraryCleanupService, porganizer.Api.Features.Library.Cleanup.LibraryCleanupService>();
builder.Services.AddScoped<porganizer.Api.Features.Rescue.IRescuePreviewService, porganizer.Api.Features.Rescue.RescuePreviewService>();
builder.Services.AddScoped<porganizer.Api.Features.Rescue.IRescueExecuteService, porganizer.Api.Features.Rescue.RescueExecuteService>();
builder.Services.AddScoped<porganizer.Api.Features.Database.IDatabaseViewService, porganizer.Api.Features.Database.DatabaseViewService>();
builder.Services.AddHostedService<porganizer.Api.Background.SyncWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.QuickSyncWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.DownloadPollingWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.LibraryIndexQueueWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.ThumbnailWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.PreviewWorker>();
builder.Services.AddHostedService<porganizer.Api.Background.VideoUserImageUploadWorker>();

// EF Core / SQLite — DB_PATH env var takes precedence over appsettings
var dbPath = Path.GetFullPath(
    Environment.GetEnvironmentVariable("DB_PATH")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "./data/app.db");

var dataDir = Path.GetDirectoryName(dbPath)!;
Directory.CreateDirectory(dataDir);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Thumbnail cache lives alongside the database
var thumbnailCachePath = Path.Combine(dataDir, "thumbnails");
Directory.CreateDirectory(thumbnailCachePath);
builder.Services.Configure<porganizer.Api.Features.Library.ThumbnailOptions>(opts =>
    opts.CachePath = thumbnailCachePath);

// Preview image cache lives alongside the database, separate from thumbnails
var previewCachePath = Path.Combine(dataDir, "previews");
Directory.CreateDirectory(previewCachePath);
builder.Services.Configure<porganizer.Api.Features.Library.PreviewOptions>(opts =>
    opts.CachePath = previewCachePath);

// CORS — only active in Development to allow the Vite dev server on port 5173
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()));
}

var app = builder.Build();

// Apply any pending EF Core migrations on startup
app.Services.MigrateDatabase();

// Apply the stored log level from the database and sync download library folder
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var storedSettings = await db.GetSettingsAsync();
    if (Enum.TryParse<LogEventLevel>(storedSettings.MinimumLogLevel, out var storedLevel))
        levelSwitch.MinimumLevel = storedLevel;

    var dlFolderService = scope.ServiceProvider
        .GetRequiredService<porganizer.Api.Features.DownloadClients.DownloadLibraryFolderService>();
    await dlFolderService.SyncAsync();
}

// Serilog HTTP request logging
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

// Serve the Vue SPA from wwwroot/
app.UseStaticFiles();

app.UseAuthentication();
app.UseMiddleware<AuthRequiredMiddleware>();

// API routes must be mapped before the SPA fallback
app.MapControllers();

// Fallback to index.html for client-side routing (History Mode)
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
