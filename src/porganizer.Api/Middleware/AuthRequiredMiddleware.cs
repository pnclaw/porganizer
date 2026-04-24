using Microsoft.Extensions.Options;
using porganizer.Api.Features.Auth;

namespace porganizer.Api.Middleware;

/// <summary>
/// When Auth:Enabled is true, returns 401 for any unauthenticated request
/// to /api/* except /api/auth/*.
/// </summary>
public class AuthRequiredMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (options.Value.Enabled
            && context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/api/auth")
            && context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
