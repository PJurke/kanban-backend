using HotChocolate.Data;

namespace KanbanBackend.API.Models;

public class Board
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [UseSorting]
    public ICollection<Column> Columns { get; set; } = new List<Column>();
}
