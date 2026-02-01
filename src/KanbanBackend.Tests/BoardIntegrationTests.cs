using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes; 
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using KanbanBackend.API.Models; 

namespace KanbanBackend.Tests;

public class BoardIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BoardIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Auth:JwtSecret", "SuperSecretKeyForTesting1234567890!@" }, 
                    { "ConnectionStrings:DefaultConnection", "DataSource=:memory:" }
                });
            });
        });
    }

    private async Task<(HttpClient Client, string UserId)> CreateAuthenticatedClientAsync()
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
        
        return (authClient, token);
    }

    [Fact]
    public async Task CreateBoard_ReturnsCorrectData_And_SetsOwner()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();

        // Act
        var mutation = new
        {
            query = @"
                mutation {
                    addBoard(input: { name: ""My Board"" }) {
                        id
                        name
                        ownerId
                    }
                }"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("errors", body.ToLower());
        Assert.Contains("My Board", body);
        Assert.Contains("ownerId", body);
    }

    [Fact]
    public async Task GetBoards_UsesPagination_And_ReturnsItems()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        
        // Create a board first
        await client.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""Paginated Board"" }) { id } }" });

        // Act - Query with Pagination Structure
        var query = new
        {
            query = @"
                query {
                    boards(take: 10) {
                        items {
                            name
                        }
                        totalCount
                        pageInfo {
                            hasNextPage
                        }
                    }
                }"
        };

        var response = await client.PostAsJsonAsync("/graphql", query);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("errors", body.ToLower());
        Assert.Contains("items", body); 
        Assert.Contains("Paginated Board", body);
        Assert.Contains("totalCount", body);
    }

    [Fact]
    public async Task MultiTenancy_UsersCannotSeeOthersBoards()
    {
        // Arrange
        var (clientA, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _) = await CreateAuthenticatedClientAsync();

        // User A creates a board
        await clientA.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""User A Board"" }) { id } }" });

        // Act - User B queries boards
        var query = new
        {
            query = @"
                query {
                    boards {
                        items {
                            name
                        }
                        totalCount
                    }
                }"
        };

        var responseB = await clientB.PostAsJsonAsync("/graphql", query);
        var bodyB = await responseB.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("errors", bodyB.ToLower());
        Assert.DoesNotContain("User A Board", bodyB); 
        Assert.Contains("\"totalCount\":0", bodyB); 
    }
}
