using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes; 
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using HotChocolate;
using HotChocolate.Execution;
using KanbanBackend.API.Models; 

namespace KanbanBackend.Tests;

public class SubscriptionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbFileName;

    public SubscriptionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _dbFileName = $"test_sub_{Guid.NewGuid()}.db";
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Auth:JwtSecret", "SuperSecretKeyForTesting1234567890!@" }, 
                    { "ConnectionStrings:DefaultConnection", $"Data Source={_dbFileName}" } 
                });
            });
        });
    }

    public void Dispose()
    {
        if (File.Exists(_dbFileName))
        {
            try
            {
                File.Delete(_dbFileName);
            }
            catch
            {
                // Ignored
            }
        }
    }

    private async Task<(HttpClient Client, string UserId, string Email)> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"user_{Guid.NewGuid()}@example.com";
        var password = "Password123!";

        // Register
        await client.PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ register(email: ""{email}"", password: ""{password}"") {{ email }} }}"
        });

        // Login
        var loginRes = await client.PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ login(email: ""{email}"", password: ""{password}"") {{ accessToken user {{ email }} }} }}"
        });
        
        var body = await loginRes.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        var token = json?["data"]?["login"]?["accessToken"]?.GetValue<string>();
        
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception($"Failed to extract token. Body: {body}");
        }
        
        // Create authenticated client
        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        return (authClient, token, email);
    }

    [Fact]
    public async Task Subscription_Auth_SucceedsForOwner()
    {
        // 1. Setup Data via HTTP
        var (client, _, email) = await CreateAuthenticatedClientAsync();
        
        // Fix: Removed quotes around 'name' to match working BoardIntegrationTests syntax
        var createBoardRes = await client.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""My Sub Board"" }) { id } }" });
        createBoardRes.EnsureSuccessStatusCode();
        var boardBody = await createBoardRes.Content.ReadAsStringAsync();
        var boardId = JsonNode.Parse(boardBody)?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        // 2. Execute Subscription directly against Schema
        var executor = await _factory.Services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument($@"subscription {{ onCardMoved(boardId: ""{boardId}"") {{ id }} }}")
            .AddGlobalState("ClaimsPrincipal", CreateClaimsPrincipal(email)) 
            .Build();

        var result = await executor.ExecuteAsync(request);

        // 3. Assert
        if (result is IResponseStream)
        {
             Assert.True(true); 
        }
        else
        {
             dynamic dynamicResult = result;
             var errors = (IEnumerable<object>)dynamicResult.Errors; // Cast to enumerable to print
             var errorMsg = errors != null ? string.Join(", ", errors) : "No errors";
             Assert.Null(dynamicResult.Errors); // Fail with msg if needed, but Null check is standard
        }
    }

    [Fact]
    public async Task Subscription_Auth_FailsForNonOwner()
    {
        // 1. Setup Data
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var createBoardRes = await clientA.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""Secret Board"" }) { id } }" });
        var boardId = JsonNode.Parse(await createBoardRes.Content.ReadAsStringAsync())?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        // 2. Execute as User B
        var executor = await _factory.Services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
        
        var userBEmail = $"attacker_{Guid.NewGuid()}@example.com"; 

        var request = OperationRequestBuilder.New()
            .SetDocument($@"subscription {{ onCardMoved(boardId: ""{boardId}"") {{ id }} }}")
            .AddGlobalState("ClaimsPrincipal", CreateClaimsPrincipal(userBEmail)) 
            .Build();

        var result = await executor.ExecuteAsync(request);

        // 3. Assert
        if (result is IResponseStream)
        {
             Assert.Fail("Expected an error result (Access Denied), but got a successful stream.");
        }
        else
        {
             dynamic dynamicResult = result;
             Assert.NotNull(dynamicResult.Errors);
             
             bool accessDeniedFound = false;
             var errorList = new List<string>();
             foreach (var error in dynamicResult.Errors)
             {
                 string msg = error.Message;
                 errorList.Add(msg);
                 if (msg == "Access denied")
                 {
                     accessDeniedFound = true;
                     break;
                 }
             }
             Assert.True(accessDeniedFound, $"Expected 'Access denied' error not found. Found: {string.Join(", ", errorList)}");
        }
    }
    
    // Helper to mock ClaimsPrincipal since we are bypassing the HTTP Middleware that normally creates it
    private System.Security.Claims.ClaimsPrincipal CreateClaimsPrincipal(string email)
    {
        // We need to fetch the User ID from the DB to be accurate, 
        // as the resolver checks OwnerId (which is a Guid/String from DB).
        // For simplicity in this test refactor, let's fetch the user first.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<KanbanBackend.API.Data.AppDbContext>();
        var user = context.Users.FirstOrDefault(u => u.Email == email);
        
        // If user doesn't exist (User B case above), we might need to create them or just fallback.
        // Actually, for "Subscription_Auth_FailsForNonOwner", we constructed a fake email. 
        // We should register User B properly to have a valid ID if the logic depends on it.
        // Let's rely on the factory helper to get real IDs or just use the sub claim.
        
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
