using Microsoft.EntityFrameworkCore;

namespace porganizer.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<IndexerRow> IndexerRows => Set<IndexerRow>();
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    public DbSet<IndexerApiRequest> IndexerApiRequests => Set<IndexerApiRequest>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<PrdbNetwork> PrdbNetworks => Set<PrdbNetwork>();
    public DbSet<PrdbSite> PrdbSites => Set<PrdbSite>();
    public DbSet<PrdbVideo> PrdbVideos => Set<PrdbVideo>();
    public DbSet<PrdbPreDbEntry> PrdbPreDbEntries => Set<PrdbPreDbEntry>();
    public DbSet<PrdbVideoImage> PrdbVideoImages => Set<PrdbVideoImage>();
    public DbSet<PrdbActor> PrdbActors => Set<PrdbActor>();
    public DbSet<PrdbActorImage> PrdbActorImages => Set<PrdbActorImage>();
    public DbSet<PrdbActorAlias> PrdbActorAliases => Set<PrdbActorAlias>();
    public DbSet<PrdbVideoActor> PrdbVideoActors => Set<PrdbVideoActor>();
    public DbSet<PrdbWantedVideo> PrdbWantedVideos => Set<PrdbWantedVideo>();
    public DbSet<IndexerRowMatch> IndexerRowMatches => Set<IndexerRowMatch>();
    public DbSet<FolderMapping> FolderMappings => Set<FolderMapping>();
    public DbSet<DownloadLog> DownloadLogs => Set<DownloadLog>();
    public DbSet<DownloadLogFile> DownloadLogFiles => Set<DownloadLogFile>();
    public DbSet<PrdbVideoFilehash> PrdbVideoFilehashes => Set<PrdbVideoFilehash>();
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();
    public DbSet<LibraryFile> LibraryFiles => Set<LibraryFile>();
    public DbSet<LibraryIndexRequest> LibraryIndexRequests => Set<LibraryIndexRequest>();
    public DbSet<VideoUserImageUpload> VideoUserImageUploads => Set<VideoUserImageUpload>();
    public DbSet<PrdbVideoUserImage> PrdbVideoUserImages => Set<PrdbVideoUserImage>();
    public DbSet<PrdbIndexerFilehash> PrdbIndexerFilehashes => Set<PrdbIndexerFilehash>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasData(new AppSettings { Id = 1 });
        });

        modelBuilder.Entity<PrdbVideoActor>(e =>
        {
            e.HasKey(va => new { va.VideoId, va.ActorId });
        });

        modelBuilder.Entity<PrdbWantedVideo>(e =>
        {
            e.HasKey(w => w.VideoId);
            e.HasOne(w => w.Video)
             .WithMany()
             .HasForeignKey(w => w.VideoId);
        });

        modelBuilder.Entity<IndexerRowMatch>(e =>
        {
            e.HasIndex(m => m.IndexerRowId).IsUnique();

            e.HasOne(m => m.MatchedPreDbEntry)
             .WithMany()
             .HasForeignKey(m => m.MatchedPreDbEntryId);
        });

        modelBuilder.Entity<FolderMapping>(e =>
        {
            e.HasIndex(f => f.OriginalFolder).IsUnique();
            e.HasIndex(f => f.MappedToFolder).IsUnique();
        });

        modelBuilder.Entity<DownloadLogFile>(e =>
        {
            e.HasOne(f => f.DownloadLog)
             .WithMany(l => l.Files)
             .HasForeignKey(f => f.DownloadLogId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(f => f.DownloadLogId);
        });

        modelBuilder.Entity<PrdbVideoFilehash>(e =>
        {
            e.HasIndex(f => f.PrdbCreatedAtUtc);
            e.HasIndex(f => f.VideoId);
        });

        modelBuilder.Entity<LibraryFolder>(e =>
        {
            e.HasIndex(f => f.Path).IsUnique();
        });

        modelBuilder.Entity<LibraryFile>(e =>
        {
            e.HasIndex(f => f.LibraryFolderId);
            e.HasIndex(f => new { f.LibraryFolderId, f.RelativePath }).IsUnique();
            e.HasIndex(f => f.VideoId);
            e.HasIndex(f => f.OsHash);

            e.HasOne(f => f.Folder)
             .WithMany(fo => fo.Files)
             .HasForeignKey(f => f.LibraryFolderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(f => f.Video)
             .WithMany()
             .HasForeignKey(f => f.VideoId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LibraryIndexRequest>(e =>
        {
            e.HasKey(r => r.LibraryFolderId);
            e.HasIndex(r => r.RequestedAtUtc);

            e.HasOne(r => r.Folder)
             .WithMany()
             .HasForeignKey(r => r.LibraryFolderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VideoUserImageUpload>(e =>
        {
            e.HasIndex(u => u.LibraryFileId);
            e.HasIndex(u => u.PrdbVideoId);

            e.HasOne(u => u.LibraryFile)
             .WithMany()
             .HasForeignKey(u => u.LibraryFileId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrdbVideoUserImage>(e =>
        {
            e.HasIndex(i => i.VideoId);
            e.HasIndex(i => i.PrdbUpdatedAtUtc);
        });

        modelBuilder.Entity<PrdbIndexerFilehash>(e =>
        {
            e.HasIndex(f => f.OsHash);
            e.HasIndex(f => f.PrdbUpdatedAtUtc);
            e.HasIndex(f => new { f.IndexerSource, f.IndexerId });
        });

        modelBuilder.Entity<PrdbPreDbEntry>(e =>
        {
            e.HasIndex(p => p.CreatedAtUtc);
            e.HasIndex(p => p.Title);
            e.HasIndex(p => p.PrdbVideoId);

            e.HasOne(p => p.Video)
             .WithMany(v => v.PreDbEntries)
             .HasForeignKey(p => p.PrdbVideoId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(p => p.Site)
             .WithMany(s => s.PreDbEntries)
             .HasForeignKey(p => p.PrdbSiteId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
