namespace porganizer.Api.Features.Indexers.Search;

public record UsenetSearchResponse(List<UsenetSearchRowResponse> Items, int Total);

public record UsenetSearchRowResponse(
    Guid Id,
    Guid IndexerId,
    string IndexerName,
    string Title,
    string NzbUrl,
    long NzbSize,
    DateTime? NzbPublishedAt,
    Guid? MatchedVideoId,
    string? MatchedVideoTitle,
    string? PreviewImageUrl,
    bool HasFilehashLink
);
