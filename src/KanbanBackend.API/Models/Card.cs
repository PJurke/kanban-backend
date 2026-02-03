namespace KanbanBackend.API.Models;

public class Card
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Rank { get; set; }
    
    public Guid ColumnId { get; set; }
    public Column? Column { get; set; }

    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
