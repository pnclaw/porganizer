namespace porganizer.Api.Features.Indexers;

public class IndexerStatsResponse
{
    public long TotalSearchRequests { get; set; }
    public long TotalGrabs { get; set; }
    public long TotalRows { get; set; }
    public double? AvgResponseTimeMs { get; set; }

    public long SearchSuccess { get; set; }
    public long SearchFailure { get; set; }

    public List<DailyRequestStat> RequestsPerDay { get; set; } = [];
    public List<DailyResponseTimeStat> AvgResponseTimePerDay { get; set; } = [];
    public List<CategoryStat> RowsByCategory { get; set; } = [];
}

public record DailyRequestStat(string Date, int Search, int Grab);
public record DailyResponseTimeStat(string Date, double AvgMs);
public record CategoryStat(int Category, int Count);
