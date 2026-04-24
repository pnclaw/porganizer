using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadClientSender(IHttpClientFactory httpClientFactory)
{
    public async Task<(bool Success, string Message, string? ClientItemId)> SendAsync(
        DownloadClient client, string nzbUrl, string name, CancellationToken ct = default)
    {
        return client.ClientType switch
        {
            ClientType.Sabnzbd => await SendToSabnzbdAsync(client, nzbUrl, name, ct),
            ClientType.Nzbget  => await SendToNzbgetAsync(client, nzbUrl, name, ct),
            _                  => (false, $"Unsupported client type: {client.ClientType}", null),
        };
    }

    private async Task<(bool, string, string?)> SendToSabnzbdAsync(
        DownloadClient client, string nzbUrl, string name, CancellationToken ct)
    {
        var scheme = client.UseSsl ? "https" : "http";
        var url = $"{scheme}://{client.Host}:{client.Port}/api" +
                  $"?mode=addurl" +
                  $"&name={Uri.EscapeDataString(nzbUrl)}" +
                  $"&nzbname={Uri.EscapeDataString(name)}" +
                  $"&cat={Uri.EscapeDataString(client.Category)}" +
                  $"&apikey={client.ApiKey}" +
                  $"&output=json";
        try
        {
            var http = CreateClient();
            var response = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errEl))
                return (false, $"SABnzbd: {errEl.GetString()}", null);

            if (root.TryGetProperty("status", out var statusEl) && !statusEl.GetBoolean())
                return (false, "SABnzbd rejected the download", null);

            string? nzoId = null;
            if (root.TryGetProperty("nzo_ids", out var nzoIdsEl) &&
                nzoIdsEl.ValueKind == JsonValueKind.Array &&
                nzoIdsEl.GetArrayLength() > 0)
            {
                nzoId = nzoIdsEl[0].GetString();
            }

            return (true, $"Sent to SABnzbd ({client.Title})", nzoId);
        }
        catch (TaskCanceledException) { return (false, "Request timed out", null); }
        catch (HttpRequestException ex) { return (false, $"Connection failed: {ex.Message}", null); }
        catch (Exception ex) { return (false, $"Error: {ex.Message}", null); }
    }

    private async Task<(bool, string, string?)> SendToNzbgetAsync(
        DownloadClient client, string nzbUrl, string name, CancellationToken ct)
    {
        var scheme = client.UseSsl ? "https" : "http";
        var url = $"{scheme}://{client.Host}:{client.Port}/jsonrpc";

        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "appendurl",
            @params = new object[] { name, client.Category, 0, false, nzbUrl },
            id = 1,
        });

        try
        {
            var http = CreateClient();

            if (!string.IsNullOrEmpty(client.Username))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
            }

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var httpResponse = await http.PostAsync(url, content, ct);

            if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "NZBGet: invalid username or password", null);

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out _))
                return (false, "NZBGet returned an error", null);

            if (root.TryGetProperty("result", out var resultEl))
            {
                var nzbId = resultEl.GetInt32();
                if (nzbId > 0)
                    return (true, $"Sent to NZBGet ({client.Title})", nzbId.ToString());
            }

            return (false, "NZBGet rejected the download (result=0)", null);
        }
        catch (TaskCanceledException) { return (false, "Request timed out", null); }
        catch (HttpRequestException ex) { return (false, $"Connection failed: {ex.Message}", null); }
        catch (Exception ex) { return (false, $"Error: {ex.Message}", null); }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }
}
