using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace KanbanBackend.Tests.Builders;

public class CardBuilder
{
    private readonly HttpClient _client;
    private string _columnId = string.Empty;
    private string _name = "Test Card";
    private double _rank = 0;

    public CardBuilder(HttpClient client)
    {
        _client = client;
    }

    public CardBuilder InColumn(string columnId)
    {
        _columnId = columnId;
        return this;
    }

    public CardBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public CardBuilder WithRank(double rank)
    {
        _rank = rank;
        return this;
    }

    public async Task<string> BuildAsync()
    {
        if (string.IsNullOrEmpty(_columnId))
            throw new InvalidOperationException("ColumnId must be set.");

        var createCardQuery = new
        {
            query = $@"mutation {{ addCard(input: {{ columnId: ""{_columnId}"", name: ""{_name}"", rank: {_rank} }}) {{ id }} }}"
        };

        var cardRes = await _client.PostAsJsonAsync("/graphql", createCardQuery);
        cardRes.EnsureSuccessStatusCode();
        var cardBody = await cardRes.Content.ReadAsStringAsync();
        var cardId = JsonNode.Parse(cardBody)?["data"]?["addCard"]?["id"]?.GetValue<string>();

        if (string.IsNullOrEmpty(cardId))
            throw new Exception($"Failed to create card '{_name}'");

        return cardId;
    }
}
