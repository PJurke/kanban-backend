using System.Net.Http.Json;
using FluentAssertions;
using KanbanBackend.API.Data;
using KanbanBackend.Tests.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KanbanBackend.Tests.Features;

public class WipLimitIntegrationTests : IntegrationTestBase
{
    public WipLimitIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task Should_Update_Column_WipLimit()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("WIP Board")
            .WithColumn("To Do")
            .BuildAsync();
        
        var columnIdStr = board.ColumnIds["To Do"];

        // Act
        var mutation = new
        {
            query = $$"""
                mutation {
                    updateColumn(input: {
                        id: "{{columnIdStr}}",
                        wipLimit: 5
                    }) {
                        id
                        wipLimit
                    }
                }
            """
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        body.Should().NotContain("errors");
        body.Should().Contain("\"wipLimit\":5");

        // Verify DB
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbColumn = await db.Columns.FindAsync(Guid.Parse(columnIdStr));
        dbColumn!.WipLimit.Should().Be(5);
    }

    [Fact]
    public async Task Should_Clear_Column_WipLimit()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("WIP Board 2")
            .WithColumn("Doing")
            .BuildAsync();
            
        var columnIdStr = board.ColumnIds["Doing"];
        
        // Set initial limit via DB to avoid dependency on previous test
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Columns.FindAsync(Guid.Parse(columnIdStr));
            c!.WipLimit = 3;
            await db.SaveChangesAsync();
        }

        // Act
        var mutation = new
        {
            query = $$"""
                mutation {
                    updateColumn(input: {
                        id: "{{columnIdStr}}",
                        wipLimit: null
                    }) {
                        id
                        wipLimit
                    }
                }
            """
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        body.Should().NotContain("errors");
        body.Should().Contain("\"wipLimit\":null");

        // Verify DB
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbColumn = await db.Columns.FindAsync(Guid.Parse(columnIdStr));
            dbColumn!.WipLimit.Should().BeNull();
        }
    }
    
    [Fact]
    public async Task Should_Validate_Negative_WipLimit()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("WIP Board 3")
            .WithColumn("Done")
            .BuildAsync();
        
        var columnId = board.ColumnIds["Done"];

        // Act
        var mutation = new
        {
            query = $$"""
                mutation {
                    updateColumn(input: {
                        id: "{{columnId}}",
                        wipLimit: -1
                    }) {
                        id
                    }
                }
            """
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.Should().Contain("errors");
        body.Should().Contain("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Soft_Limit_Should_Not_Block_MoveCard()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var board = await new BoardBuilder(client)
            .WithName("Soft Limit Board")
            .WithColumn("Source")
                .WithCard("CardToMove")
            .WithColumn("Target")
                .WithCard("ExistingCard")
            .BuildAsync();

        var sourceColumnId = board.ColumnIds["Source"];
        var targetColumnId = board.ColumnIds["Target"];
        var cardId = board.CardIds["CardToMove"];

        // Set Limit to 1 on Target
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Columns.FindAsync(Guid.Parse(targetColumnId));
            c!.WipLimit = 1; // Limit is 1, and it already has "ExistingCard"
            await db.SaveChangesAsync();
        }

        // Act: Move card to target column (Limit is 1, will become 2 -> Over Limit)
        var mutation = new
        {
            query = $$"""
                mutation {
                    moveCard(input: {
                        cardId: "{{cardId}}",
                        columnId: "{{targetColumnId}}",
                        rank: 2500
                    }) {
                        id
                        columnId
                    }
                }
            """
        };

        var response = await client.PostAsJsonAsync("/graphql", mutation);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        body.Should().NotContain("errors");
        
        // Verify Card is in target column
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbCard = await db.Cards.FindAsync(Guid.Parse(cardId));
            dbCard!.ColumnId.ToString().Should().Be(targetColumnId);
        }
    }
}
