using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes; 
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using KanbanBackend.API.Models; 

namespace KanbanBackend.Tests;

public class MoveCardIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbFileName;

    public MoveCardIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _dbFileName = $"test_movecard_{Guid.NewGuid()}.db";
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

    private async Task<(string BoardId, string ColumnId, string CardId)> SetupBoardWithCard(HttpClient client)
    {
         // 1. Create Board
        var createBoardRes = await client.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""Board1"" }) { id } }" });
        createBoardRes.EnsureSuccessStatusCode();
        var bodyBoard = await createBoardRes.Content.ReadAsStringAsync();
        var boardId = JsonNode.Parse(bodyBoard)?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        // 2. Create Column
        var createColRes = await client.PostAsJsonAsync("/graphql", new { 
            query = $@"mutation {{ addColumn(input: {{ boardId: ""{boardId}"", name: ""Col1"", order: 0 }}) {{ id }} }}" 
        });
        var bodyCol = await createColRes.Content.ReadAsStringAsync();
        var columnId = JsonNode.Parse(bodyCol)?["data"]?["addColumn"]?["id"]?.GetValue<string>();

        // 3. Create Card
        var createCardRes = await client.PostAsJsonAsync("/graphql", new {
            query = $@"mutation {{ addCard(input: {{ columnId: ""{columnId}"", name: ""Card1"", rank: 100 }}) {{ id }} }}"
        });
        var bodyCard = await createCardRes.Content.ReadAsStringAsync();
        var cardId = JsonNode.Parse(bodyCard)?["data"]?["addCard"]?["id"]?.GetValue<string>();

        return (boardId!, columnId!, cardId!);
    }

    [Fact]
    public async Task MoveCard_Success_UpdatesColumnAndRank()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var (boardId, col1Id, cardId) = await SetupBoardWithCard(client);

        // Create a second column
        var createCol2Res = await client.PostAsJsonAsync("/graphql", new { 
            query = $@"mutation {{ addColumn(input: {{ boardId: ""{boardId}"", name: ""Col2"", order: 1 }}) {{ id }} }}" 
        });
        var col2Id = JsonNode.Parse(await createCol2Res.Content.ReadAsStringAsync())?["data"]?["addColumn"]?["id"]?.GetValue<string>();

        // Act - Move Card to Col2 with new rank
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{col2Id}"", rank: 500 }}) {{
                        id
                        rank
                        columnId
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("errors", body.ToLower());
        Assert.Contains(col2Id, body);
        Assert.Contains("500", body);
    }

    [Fact]
    public async Task MoveCard_Validation_NegativeRank_ReturnsError()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var (boardId, colId, cardId) = await SetupBoardWithCard(client);

        // Act
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{colId}"", rank: -1 }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.Contains("VALIDATION_ERROR", body);
    }

    [Fact]
    public async Task MoveCard_NotFound_WhenCardDoesNotExist()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var (_, colId, _) = await SetupBoardWithCard(client);
        var randomCardId = Guid.NewGuid();

        // Act
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{randomCardId}"", columnId: ""{colId}"", rank: 1 }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.Contains("NOT_FOUND", body); // Or EntityNotFoundException code
    }

    [Fact]
    public async Task MoveCard_OwnershipCheck_ReturnsNotFound_ForOtherUsersCard()
    {
        // Arrange
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();
        
        var (boardId, colId, cardId) = await SetupBoardWithCard(clientA);

        // Act - Client B tries to move Client A's card
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{colId}"", rank: 1 }}) {{
                        id
                    }}
                }}"
        };

        var response = await clientB.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.Contains("NOT_FOUND", body); // Assuming strict ownership check hides existence
    }

    [Fact]
    public async Task MoveCard_SiloCheck_PreventsMovingToDifferentBoard()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
        // Board 1 Setup
        var (board1Id, col1Id, cardId) = await SetupBoardWithCard(client);

        // Board 2 Setup
        var createBoard2Res = await client.PostAsJsonAsync("/graphql", new { query = @"mutation { addBoard(input: { name: ""Board2"" }) { id } }" });
        var board2Id = JsonNode.Parse(await createBoard2Res.Content.ReadAsStringAsync())?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        var createCol2Res = await client.PostAsJsonAsync("/graphql", new { 
            query = $@"mutation {{ addColumn(input: {{ boardId: ""{board2Id}"", name: ""Col2"", order: 0 }}) {{ id }} }}" 
        });
        var col2Id = JsonNode.Parse(await createCol2Res.Content.ReadAsStringAsync())?["data"]?["addColumn"]?["id"]?.GetValue<string>();

        // Act - Move Card from Board 1 (col1) to Board 2 (col2)
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{col2Id}"", rank: 1 }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("errors", body.ToLower());
        Assert.Contains("Cannot move card to a column on a different board", body);
    }
}
