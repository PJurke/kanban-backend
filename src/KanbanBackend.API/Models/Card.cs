namespace KanbanBackend.API.Models;

public class Card
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Rank { get; set; }
    
    public Guid ColumnId { get; set; }
    public Column? Column { get; set; }
}
