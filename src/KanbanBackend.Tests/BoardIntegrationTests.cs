using FluentAssertions;
using KanbanBackend.Tests.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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
        body.ToLower().Should().NotContain("errors");
        body.Should().Contain("My Board");
        body.Should().Contain("ownerId");
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
        body.ToLower().Should().NotContain("errors");
        body.Should().Contain("items"); 
        body.Should().Contain("Paginated Board");
        body.Should().Contain("totalCount");
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
        bodyB.ToLower().Should().NotContain("errors");
        bodyB.Should().NotContain("User A Board"); 
        bodyB.Should().Contain("\"totalCount\":0"); 
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
            query = $@"mutation {{ deleteAccount(password: ""{TestConstants.DefaultPassword}"") }}"
        };
        var deleteRes = await client.PostAsJsonAsync("/graphql", deleteMutation);
        var deleteBody = await deleteRes.Content.ReadAsStringAsync();
        deleteBody.ToLower().Should().NotContain("errors");
        deleteBody.ToLower().Should().Contain("true");

        // 3. Verify Login Fails
        var loginRes = await Factory.CreateClient().PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ login(email: ""{email}"", password: ""{TestConstants.DefaultPassword}"") {{ accessToken }} }}"
        });
        var loginBody = await loginRes.Content.ReadAsStringAsync();
        loginBody.Should().Contain("AUTH_FAILED");
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
        body.ToLower().Should().Contain("errors");
        body.Should().NotContain("Hacked Column");
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
        body.ToLower().Should().Contain("errors");
        body.Should().NotContain("Hacked Card");
    }
}
