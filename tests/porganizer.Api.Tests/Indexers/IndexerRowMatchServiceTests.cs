using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.Indexers.Matching;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Indexers;

public sealed class IndexerRowMatchServiceTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private AppDbContext _db = null!;
    private IndexerRowMatchService _service = null!;
    private IServiceScope _scope = null!;

    public Task InitializeAsync()
    {
        _scope   = _factory.Services.CreateScope();
        _db      = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _service = _scope.ServiceProvider.GetRequiredService<IndexerRowMatchService>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _factory.DisposeAsync();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Indexer> SeedIndexerAsync()
    {
        var indexer = new Indexer
        {
            Id         = Guid.NewGuid(),
            Title      = "Test Indexer",
            Url        = "https://example.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled  = true,
        };
        _db.Indexers.Add(indexer);
        await _db.SaveChangesAsync();
        return indexer;
    }

    private async Task<IndexerRow> SeedIndexerRowAsync(Guid indexerId, string title, DateTime? createdAt = null)
    {
        var row = new IndexerRow
        {
            Id        = Guid.NewGuid(),
            IndexerId = indexerId,
            Title     = title,
            NzbId     = Guid.NewGuid().ToString(),
            NzbUrl    = "https://example.com/nzb",
            Category  = 6000,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        _db.IndexerRows.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<(PrdbVideo video, PrdbPreDbEntry preDbEntry)> SeedVideoWithPreNameAsync(string prename)
    {
        var network = new PrdbNetwork { Id = Guid.NewGuid(), Title = "Net" };
        var site    = new PrdbSite    { Id = Guid.NewGuid(), Title = "Site", NetworkId = network.Id };
        var video   = new PrdbVideo   { Id = Guid.NewGuid(), Title = "Video", SiteId = site.Id, SyncedAtUtc = DateTime.UtcNow };
        var pn      = new PrdbPreDbEntry
        {
            Id           = Guid.NewGuid(),
            Title        = prename,
            CreatedAtUtc = DateTime.UtcNow,
            PrdbVideoId  = video.Id,
            PrdbSiteId   = site.Id,
            VideoTitle   = video.Title,
            SiteTitle    = site.Title,
            SyncedAtUtc  = DateTime.UtcNow,
        };

        _db.PrdbNetworks.Add(network);
        _db.PrdbSites.Add(site);
        _db.PrdbVideos.Add(video);
        _db.PrdbPreDbEntries.Add(pn);
        await _db.SaveChangesAsync();

        return (video, pn);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_SinglePreNameMatch_CreatesIndexerRowMatch()
    {
        var indexer         = await SeedIndexerAsync();
        var (video, pn)     = await SeedVideoWithPreNameAsync("Some.Scene.Title");
        var row             = await SeedIndexerRowAsync(indexer.Id, "Some.Scene.Title");

        await _service.RunAsync(CancellationToken.None);

        var match = _db.IndexerRowMatches.SingleOrDefault(m => m.IndexerRowId == row.Id);
        match.Should().NotBeNull();
        match!.PrdbVideoId.Should().Be(video.Id);
        match.MatchedPreDbEntryId.Should().Be(pn.Id);
        match.MatchedTitle.Should().Be("Some.Scene.Title");
        match.MatchedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Run_CaseInsensitiveTitle_CreatesMatch()
    {
        var indexer     = await SeedIndexerAsync();
        var (video, pn) = await SeedVideoWithPreNameAsync("UPPER.CASE.TITLE");
        var row         = await SeedIndexerRowAsync(indexer.Id, "upper.case.title");

        await _service.RunAsync(CancellationToken.None);

        _db.IndexerRowMatches.Should().ContainSingle(m => m.IndexerRowId == row.Id);
    }

    [Fact]
    public async Task Run_NoMatchingPreName_CreatesNoMatch()
    {
        var indexer = await SeedIndexerAsync();
        var row     = await SeedIndexerRowAsync(indexer.Id, "Title.With.No.Match");

        await _service.RunAsync(CancellationToken.None);

        _db.IndexerRowMatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_MultiplePreNameMatches_CreatesNoMatchAndLogsWarning()
    {
        var indexer        = await SeedIndexerAsync();
        await SeedVideoWithPreNameAsync("Ambiguous.Title");
        await SeedVideoWithPreNameAsync("Ambiguous.Title");
        var row            = await SeedIndexerRowAsync(indexer.Id, "Ambiguous.Title");

        await _service.RunAsync(CancellationToken.None);

        _db.IndexerRowMatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_AlreadyMatchedRow_IsNotProcessedAgain()
    {
        var indexer     = await SeedIndexerAsync();
        var (video, pn) = await SeedVideoWithPreNameAsync("Already.Matched");
        var row         = await SeedIndexerRowAsync(indexer.Id, "Already.Matched");

        // First run — creates the match
        await _service.RunAsync(CancellationToken.None);
        _db.IndexerRowMatches.Should().HaveCount(1);

        // Second run — should not create a duplicate
        await _service.RunAsync(CancellationToken.None);
        _db.IndexerRowMatches.Should().HaveCount(1);
    }

    [Fact]
    public async Task Run_OldRow_OutsideWindow_IsIgnored()
    {
        var indexer     = await SeedIndexerAsync();
        await SeedVideoWithPreNameAsync("Old.Row.Title");
        // Row created 8 days ago — outside the 7-day window
        var row = await SeedIndexerRowAsync(indexer.Id, "Old.Row.Title",
            createdAt: DateTime.UtcNow.AddDays(-8));

        await _service.RunAsync(CancellationToken.None);

        _db.IndexerRowMatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_DotSeparatedRow_MatchesDashSeparatedPreName()
    {
        var indexer     = await SeedIndexerAsync();
        var (video, pn) = await SeedVideoWithPreNameAsync("Some-Scene-Title");
        var row         = await SeedIndexerRowAsync(indexer.Id, "Some.Scene.Title");

        await _service.RunAsync(CancellationToken.None);

        var match = _db.IndexerRowMatches.SingleOrDefault(m => m.IndexerRowId == row.Id);
        match.Should().NotBeNull();
        match!.PrdbVideoId.Should().Be(video.Id);
    }

    [Fact]
    public async Task Run_MixedSeparators_MatchesSpaceSeparatedPreName()
    {
        var indexer     = await SeedIndexerAsync();
        var (video, pn) = await SeedVideoWithPreNameAsync("Some Scene Title");
        var row         = await SeedIndexerRowAsync(indexer.Id, "Some.Scene-Title");

        await _service.RunAsync(CancellationToken.None);

        var match = _db.IndexerRowMatches.SingleOrDefault(m => m.IndexerRowId == row.Id);
        match.Should().NotBeNull();
        match!.PrdbVideoId.Should().Be(video.Id);
    }

    [Fact]
    public async Task Run_UnderscoreSeparatedRow_MatchesDotSeparatedPreName()
    {
        var indexer     = await SeedIndexerAsync();
        var (video, pn) = await SeedVideoWithPreNameAsync("Some.Scene.Title");
        var row         = await SeedIndexerRowAsync(indexer.Id, "Some_Scene_Title");

        await _service.RunAsync(CancellationToken.None);

        var match = _db.IndexerRowMatches.SingleOrDefault(m => m.IndexerRowId == row.Id);
        match.Should().NotBeNull();
        match!.PrdbVideoId.Should().Be(video.Id);
    }

    [Fact]
    public async Task Run_UpdatesLastRunAt()
    {
        var indexer = await SeedIndexerAsync();
        await _service.RunAsync(CancellationToken.None);

        var settings = _db.AppSettings.Single();
        settings.IndexerRowMatchLastRunAt.Should().NotBeNull();
        settings.IndexerRowMatchLastRunAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
