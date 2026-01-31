using HotChocolate.Data;

namespace KanbanBackend.API.Models;

public class Board
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty; // Multi-Tenancy
    [UseSorting]
    public ICollection<Column> Columns { get; set; } = new List<Column>();
}
