namespace porganizer.Api.Features.Prdb.Sync;

// Internal models for deserializing prdb.net API responses

record PrdbApiPagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

record PrdbApiSite(
    Guid Id,
    string Title,
    string Url,
    Guid? NetworkId,
    string? NetworkTitle);

record PrdbApiVideo(
    Guid Id,
    string Title,
    Guid SiteId,
    string SiteTitle,
    DateOnly? ReleaseDate);

record PrdbApiFavoriteSite(
    Guid Id,
    string Title,
    string Url,
    Guid? NetworkId,
    string? NetworkTitle,
    DateTime FavoritedAtUtc);

record PrdbApiFavoriteActor(
    Guid Id,
    string Name,
    string Gender,
    string Nationality,
    string Ethnicity,
    string? ProfileImageCdnPath,
    DateTime FavoritedAtUtc);

record PrdbApiFavoriteSiteChangesResponse(
    List<PrdbApiFavoriteSiteChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiFavoriteSiteChangesCursorDto? NextCursor);

record PrdbApiFavoriteSiteChangeDto(
    string EventType,
    PrdbApiFavoriteSiteChangeFavoriteSiteDto FavoriteSite);

record PrdbApiFavoriteSiteChangeFavoriteSiteDto(
    Guid Id,
    string Title,
    string Url,
    Guid? NetworkId,
    string? NetworkTitle,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime FavoritedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiFavoriteSiteChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiFavoriteActorChangesResponse(
    List<PrdbApiFavoriteActorChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiFavoriteActorChangesCursorDto? NextCursor);

record PrdbApiFavoriteActorChangeDto(
    string EventType,
    PrdbApiFavoriteActorChangeFavoriteActorDto FavoriteActor);

record PrdbApiFavoriteActorChangeFavoriteActorDto(
    Guid Id,
    string Name,
    string Gender,
    string Nationality,
    string Ethnicity,
    string? ProfileImageCdnPath,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime FavoritedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiFavoriteActorChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiActorSummary(
    Guid Id,
    string Name,
    int Gender,
    int Nationality,
    int Ethnicity,
    DateOnly? Birthday,
    string? ProfileImageUrl);

record PrdbApiActorDetail(
    Guid Id,
    string Name,
    int Gender,
    DateOnly? Birthday,
    int? BirthdayType,
    DateOnly? Deathday,
    string? Birthplace,
    int Haircolor,
    int Eyecolor,
    int BreastType,
    int? Height,
    int? BraSize,
    string? BraSizeLabel,
    int? WaistSize,
    int? HipSize,
    int Nationality,
    int Ethnicity,
    int? CareerStart,
    int? CareerEnd,
    string? Tattoos,
    string? Piercings,
    List<PrdbApiActorImageDetail> Images,
    List<PrdbApiActorAliasDetail> Aliases,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiActorAliasDetail(string Name, Guid? SiteId);

record PrdbApiActorImageDetail(Guid Id, int ImageType, string? Url);

record PrdbApiBatchActorsRequest(List<Guid> Ids);
record PrdbApiBatchVideosRequest(List<Guid> Ids);

record PrdbApiWantedVideoSummary(
    Guid VideoId,
    string VideoTitle,
    string SiteTitle,
    DateOnly? VideoReleaseDate,
    DateTime? VideoCreatedAtUtc,
    string? ImageCdnPath,
    bool IsFulfilled,
    DateTime? FulfilledAtUtc,
    int? FulfilledInQuality,
    string? FulfillmentExternalId,
    int? FulfillmentByApp,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiWantedVideoChangesResponse(
    List<PrdbApiWantedVideoChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiWantedVideoChangesCursorDto? NextCursor);

record PrdbApiWantedVideoChangeDto(
    string EventType,
    PrdbApiWantedVideoChangeWantedVideoDto WantedVideo);

record PrdbApiWantedVideoChangeWantedVideoDto(
    Guid VideoId,
    string VideoTitle,
    string SiteTitle,
    DateOnly? VideoReleaseDate,
    DateTime? VideoCreatedAtUtc,
    string? ImageCdnPath,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    bool IsFulfilled,
    DateTime? FulfilledAtUtc,
    int? FulfilledInQuality,
    string? FulfillmentExternalId,
    int? FulfillmentByApp,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiWantedVideoChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiVideoDetail(
    Guid Id,
    string Title,
    DateOnly? ReleaseDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    PrdbApiVideoDetailSite Site,
    List<PrdbApiVideoDetailImage> Images,
    List<PrdbApiVideoDetailPreName> PreNames,
    List<PrdbApiVideoDetailActor> Actors);

record PrdbApiVideoDetailSite(Guid Id, string Title, string Url);

record PrdbApiVideoDetailImage(Guid Id, string? CdnPath);

record PrdbApiVideoDetailPreName(Guid Id, string Title);

record PrdbApiLatestPreDbSite(Guid Id, string Title);
record PrdbApiLatestPreDbVideo(Guid Id, string Title, DateOnly? ReleaseDate, PrdbApiLatestPreDbSite Site);
record PrdbApiLatestPreDbItem(Guid Id, string Title, DateTime CreatedAtUtc, PrdbApiLatestPreDbVideo? Video);

record PrdbApiVideoFilehashDto(
    Guid Id,
    Guid? VideoId,
    string Filename,
    string? OsHash,
    string? PHash,
    long Filesize,
    int SubmissionCount,
    bool IsVerified,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiVideoFilehashChangesResponse(
    List<PrdbApiVideoFilehashChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiVideoFilehashChangesCursorDto? NextCursor);

record PrdbApiVideoFilehashChangeDto(
    string EventType,
    PrdbApiVideoFilehashChangeFilehashDto Filehash);

record PrdbApiVideoFilehashChangeFilehashDto(
    Guid Id,
    Guid? VideoId,
    string Filename,
    string? OsHash,
    string? PHash,
    long Filesize,
    int SubmissionCount,
    bool IsVerified,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiVideoFilehashChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiIndexerFilehashDto(
    Guid Id,
    string IndexerSource,
    string IndexerId,
    string Filename,
    string OsHash,
    string? PHash,
    long Filesize,
    int SubmissionCount,
    bool IsVerified,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiIndexerFilehashChangesResponse(
    List<PrdbApiIndexerFilehashChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiIndexerFilehashChangesCursorDto? NextCursor);

record PrdbApiIndexerFilehashChangeDto(
    string EventType,
    PrdbApiIndexerFilehashChangeFilehashDto Filehash);

record PrdbApiIndexerFilehashChangeFilehashDto(
    Guid Id,
    string IndexerSource,
    string IndexerId,
    string Filename,
    string OsHash,
    string? PHash,
    long Filesize,
    int SubmissionCount,
    bool IsVerified,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

record PrdbApiIndexerFilehashChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiVideoUserImageDto(
    Guid Id,
    Guid? VideoId,
    string PreviewImageType,
    int DisplayOrder,
    string Url,
    string ModerationVisibility,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int? SpriteTileCount,
    int? SpriteTileWidth,
    int? SpriteTileHeight,
    int? SpriteColumns,
    int? SpriteRows);

record PrdbApiVideoUserImageChangesResponse(
    List<PrdbApiVideoUserImageChangeDto> Items,
    int PageSize,
    bool HasMore,
    PrdbApiVideoUserImageChangesCursorDto? NextCursor);

record PrdbApiVideoUserImageChangeDto(
    string EventType,
    PrdbApiVideoUserImageDto VideoUserImage);

record PrdbApiVideoUserImageChangesCursorDto(
    DateTime UpdatedAtUtc,
    Guid Id);

record PrdbApiVideoDetailActor(
    Guid Id,
    string Name,
    int Gender,
    DateOnly? Birthday,
    int Nationality,
    List<PrdbApiVideoDetailActorImage> Images);

record PrdbApiVideoDetailActorImage(Guid Id, string? CdnPath, int ImageType);
