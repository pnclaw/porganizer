using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace porganizer.Api.Tests.Auth;

public sealed class AuthTests : IAsyncLifetime
{
    // Factory with auth disabled (the default)
    private readonly PorganizerApiFactory _factoryDisabled = new();

    // Factory with auth enabled
    private readonly PorganizerApiFactory _factoryEnabled = new("alice", "s3cr3t");

    private HttpClient _clientDisabled = null!;
    private HttpClient _clientEnabled = null!;

    public async Task InitializeAsync()
    {
        _clientDisabled = _factoryDisabled.CreateClient();
        _clientEnabled = _factoryEnabled.CreateClient(new() { AllowAutoRedirect = false });
    }

    public async Task DisposeAsync()
    {
        _clientDisabled.Dispose();
        _clientEnabled.Dispose();
        await _factoryDisabled.DisposeAsync();
        await _factoryEnabled.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // GET /api/auth/status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_AuthDisabled_ReturnsNotRequiredAndAuthenticated()
    {
        var response = await _clientDisabled.GetAsync("/api/auth/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthStatusDto>();
        body!.Required.Should().BeFalse();
        body.Authenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Status_AuthEnabled_NoCookie_ReturnsRequiredAndNotAuthenticated()
    {
        var response = await _clientEnabled.GetAsync("/api/auth/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthStatusDto>();
        body!.Required.Should().BeTrue();
        body.Authenticated.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/login
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Login_CorrectCredentials_Returns204AndSetsCookie()
    {
        var response = await _clientEnabled.PostAsJsonAsync("/api/auth/login",
            new { username = "alice", password = "s3cr3t" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Should().ContainKey("Set-Cookie");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await _clientEnabled.PostAsJsonAsync("/api/auth/login",
            new { username = "alice", password = "wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongUsername_Returns401()
    {
        var response = await _clientEnabled.PostAsJsonAsync("/api/auth/login",
            new { username = "eve", password = "s3cr3t" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_AuthDisabled_Returns204WithoutCookie()
    {
        var response = await _clientDisabled.PostAsJsonAsync("/api/auth/login",
            new { username = "anyone", password = "anything" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // -------------------------------------------------------------------------
    // Protected endpoint enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProtectedEndpoint_AuthDisabled_Returns200()
    {
        var response = await _clientDisabled.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_AuthEnabled_NoCookie_Returns401()
    {
        var response = await _clientEnabled.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_AuthEnabled_WithValidCookie_Returns200()
    {
        // Log in to get the cookie
        var loginResponse = await _clientEnabled.PostAsJsonAsync("/api/auth/login",
            new { username = "alice", password = "s3cr3t" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The HttpClient stores the cookie automatically (CookieContainer via CreateClient)
        var response = await _clientEnabled.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/logout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Logout_ClearsSession()
    {
        // Log in
        await _clientEnabled.PostAsJsonAsync("/api/auth/login",
            new { username = "alice", password = "s3cr3t" });

        // Confirm access
        var before = await _clientEnabled.GetAsync("/api/settings");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        // Log out
        var logoutResponse = await _clientEnabled.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Access should now be denied
        var after = await _clientEnabled.GetAsync("/api/settings");
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

file record AuthStatusDto(bool Required, bool Authenticated);
