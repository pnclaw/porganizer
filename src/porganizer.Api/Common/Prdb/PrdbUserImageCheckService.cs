using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using porganizer.Database;

namespace porganizer.Api.Common.Prdb;

public interface IPrdbUserImageCheckService
{
    Task<int?> GetUserImageCountAsync(Guid videoId, AppSettings settings, CancellationToken ct);
    Task<int?> GetUserImageCountByOsHashAsync(string osHash, AppSettings settings, CancellationToken ct);
}

public class PrdbUserImageCheckService(
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbUserImageCheckService> logger) : IPrdbUserImageCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<int?> GetUserImageCountAsync(Guid videoId, AppSettings settings, CancellationToken ct)
    {
        var http = CreateClient(settings);

        try
        {
            var response = await http.GetAsync($"videos/{videoId}/user-images", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning("PrdbUserImageCheckService: video {VideoId} not found on prdb.net", videoId);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<List<UserImageSummaryDto>>(JsonOptions, ct);
            return result?.Count ?? 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PrdbUserImageCheckService: failed to check existing user images for video {VideoId}", videoId);
            return null;
        }
    }

    public async Task<int?> GetUserImageCountByOsHashAsync(string osHash, AppSettings settings, CancellationToken ct)
    {
        var http = CreateClient(settings);

        try
        {
            var response = await http.GetAsync($"video-user-images/by-os-hash/{osHash}", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<List<UserImageSummaryDto>>(JsonOptions, ct);
            return result?.Count ?? 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PrdbUserImageCheckService: failed to check existing user images for os hash {OsHash}", osHash);
            return null;
        }
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }

    private sealed class UserImageSummaryDto
    {
        public Guid Id { get; set; }
    }
}
