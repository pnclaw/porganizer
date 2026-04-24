namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbSyncResult
{
    public int NetworksUpserted { get; init; }
    public int SitesUpserted { get; init; }
    public int VideosUpserted { get; init; }
    public int FavoriteSitesSynced { get; init; }
    public int FavoriteActorsSynced { get; init; }
    public int VideoUserImagesSynced { get; init; }
}
