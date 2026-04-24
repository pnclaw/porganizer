using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadClientTester(IHttpClientFactory httpClientFactory)
{
    public async Task<(bool Success, string Message)> TestAsync(
        ClientType clientType, string host, int port, bool useSsl,
        string apiKey, string username, string password,
        CancellationToken ct = default)
    {
        return clientType switch
        {
            ClientType.Sabnzbd => await TestSabnzbdAsync(host, port, useSsl, apiKey, ct),
            ClientType.Nzbget  => await TestNzbgetAsync(host, port, useSsl, username, password, ct),
            _ => (false, $"Unknown client type: {clientType}"),
        };
    }

    private async Task<(bool, string)> TestSabnzbdAsync(
        string host, int port, bool useSsl, string apiKey, CancellationToken ct)
    {
        var scheme = useSsl ? "https" : "http";
        // Use mode=queue to verify both connectivity and API key
        var mode = string.IsNullOrEmpty(apiKey) ? "version" : "queue";
        var url = $"{scheme}://{host}:{port}/api?mode={mode}&output=json&apikey={apiKey}";

        try
        {
            var client = CreateClient();
            var response = await client.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorEl))
                return (false, $"SABnzbd error: {errorEl.GetString()}");

            return (true, "Connected to SABnzbd successfully");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    private async Task<(bool, string)> TestNzbgetAsync(
        string host, int port, bool useSsl, string username, string password, CancellationToken ct)
    {
        var scheme = useSsl ? "https" : "http";
        var url = $"{scheme}://{host}:{port}/jsonrpc";

        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "version",
            @params = Array.Empty<object>(),
            id = 1,
        });

        try
        {
            var client = CreateClient();

            if (!string.IsNullOrEmpty(username))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var httpResponse = await client.PostAsync(url, content, ct);

            if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "NZBGet: invalid username or password");

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out _))
                return (false, "NZBGet returned an error — check credentials");

            if (root.TryGetProperty("result", out var resultEl))
                return (true, $"Connected to NZBGet {resultEl.GetString()} successfully");

            return (false, "Unexpected response from NZBGet");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }
}
