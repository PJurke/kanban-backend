using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Authorization;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory; // Extension methods for GetOrCreate
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class AuthMutations
{
    [AllowAnonymous]
    public async Task<UserPayload> RegisterAsync(
        string email,
        string password,
        [Service] AuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        CheckRateLimit(cache, $"Register_{ip}", 5, TimeSpan.FromMinutes(1)); // Strict: 5/min

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
        [Service] AuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        CheckRateLimit(cache, $"Login_{ip}", 5, TimeSpan.FromMinutes(1)); // Strict: 5/min

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
        [Service] AuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        var ip = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        CheckRateLimit(cache, $"Refresh_{ip}", 30, TimeSpan.FromMinutes(1)); // Moderate: 30/min

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
        [Service] AuthService authService,
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
        [Service] AuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
             throw new GraphQLException(new Error("Unauthorized", "AUTH_REQUIRED"));
        }

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
