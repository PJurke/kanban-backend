using HotChocolate;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBackend.API.GraphQL.Mutations;

public class Mutation
{
    public async Task<Board> AddBoard(
        string name,
        [Service] AppDbContext context)
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        context.Boards.Add(board);
        await context.SaveChangesAsync();

        return board;
    }

    public async Task<Column> AddColumn(
        Guid boardId,
        string name,
        int order,
        [Service] AppDbContext context)
    {
        if (!await context.Boards.AnyAsync(b => b.Id == boardId))
        {
            throw new GraphQLException("Board not found");
        }

        var column = new Column
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            Name = name,
            Order = order
        };

        context.Columns.Add(column);
        await context.SaveChangesAsync();

        return column;
    }

    public async Task<Card> AddCard(
        Guid columnId,
        string name,
        double rank,
        [Service] AppDbContext context)
    {
        if (!await context.Columns.AnyAsync(c => c.Id == columnId))
        {
            throw new GraphQLException("Column not found");
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            ColumnId = columnId,
            Name = name,
            Rank = rank
        };

        context.Cards.Add(card);
        await context.SaveChangesAsync();

        return card;
    }
}
