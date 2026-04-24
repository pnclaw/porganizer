using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace porganizer.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(IOptions<AuthOptions> options) : ControllerBase
{
    [HttpGet("status")]
    [EndpointSummary("Get auth status")]
    [EndpointDescription("Returns whether authentication is required and whether the current request is authenticated.")]
    [ProducesResponseType<AuthStatusResponse>(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var opts = options.Value;
        return Ok(new AuthStatusResponse(
            Required: opts.Enabled,
            Authenticated: !opts.Enabled || User.Identity?.IsAuthenticated == true
        ));
    }

    [HttpPost("login")]
    [EndpointSummary("Log in")]
    [EndpointDescription("Validates credentials and issues a 30-day persistent cookie on success.")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var opts = options.Value;

        if (!opts.Enabled)
            return NoContent();

        if (!string.Equals(request.Username, opts.Username, StringComparison.OrdinalIgnoreCase)
            || request.Password != opts.Password)
            return Unauthorized();

        var claims = new[] { new Claim(ClaimTypes.Name, opts.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            });

        return NoContent();
    }

    [HttpPost("logout")]
    [EndpointSummary("Log out")]
    [EndpointDescription("Clears the session cookie.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public record AuthStatusResponse(bool Required, bool Authenticated);
public record LoginRequest(string Username, string Password);
