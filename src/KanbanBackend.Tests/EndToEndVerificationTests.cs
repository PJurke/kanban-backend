using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace KanbanBackend.Tests;

public class EndToEndVerificationTests : IntegrationTestBase
{
    public EndToEndVerificationTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task VerifyCompleteUserFlow_PostgresMigration()
    {
        // 1. Register & Login
        var (client, token, email) = await CreateAuthenticatedClientAsync();

        // 2. Create Board
        var createBoardQuery = @"
            mutation {
                addBoard(input: { name: ""Migration Test Board"" }) {
                    id
                    name
                }
            }";

        var boardRes = await client.PostAsJsonAsync("/graphql", new { query = createBoardQuery });
        if (!boardRes.IsSuccessStatusCode)
        {
            var errorContent = await boardRes.Content.ReadAsStringAsync();
            throw new Exception($"CreateBoard Failed. Status: {boardRes.StatusCode}, Body: {errorContent}");
        }
        boardRes.EnsureSuccessStatusCode();
        var boardJson = JsonNode.Parse(await boardRes.Content.ReadAsStringAsync());
        var boardId = boardJson?["data"]?["addBoard"]?["id"]?.GetValue<string>();
        boardId.Should().NotBeNullOrEmpty();

        // 3. Create Column
        var createColumnQuery = $@"
            mutation {{
                addColumn(input: {{ boardId: ""{boardId}"", name: ""To Do"", order: 0 }}) {{
                    id
                    name
                }}
            }}";
        
        var colRes = await client.PostAsJsonAsync("/graphql", new { query = createColumnQuery });
        if (!colRes.IsSuccessStatusCode)
        {
            var errorContent = await colRes.Content.ReadAsStringAsync();
            throw new Exception($"AddColumn Failed. Status: {colRes.StatusCode}, Body: {errorContent}");
        }
        colRes.EnsureSuccessStatusCode();
        var colJson = JsonNode.Parse(await colRes.Content.ReadAsStringAsync());
        var columnId = colJson?["data"]?["addColumn"]?["id"]?.GetValue<string>();
        columnId.Should().NotBeNullOrEmpty();

        // 4. Create Card
        var createCardQuery = $@"
            mutation {{
                addCard(input: {{ columnId: ""{columnId}"", name: ""Test Postgres Card"", rank: 1000 }}) {{
                    id
                    name
                    rowVersion
                }}
            }}";

        var cardRes = await client.PostAsJsonAsync("/graphql", new { query = createCardQuery });
        if (!cardRes.IsSuccessStatusCode)
        {
            var errorContent = await cardRes.Content.ReadAsStringAsync();
            throw new Exception($"AddCard Failed. Status: {cardRes.StatusCode}, Body: {errorContent}");
        }
        cardRes.EnsureSuccessStatusCode();
        var cardJson = JsonNode.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardJson?["data"]?["addCard"]?["id"]?.GetValue<string>();
        var rowVersion = cardJson?["data"]?["addCard"]?["rowVersion"]?.GetValue<string>(); // Should be string token now

        cardId.Should().NotBeNullOrEmpty();
        rowVersion.Should().NotBeNullOrEmpty("RowVersionToken should be returned as a string from GraphQL");

        // 5. Query Board to verify hierarchy
        var getBoardQuery = $@"
            query {{
                boards(where: {{ id: {{ eq: ""{boardId}"" }} }}) {{
                    items {{
                        columns {{
                            id
                            cards {{
                                id
                                name
                            }}
                        }}
                    }}
                }}
            }}";

        var queryRes = await client.PostAsJsonAsync("/graphql", new { query = getBoardQuery });
        if (!queryRes.IsSuccessStatusCode)
        {
             var errorContent = await queryRes.Content.ReadAsStringAsync();
             throw new Exception($"QueryBoard Failed. Status: {queryRes.StatusCode}, Body: {errorContent}");
        }
        var queryJson = JsonNode.Parse(await queryRes.Content.ReadAsStringAsync());
        var cards = queryJson?["data"]?["boards"]?["items"]?[0]?["columns"]?[0]?["cards"]?.AsArray();
        cards.Should().HaveCount(1);
        cards![0]?["name"]?.GetValue<string>().Should().Be("Test Postgres Card");

        // 6. Move Card (Testing Concurrency/RowVersion)
        // Create another column to move to
        var createCol2Query = $@"
            mutation {{
                addColumn(input: {{ boardId: ""{boardId}"", name: ""Done"", order: 1 }}) {{
                    id
                }}
            }}";
        var col2Res = await client.PostAsJsonAsync("/graphql", new { query = createCol2Query });
        var col2Id = JsonNode.Parse(await col2Res.Content.ReadAsStringAsync())?["data"]?["addColumn"]?["id"]?.GetValue<string>();

        var moveCardQuery = $@"
            mutation {{
                moveCard(input: {{ 
                    cardId: ""{cardId}"", 
                    columnId: ""{col2Id}"", 
                    rank: 2000, 
                    rowVersion: ""{rowVersion}"" 
                }}) {{
                    id
                    columnId
                    rowVersion
                }}
            }}";

        var moveRes = await client.PostAsJsonAsync("/graphql", new { query = moveCardQuery });
        if (!moveRes.IsSuccessStatusCode)
        {
             var errorContent = await moveRes.Content.ReadAsStringAsync();
             throw new Exception($"MoveCard Failed. Status: {moveRes.StatusCode}, Body: {errorContent}");
        }
        moveRes.EnsureSuccessStatusCode();
        var moveJson = JsonNode.Parse(await moveRes.Content.ReadAsStringAsync());
        
        // Assert Move and New RowVersion
        moveJson?["errors"].Should().BeNull();
        moveJson?["data"]?["moveCard"]?["columnId"]?.GetValue<string>().Should().Be(col2Id);
        var newRowVersion = moveJson?["data"]?["moveCard"]?["rowVersion"]?.GetValue<string>();
        newRowVersion.Should().NotBe(rowVersion);
    }
}
