using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;

namespace KanbanBackend.Tests;

public class ConcurrencyIntegrationTests : IntegrationTestBase
{
    public ConcurrencyIntegrationTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task MoveCard_ShouldFail_WhenRowVersionMissing()
    {
        // Arrange
        var (client, userId, _) = await CreateAuthenticatedClientAsync();
        var boardId = await CreateBoardAsync(client, "Board1");
        var columnId = await CreateColumnAsync(client, boardId, "Col1");
        var cardId = await CreateCardAsync(client, columnId, "Card1");

        // Act
        var query = $@"
            mutation {{
                moveCard(input: {{
                    cardId: ""{cardId}"",
                    columnId: ""{columnId}"",
                    rank: 100,
                    rowVersion: """" 
                }}) {{
                    id
                }}
            }}";
        
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.Should().Contain("PRECONDITION_REQUIRED");
        body.Should().Contain("RowVersion is required");
    }

    [Fact]
    public async Task MoveCard_ShouldFail_WhenRowVersionInvalidBase64()
    {
        // Arrange
        var (client, userId, _) = await CreateAuthenticatedClientAsync();
        var boardId = await CreateBoardAsync(client, "Board1");
        var columnId = await CreateColumnAsync(client, boardId, "Col1");
        var cardId = await CreateCardAsync(client, columnId, "Card1");

        // Act
        var query = $@"
            mutation {{
                moveCard(input: {{
                    cardId: ""{cardId}"",
                    columnId: ""{columnId}"",
                    rank: 100,
                    rowVersion: ""NOT_BASE64"" 
                }}) {{
                    id
                }}
            }}";
        
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.Should().Contain("PRECONDITION_REQUIRED");
        body.Should().Contain("Invalid RowVersion");
    }

    [Fact]
    public async Task MoveCard_ShouldFail_WhenRowVersionMismatch()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
        // Setup Data (Board, Column, Card)
        var boardId = await CreateBoardAsync(client, "Concurrency Board");
        var columnId = await CreateColumnAsync(client, boardId, "Col 1");
        var cardId = await CreateCardAsync(client, columnId, "Concurrent Card");

        // Fetch current RowVersion manually from DB to simulate "stale" client
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.FindAsync(cardId);
        var oldVersion = Convert.ToBase64String(BitConverter.GetBytes(card!.RowVersion));

        // Simulate another user updating the card (e.g. changing name or moving it)
        // We do this by sneaking into the DB and updating it, which changes RowVersion
        card.Name = "Updated Name";
        await db.SaveChangesAsync();

        // Act: Try to move with OLD RowVersion
        var query = $@"
            mutation {{
                moveCard(input: {{
                    cardId: ""{cardId}"",
                    columnId: ""{columnId}"",
                    rank: 100,
                    rowVersion: ""{oldVersion}""
                }}) {{
                    id
                }}
            }}";

        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        responseBody.Should().Contain("CONFLICT");
        responseBody.Should().Contain("Card was modified by another operation");
    }

    [Fact]
    public async Task MoveCard_ShouldTriggerRebalancing_WhenRanksAreTooClose()
    {
         // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        
        var boardId = await CreateBoardAsync(client, "Rebalance Board");
        var columnId = await CreateColumnAsync(client, boardId, "Col 1");
        
        // Create 2 cards with ranks very close together
        // We use backend seeding to force this state
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Note: We need a fresh content tracking for integration context
            // But we can add via DB directly
            var c1 = new Card { Id = Guid.NewGuid(), ColumnId = columnId, Name = "C1", Rank = 1000 };
            var c2 = new Card { Id = Guid.NewGuid(), ColumnId = columnId, Name = "C2", Rank = 1000.00001 }; 
            
            db.Cards.AddRange(c1, c2);
            await db.SaveChangesAsync();
        }

        // Create a 3rd card via API to trigger the move logic
        var card3Id = await CreateCardAsync(client, columnId, "C3");
        
        // Fetch Version
        string card3Version;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Cards.FindAsync(card3Id);
            card3Version = Convert.ToBase64String(BitConverter.GetBytes(c!.RowVersion));
        }

        // Act: Move C3 to exactly the same rank as C1 (Rank collision)
        var query = $@"
            mutation {{
                moveCard(input: {{
                    cardId: ""{card3Id}"",
                    columnId: ""{columnId}"",
                    rank: 1000,
                    rowVersion: ""{card3Version}""
                }}) {{
                    id
                }}
            }}";
        
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.EnsureSuccessStatusCode();

        // Assert: Check DB if rebalancing happened
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cards = await db.Cards.Where(c => c.ColumnId == columnId).OrderBy(c => c.Rank).ToListAsync();

            cards.Should().HaveCount(3);
            
            // Rebalancing should enforce large steps like 1000, 2000, 3000
            // Since we had collision, order is determined by secondary sort (CreatedAt/Id).
            // Expect normalized ranks.
            cards[0].Rank.Should().Be(1000);
            cards[1].Rank.Should().Be(2000);
            cards[2].Rank.Should().Be(3000);
        }
    }

    // Helpers (Should ideally be in IntegrationTestBase or Builder, simplified here for speed)
    private async Task<Guid> CreateBoardAsync(HttpClient client, string name)
    {
        var mut = $@"mutation {{ addBoard(input: {{ name: ""{name}"" }}) {{ id }} }}";
        var res = await client.PostAsJsonAsync("/graphql", new { query = mut });
        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        if (json?["data"]?["addBoard"]?["id"] == null) throw new Exception($"Failed to create board. Errors: {json?["errors"]}");
        return Guid.Parse(json!["data"]!["addBoard"]!["id"]!.GetValue<string>());
    }

    private async Task<Guid> CreateColumnAsync(HttpClient client, Guid boardId, string name)
    {
        var mut = $@"mutation {{ addColumn(input: {{ boardId: ""{boardId}"", name: ""{name}"", order: 0 }}) {{ id }} }}";
        var res = await client.PostAsJsonAsync("/graphql", new { query = mut });
        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        if (json?["data"]?["addColumn"]?["id"] == null) throw new Exception($"Failed to create column. Errors: {json?["errors"]}");
        return Guid.Parse(json!["data"]!["addColumn"]!["id"]!.GetValue<string>());
    }

    private async Task<Guid> CreateCardAsync(HttpClient client, Guid columnId, string name)
    {
        var mut = $@"mutation {{ addCard(input: {{ columnId: ""{columnId}"", name: ""{name}"", rank: 0 }}) {{ id }} }}";
        var res = await client.PostAsJsonAsync("/graphql", new { query = mut });
        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        if (json?["data"]?["addCard"]?["id"] == null) throw new Exception($"Failed to create card. Errors: {json?["errors"]}");
        return Guid.Parse(json!["data"]!["addCard"]!["id"]!.GetValue<string>());
    }
}
