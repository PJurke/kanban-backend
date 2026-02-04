using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HotChocolate;

namespace KanbanBackend.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the user ID from the JWT "sub" claim.
    /// Throws a GraphQLException if the user ID is not found in the token.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal containing the JWT claims</param>
    /// <returns>The user ID from the token</returns>
    /// <exception cref="GraphQLException">Thrown when the user ID is not found in the token</exception>
    public static string GetRequiredUserId(this ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(userId))
        {
            throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return userId;
    }
}
