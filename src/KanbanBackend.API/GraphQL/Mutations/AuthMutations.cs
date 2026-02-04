using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Authorization;
using KanbanBackend.API.Configuration;
using KanbanBackend.API.Extensions;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory; // Extension methods for GetOrCreate
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class AuthMutations
{
    [AllowAnonymous]
    public async Task<UserPayload> RegisterAsync(
        string email,
        string password,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        [Service] IOptions<RateLimitingOptions> rateLimitingOptions)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        var options = rateLimitingOptions.Value;
        CheckRateLimit(cache, $"Register_{ip}", options.RegisterLimit, TimeSpan.FromMinutes(options.WindowMinutes));

        var result = await authService.RegisterAsync(email, password);
        if (!result.Succeeded)
        {
            throw new GraphQLException(result.Errors.Select(e => new Error(e.Description, "AUTH_ERROR")).ToArray());
        }

        return new UserPayload(email);
    }

    [AllowAnonymous]
    public async Task<AuthPayload> LoginAsync(
        string email,
        string password,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        [Service] IOptions<RateLimitingOptions> rateLimitingOptions)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        var options = rateLimitingOptions.Value;
        CheckRateLimit(cache, $"Login_{ip}", options.LoginLimit, TimeSpan.FromMinutes(options.WindowMinutes));

        var result = await authService.LoginAsync(email, password);
        if (result == null)
        {
            throw new GraphQLException(new Error("Invalid credentials", "AUTH_FAILED"));
        }

        SetRefreshTokenCookie(httpContextAccessor.HttpContext!, result.RefreshToken, result.RefreshTokenExpires);

        return new AuthPayload(result.AccessToken, new UserPayload(email));
    }

    [AllowAnonymous]
    public async Task<AuthPayload> RefreshTokenAsync(
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        [Service] IOptions<RateLimitingOptions> rateLimitingOptions)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        var options = rateLimitingOptions.Value;
        CheckRateLimit(cache, $"Refresh_{ip}", options.RefreshLimit, TimeSpan.FromMinutes(options.WindowMinutes));

        var refreshToken = httpContextAccessor.HttpContext!.Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
        {
             throw new GraphQLException(new Error("No refresh token found", "AUTH_NO_TOKEN"));
        }

        var result = await authService.RefreshAsync(refreshToken);
        if (result == null)
        {
            // Clear invalid cookie
            httpContextAccessor.HttpContext!.Response.Cookies.Delete("refreshToken");
            throw new GraphQLException(new Error("Invalid refresh token", "AUTH_INVALID_TOKEN"));
        }

        SetRefreshTokenCookie(httpContextAccessor.HttpContext!, result.RefreshToken, result.RefreshTokenExpires);

        // Get email... (simplified extraction as per previous step)
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.AccessToken);
        var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? "";

        return new AuthPayload(result.AccessToken, new UserPayload(email));
    }

    private void CheckRateLimit(Microsoft.Extensions.Caching.Memory.IMemoryCache cache, string key, int limit, TimeSpan window)
    {
        var count = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return 0;
        });

        if (count >= limit)
        {
            throw new GraphQLException(new Error("Rate limit exceeded", "AUTH_RATE_LIMIT"));
        }

        cache.Set(key, count + 1, window);
    }

    public async Task<bool> LogoutAsync(
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var refreshToken = httpContextAccessor.HttpContext!.Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await authService.RevokeAsync(refreshToken, httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        httpContextAccessor.HttpContext!.Response.Cookies.Delete("refreshToken");
        return true;
    }

    [Authorize]
    public async Task<bool> DeleteAccountAsync(
        string password,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();

        var result = await authService.DeleteAccountAsync(userId, password);
        if (!result.Succeeded)
        {
             throw new GraphQLException(result.Errors.Select(e => new Error(e.Description, "AUTH_DELETE_FAILED")).ToArray());
        }

        // Cleanup Cookie
        httpContextAccessor.HttpContext!.Response.Cookies.Delete("refreshToken");

        return true;
    }

    private void SetRefreshTokenCookie(HttpContext context, string token, DateTimeOffset expires)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps, // Dynamic secure flag
            SameSite = SameSiteMode.Lax, // Adjust based on requirements (None if cross-site)
            Expires = expires,
            Path = "/graphql" // Restrict to GraphQL endpoint
        };

        context.Response.Cookies.Append("refreshToken", token, cookieOptions);
    }
}

public record UserPayload(string Email); // Simple payload
public record AuthPayload(string AccessToken, UserPayload User);
