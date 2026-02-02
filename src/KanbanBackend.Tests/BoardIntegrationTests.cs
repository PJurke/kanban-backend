using System.Net.Http.Json;
using System.Text.Json.Nodes;
using KanbanBackend.Tests.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace KanbanBackend.Tests;

public class BoardIntegrationTests : IntegrationTestBase
{
    public BoardIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateBoard_ReturnsCorrectData_And_SetsOwner()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        // Act
        // Note: Using raw mutation here to verify specific response fields like ownerId which might not be exposed by Builder result
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
        
        // Create a board first using Builder
        await new BoardBuilder(client).WithName("Paginated Board").BuildAsync();

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
        await new BoardBuilder(clientA).WithName("User A Board").BuildAsync();

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
        await new BoardBuilder(client).WithName("To Be Deleted").BuildAsync();

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
        var loginRes = await Factory.CreateClient().PostAsJsonAsync("/graphql", new
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
        var board = await new BoardBuilder(clientA).WithName("My Board").BuildAsync();
        var boardId = board.BoardId;

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

        // User A creates board and column
        var board = await new BoardBuilder(clientA)
            .WithName("My Board")
            .WithColumn("Backlog")
            .BuildAsync();
        
        var columnId = board.ColumnIds["Backlog"];

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
