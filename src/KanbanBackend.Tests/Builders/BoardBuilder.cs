using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace KanbanBackend.Tests.Builders;

public class BoardBuilder
{
    private readonly HttpClient _client;
    private string _name = "Test Board";
    private readonly List<ColumnBuilderData> _columns = new();

    public BoardBuilder(HttpClient client)
    {
        _client = client;
    }

    public BoardBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public BoardBuilder WithColumn(string name)
    {
        _columns.Add(new ColumnBuilderData { Name = name });
        return this;
    }

    public BoardBuilder WithCard(string name, string? description = null)
    {
        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("Cannot add a card without a column. Call WithColumn first.");
        }

        var lastColumn = _columns.Last();
        lastColumn.Cards.Add(new CardBuilderData { Name = name, Description = description });
        return this;
    }

    public async Task<BoardBuildResult> BuildAsync()
    {
        // 1. Create Board
        var createBoardQuery = new
        {
            query = $@"mutation {{ addBoard(input: {{ name: ""{_name}"" }}) {{ id name }} }}"
        };
        var boardRes = await _client.PostAsJsonAsync("/graphql", createBoardQuery);
        boardRes.EnsureSuccessStatusCode();
        var boardBody = await boardRes.Content.ReadAsStringAsync();
        var boardId = JsonNode.Parse(boardBody)?["data"]?["addBoard"]?["id"]?.GetValue<string>();

        if (string.IsNullOrEmpty(boardId))
            throw new Exception("Failed to create board");

        var result = new BoardBuildResult { BoardId = boardId, BoardName = _name };

        // 2. Create Columns and Cards
        int colOrder = 0;
        foreach (var col in _columns)
        {
            var createColQuery = new
            {
                query = $@"mutation {{ addColumn(input: {{ boardId: ""{boardId}"", name: ""{col.Name}"", order: {colOrder++} }}) {{ id }} }}"
            };
            var colRes = await _client.PostAsJsonAsync("/graphql", createColQuery);
            var colBody = await colRes.Content.ReadAsStringAsync();
            
            if (colBody.Contains("errors", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Failed to create column '{col.Name}': {colBody}");

            var colId = JsonNode.Parse(colBody)?["data"]?["addColumn"]?["id"]?.GetValue<string>();

            if (string.IsNullOrEmpty(colId))
                 throw new Exception($"Failed to create column {col.Name} (No ID returned)");
            
            result.ColumnIds.Add(col.Name, colId);

            foreach (var card in col.Cards)
            {
                var createCardQuery = new
                {
                    query = $@"mutation {{ addCard(input: {{ columnId: ""{colId}"", name: ""{card.Name}"", rank: 0 }}) {{ id }} }}"
                };
                var cardRes = await _client.PostAsJsonAsync("/graphql", createCardQuery);
                var cardBody = await cardRes.Content.ReadAsStringAsync();
                
                if (cardBody.Contains("errors", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Failed to create card '{card.Name}': {cardBody}");

                var cardId = JsonNode.Parse(cardBody)?["data"]?["addCard"]?["id"]?.GetValue<string>();
                
                if (string.IsNullOrEmpty(cardId))
                    throw new Exception($"Failed to create card '{card.Name}' (No ID returned)");
                    
                result.CardIds.Add(card.Name, cardId);
            }
        }

        return result;
    }

    private class ColumnBuilderData
    {
        public string Name { get; set; } = string.Empty;
        public List<CardBuilderData> Cards { get; set; } = new();
    }

    private class CardBuilderData
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

public class BoardBuildResult
{
    public string BoardId { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public Dictionary<string, string> ColumnIds { get; set; } = new();
    public Dictionary<string, string> CardIds { get; set; } = new();
}
