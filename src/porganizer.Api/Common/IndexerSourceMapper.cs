using porganizer.Database.Enums;

namespace porganizer.Api.Common;

public static class IndexerSourceMapper
{
    public static bool TryMap(string indexerUrl, out int indexerSource)
    {
        var lower = indexerUrl.ToLowerInvariant();
        if (lower.Contains("drunkenslug"))
        {
            indexerSource = (int)IndexerSource.DrunkenSlug;
            return true;
        }

        if (lower.Contains("nzbfinder"))
        {
            indexerSource = (int)IndexerSource.NzbFinder;
            return true;
        }

        indexerSource = default;
        return false;
    }
}
