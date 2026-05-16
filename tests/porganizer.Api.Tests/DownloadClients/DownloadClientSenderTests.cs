using System.Net;
using System.Text.Json;
using porganizer.Api.Features.DownloadClients;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadClients;

public sealed class DownloadClientSenderTests
{
    [Fact]
    public async Task SendAsync_WhenSabnzbdReturnsNzoId_SucceedsWithClientItemId()
    {
        var sender = Sender(_ => Ok(new
        {
            status = true,
            nzo_ids = new[] { "nzo-1" },
        }));

        var (success, message, clientItemId) = await sender.SendAsync(SabnzbdClient(), "https://indexer.test/nzb", "Test.Release");

        success.Should().BeTrue();
        message.Should().Contain("Sent to SABnzbd");
        clientItemId.Should().Be("nzo-1");
    }

    [Fact]
    public async Task SendAsync_WhenSabnzbdSuccessResponseOmitsNzoIds_Fails()
    {
        var sender = Sender(_ => Ok(new
        {
            status = true,
        }));

        var (success, message, clientItemId) = await sender.SendAsync(SabnzbdClient(), "https://indexer.test/nzb", "Test.Release");

        success.Should().BeFalse();
        message.Should().Contain("did not return a download item ID");
        clientItemId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WhenSabnzbdSuccessResponseHasEmptyNzoIds_Fails()
    {
        var sender = Sender(_ => Ok(new
        {
            status = true,
            nzo_ids = Array.Empty<string>(),
        }));

        var (success, message, clientItemId) = await sender.SendAsync(SabnzbdClient(), "https://indexer.test/nzb", "Test.Release");

        success.Should().BeFalse();
        message.Should().Contain("did not return a download item ID");
        clientItemId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WhenSabnzbdSuccessResponseHasBlankNzoId_Fails()
    {
        var sender = Sender(_ => Ok(new
        {
            status = true,
            nzo_ids = new[] { " " },
        }));

        var (success, message, clientItemId) = await sender.SendAsync(SabnzbdClient(), "https://indexer.test/nzb", "Test.Release");

        success.Should().BeFalse();
        message.Should().Contain("did not return a download item ID");
        clientItemId.Should().BeNull();
    }

    private static DownloadClient SabnzbdClient() => new()
    {
        Id = Guid.NewGuid(),
        Title = "SABnzbd Test",
        ClientType = ClientType.Sabnzbd,
        Host = "sabnzbd.test",
        Port = 8080,
        ApiKey = "sabnzbd-key",
        Category = "movies",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static DownloadClientSender Sender(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubHttpClientFactory(responder));

    private static HttpResponseMessage Ok(object body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json"),
        };

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHttpMessageHandler(responder));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
