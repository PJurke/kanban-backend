using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using HotChocolate;
using KanbanBackend.API.Extensions;
using Xunit;

namespace KanbanBackend.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetRequiredUserId_WithValidSubClaim_ReturnsUserId()
    {
        // Arrange
        var userId = "test-user-123";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var result = claimsPrincipal.GetRequiredUserId();

        // Assert
        result.Should().Be(userId);
    }

    [Fact]
    public void GetRequiredUserId_WithMissingSubClaim_ThrowsGraphQLException()
    {
        // Arrange
        var identity = new ClaimsIdentity();
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var act = () => claimsPrincipal.GetRequiredUserId();

        // Assert
        act.Should().Throw<GraphQLException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("AUTH_INVALID_TOKEN");
    }

    [Fact]
    public void GetRequiredUserId_WithEmptySubClaim_ThrowsGraphQLException()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "")
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var act = () => claimsPrincipal.GetRequiredUserId();

        // Assert
        act.Should().Throw<GraphQLException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("AUTH_INVALID_TOKEN");
    }

    [Fact]
    public void GetRequiredUserId_WithWhitespaceSubClaim_ReturnsWhitespace()
    {
        // Arrange
        var userId = "   ";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var result = claimsPrincipal.GetRequiredUserId();

        // Assert
        result.Should().Be(userId);
    }
}
