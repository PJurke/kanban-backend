namespace KanbanBackend.API.GraphQL.Payloads;

public class CardPayload
{
    public Guid Id { get; set; }
    public Guid ColumnId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Rank { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public CardPayload() { }

    public CardPayload(Models.Card card)
    {
        Id = card.Id;
        ColumnId = card.ColumnId;
        Name = card.Name;
        Rank = card.Rank;
        RowVersion = Convert.ToBase64String(BitConverter.GetBytes(card.RowVersion));
        CreatedAt = card.CreatedAt;
    }
}
