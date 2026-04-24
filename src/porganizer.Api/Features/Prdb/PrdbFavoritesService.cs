using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using System.Net;

namespace porganizer.Api.Features.Prdb;

public class PrdbFavoritesService(AppDbContext db, IHttpClientFactory httpClientFactory)
{
    public async Task SetSiteFavoriteAsync(Guid siteId, bool favorite, CancellationToken ct)
    {
        var http = await CreateClientAsync(ct);
        await CallPrdbAsync(http, favorite ? HttpMethod.Post : HttpMethod.Delete,
            $"favorite-sites/{siteId}", ct);

        var site = await db.PrdbSites.FindAsync([siteId], ct);
        if (site is not null)
        {
            site.IsFavorite     = favorite;
            site.FavoritedAtUtc = favorite ? DateTime.UtcNow : null;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetActorFavoriteAsync(Guid actorId, bool favorite, CancellationToken ct)
    {
        var http = await CreateClientAsync(ct);
        await CallPrdbAsync(http, favorite ? HttpMethod.Post : HttpMethod.Delete,
            $"favorite-actors/{actorId}", ct);

        var actor = await db.PrdbActors.FindAsync([actorId], ct);
        if (actor is not null)
        {
            actor.IsFavorite     = favorite;
            actor.FavoritedAtUtc = favorite ? DateTime.UtcNow : null;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }

    private static async Task CallPrdbAsync(HttpClient http, HttpMethod method, string path, CancellationToken ct)
    {
        var response = await http.SendAsync(new HttpRequestMessage(method, path), ct);
        if (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound)
            return;
        response.EnsureSuccessStatusCode();
    }
}
