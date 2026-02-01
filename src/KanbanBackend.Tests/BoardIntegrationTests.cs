using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes; 
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using KanbanBackend.API.Models; 

namespace KanbanBackend.Tests;

public class BoardIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbFileName;

    public BoardIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _dbFileName = $"test_{Guid.NewGuid()}.db";
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
                // Ignored to prevent test failures during cleanup
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
    public async Task CreateBoard_ReturnsCorrectData_And_SetsOwner()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();

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
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
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
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();

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

    [Fact]
    public async Task DeleteAccount_RemovesUserAndData()
    {
        // 1. Setup User and Data
        var (client, _, email) = await CreateAuthenticatedClientAsync();
        // Create a board
        await client.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""To Be Deleted"" }) { id } }" });

        // 2. Call Delete Account
        var deleteMutation = new
        {
            query = @"mutation { deleteAccount(password: ""Password123!"") }"
        };
        var deleteRes = await client.PostAsJsonAsync("/graphql", deleteMutation);
        var deleteBody = await deleteRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("errors", deleteBody.ToLower());
        Assert.Contains("true", deleteBody.ToLower());

        // 3. Verify Login Fails
        var loginRes = await _factory.CreateClient().PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ login(email: ""{email}"", password: ""Password123!"") {{ accessToken }} }}"
        });
        var loginBody = await loginRes.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_FAILED", loginBody); // Should fail
    }

    [Fact]
    public async Task AddColumn_FailsForNonOwner_ReturnsEntityNotFound()
    {
        // Assemble
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();

        // User A creates board
        var createBoardRes = await clientA.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""My Board"" }) { id } }" });
        createBoardRes.EnsureSuccessStatusCode();
        var bodyA = await createBoardRes.Content.ReadAsStringAsync();
        var jsonA = JsonNode.Parse(bodyA);
        var boardId = jsonA?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        // Act - User B tries to add column
        var mutation = new
        {
            query = $@"
                mutation {{
                    addColumn(input: {{ boardId: ""{boardId}"", name: ""Hacked Column"", order: 0 }}) {{
                        id
                    }}
                }}"
        };
        var response = await clientB.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.DoesNotContain("Hacked Column", body);
    }

    [Fact]
    public async Task AddCard_FailsForNonOwner_ReturnsEntityNotFound()
    {
        // Assemble
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();

        // User A creates board
        var createBoardRes = await clientA.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""My Board"" }) { id } }" });
        var bodyA = await createBoardRes.Content.ReadAsStringAsync();
        var jsonA = JsonNode.Parse(bodyA);
        var boardId = jsonA?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        // User A creates column
        var createColRes = await clientA.PostAsJsonAsync("/graphql", new { 
            query = $@"mutation {{ addColumn(input: {{ boardId: ""{boardId}"", name: ""Backlog"", order: 0 }}) {{ id }} }}" 
        });
        var bodyCol = await createColRes.Content.ReadAsStringAsync();
        var jsonCol = JsonNode.Parse(bodyCol);
        var columnId = jsonCol?["data"]?["addColumn"]?["id"]?.GetValue<string>();

        // Act - User B tries to add card
        var mutation = new
        {
            query = $@"
                mutation {{
                    addCard(input: {{ columnId: ""{columnId}"", name: ""Hacked Card"", rank: ""0|h:"" }}) {{
                        id
                    }}
                }}"
        };
        var response = await clientB.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.DoesNotContain("Hacked Card", body);
    }
}
