namespace KanbanBackend.API.GraphQL.Inputs;

public class MoveCardInput
{
    public Guid CardId { get; set; }
    public Guid ColumnId { get; set; }
    public double Rank { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
