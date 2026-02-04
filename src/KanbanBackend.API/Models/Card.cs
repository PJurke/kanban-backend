namespace KanbanBackend.API.Models;

public class Card
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Rank { get; set; }
    
    public Guid ColumnId { get; set; }
    public Column? Column { get; set; }

    [GraphQLIgnore]
    public uint RowVersion { get; set; }

    [GraphQLName("rowVersion")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string RowVersionToken => Convert.ToBase64String(BitConverter.GetBytes(RowVersion));
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
