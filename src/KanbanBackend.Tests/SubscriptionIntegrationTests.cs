using FluentAssertions;
using HotChocolate.Execution;
using KanbanBackend.API.Data;
using KanbanBackend.Tests.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace KanbanBackend.Tests;

public class SubscriptionIntegrationTests : IntegrationTestBase
{
    public SubscriptionIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task Subscription_Auth_SucceedsForOwner()
    {
        // 1. Setup Data via HTTP
        var (client, _, email) = await CreateAuthenticatedClientAsync();
        
        var board = await new BoardBuilder(client).WithName("My Sub Board").BuildAsync();
        var boardId = board.BoardId;

        // 2. Execute Subscription directly against Schema
        var executor = await Factory.Services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument($@"subscription {{ onCardMoved(boardId: ""{boardId}"") {{ id }} }}")
            .AddGlobalState("ClaimsPrincipal", CreateClaimsPrincipal(email)) 
            .Build();

        var result = await executor.ExecuteAsync(request);

        // 3. Assert
        if (result is IResponseStream)
        {
             // Success
        }
        else
        {
             dynamic dynamicResult = result;
             var errors = (IEnumerable<object>)dynamicResult.Errors; 
             // errors.Should().BeNull(); // dynamic dispatch issues with FA sometimes
             ((object)dynamicResult.Errors).Should().BeNull(); // Keep simple for dynamic or cast
        }
        // Better FA approach for structure:
        // result.Should().BeAssignableTo<IResponseStream>(); 
        // But the test structure handles both.
        // Let's rely on type check assertion.
        result.Should().BeAssignableTo<IResponseStream>();
    }

    [Fact]
    public async Task Subscription_Auth_FailsForNonOwner()
    {
        // 1. Setup Data
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(clientA).WithName("Secret Board").BuildAsync();
        var boardId = board.BoardId;

        // 2. Execute as User B
        var executor = await Factory.Services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
        
        var userBEmail = $"attacker_{Guid.NewGuid()}@example.com"; 
        // We need to ensure User B actually exists in the DB if the logic checks it, or at least construct a principal.
        // The original test just faked the email in the principal. 
        // We'll mimic the original behavior but using the helper.
        
        var request = OperationRequestBuilder.New()
            .SetDocument($@"subscription {{ onCardMoved(boardId: ""{boardId}"") {{ id }} }}")
            .AddGlobalState("ClaimsPrincipal", CreateClaimsPrincipal(userBEmail)) 
            .Build();

        var result = await executor.ExecuteAsync(request);

        // 3. Assert
        result.Should().NotBeAssignableTo<IResponseStream>("Expected an error result (Access Denied), but got a successful stream.");

        if (result is not IResponseStream)
        {
             dynamic dynamicResult = result;
             // Assert.NotNull(dynamicResult.Errors);
             // dynamicResult.Errors.Should().NotBeNull();
             
             bool accessDeniedFound = false;
             var errorList = new List<string>();
             foreach (var error in dynamicResult.Errors)
             {
                 string msg = error.Message;
                 errorList.Add(msg);
                 if (msg == "Access denied" || msg.Contains("not found"))
                 {
                     accessDeniedFound = true;
                     break;
                 }
             }
             accessDeniedFound.Should().BeTrue($"Expected 'Access denied' or 'not found' error. Found: {string.Join(", ", errorList)}");
        }
    }
    
    // Helper to mock ClaimsPrincipal since we are bypassing the HTTP Middleware that normally creates it
    private System.Security.Claims.ClaimsPrincipal CreateClaimsPrincipal(string email)
    {
        // We need to fetch the User ID from the DB to be accurate, 
        // as the resolver checks OwnerId (which is a Guid/String from DB).
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = context.Users.FirstOrDefault(u => u.Email == email);
        
        var userId = user?.Id ?? Guid.NewGuid().ToString();

        var claims = new[]
        {
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId),
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, email)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}
