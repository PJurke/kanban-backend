using FluentAssertions;
using KanbanBackend.API.Data;
using KanbanBackend.Tests.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace KanbanBackend.Tests;

public class MoveCardIntegrationTests : IntegrationTestBase
{

    public MoveCardIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task MoveCard_Success_UpdatesColumnAndRank()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
        // Setup Board with Col1 containing Card1, and Col2 empty
        var board = await new BoardBuilder(client)
            .WithName("Board1")
            .WithColumn("Col1")
                .WithCard("Card1")
            .WithColumn("Col2")
            .BuildAsync();

        var cardId = board.CardIds["Card1"];
        var col2Id = board.ColumnIds["Col2"];
        var rowVersion = await GetRowVersionFromDb(cardId);

        // Act - Move Card to Col2 with new rank
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{col2Id}"", rank: 500, rowVersion: ""{rowVersion}"" }}) {{
                        id
                        rank
                        columnId
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ToLower().Should().NotContain("errors");
        body.Should().Contain(col2Id);
        body.Should().Contain("500");
    }

    [Fact]
    public async Task MoveCard_Validation_NegativeRank_ReturnsError()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("Board1")
            .WithColumn("Col1")
                .WithCard("Card1")
            .BuildAsync();

        var cardId = board.CardIds["Card1"];
        var colId = board.ColumnIds["Col1"];

        // Act
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{colId}"", rank: -1, rowVersion: ""MQ=="" }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ToLower().Should().Contain("errors");
        body.Should().Contain("VALIDATION_ERROR");
    }

    [Fact]
    public async Task MoveCard_NotFound_WhenCardDoesNotExist()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("Board1")
            .WithColumn("Col1")
            .BuildAsync();
        
        var colId = board.ColumnIds["Col1"];
        var randomCardId = Guid.NewGuid();

        // Act
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{randomCardId}"", columnId: ""{colId}"", rank: 1, rowVersion: ""MQ=="" }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ToLower().Should().Contain("errors");
        body.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task MoveCard_OwnershipCheck_ReturnsNotFound_ForOtherUsersCard()
    {
        // Arrange
        var (clientA, _, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();
        
        var board = await new BoardBuilder(clientA)
            .WithName("Board1")
            .WithColumn("Col1")
                .WithCard("Card1")
            .BuildAsync();

        var cardId = board.CardIds["Card1"];
        var colId = board.ColumnIds["Col1"];

        // Act - Client B tries to move Client A's card
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{colId}"", rank: 1, rowVersion: ""MQ=="" }}) {{
                        id
                    }}
                }}"
        };

        var response = await clientB.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ToLower().Should().Contain("errors");
        body.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task MoveCard_SiloCheck_PreventsMovingToDifferentBoard()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
        // Board 1 Setup
        var board1 = await new BoardBuilder(client)
            .WithName("Board1")
            .WithColumn("Col1")
                .WithCard("Card1")
            .BuildAsync();
        var cardId = board1.CardIds["Card1"];

        // Board 2 Setup
        var board2 = await new BoardBuilder(client)
            .WithName("Board2")
            .WithColumn("Col2")
            .BuildAsync();
        var col2Id = board2.ColumnIds["Col2"];
        var rowVersion = await GetRowVersionFromDb(cardId);

        // Act - Move Card from Board 1 (col1) to Board 2 (col2)
        var mutation = new
        {
            query = $@"
                mutation {{
                    moveCard(input: {{ cardId: ""{cardId}"", columnId: ""{col2Id}"", rank: 1, rowVersion: ""{rowVersion}"" }}) {{
                        id
                    }}
                }}"
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ToLower().Should().Contain("errors");
        body.Should().Contain("Cannot move card to a column on a different board");
    }

    private async Task<string> GetRowVersionFromDb(string cardIdStr)
    {
        var cardId = Guid.Parse(cardIdStr);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.FindAsync(cardId);
        return Convert.ToBase64String(BitConverter.GetBytes(card!.RowVersion));
    }
}
