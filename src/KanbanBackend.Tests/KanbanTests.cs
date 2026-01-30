using FluentValidation;
using HotChocolate;
using HotChocolate.Execution;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Mutations;
using KanbanBackend.API.GraphQL.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KanbanBackend.Tests;

public class KanbanTests
{
    private async Task<IRequestExecutor> GetExecutorAsync(string dbName)
    {
        return await new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName))
            .AddValidatorsFromAssemblyContaining<KanbanBackend.API.GraphQL.Validators.AddBoardInputValidator>()
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddProjections()
            .AddFiltering()
            .AddSorting()
            .BuildRequestExecutorAsync();
    } // Removed GetInMemoryContext helper as we will do it inline or via options helper

    
    private IOperationResult EnsureSuccess(IExecutionResult result)
    {
        var opResult = (IOperationResult)result;
        if (opResult.Errors?.Count > 0)
        {
            var error = opResult.Errors[0];
            throw new Exception($"GraphQL Error: {error.Message} \nException: {error.Exception}");
        }
        return opResult;
    }

    // A) Happy Path
    [Fact]
    public async Task HappyPath_AddBoardColumnCard_ReturnsCorrectData()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var executor = await GetExecutorAsync(dbName);

        // Act - 1. Add Board
        var boardResult = await executor.ExecuteAsync(@"
            mutation {
                addBoard(input: { name: ""Test Board"" }) {
                    id
                }
            }");
        
        var queryResult = EnsureSuccess(boardResult);
        var boardDict = (IReadOnlyDictionary<string, object>)queryResult.Data!["addBoard"]!;
        var boardId = boardDict["id"].ToString();
        Assert.NotNull(boardId);

        // Act - 2. Add Column
        var columnResult = await executor.ExecuteAsync($@"
            mutation {{
                addColumn(input: {{ boardId: ""{boardId}"", name: ""To Do"", order: 1 }}) {{
                    id
                }}
            }}");
        
        var queryResult2 = EnsureSuccess(columnResult);
        var columnDict = (IReadOnlyDictionary<string, object>)queryResult2.Data!["addColumn"]!;
        var columnId = columnDict["id"].ToString();
        Assert.NotNull(columnId);

        // Act - 3. Add Card
        var cardResult = await executor.ExecuteAsync($@"
            mutation {{
                addCard(input: {{ columnId: ""{columnId}"", name: ""Task 1"", rank: 1.0 }}) {{
                    id
                    name
                }}
            }}");
        
        // Assert
        // Assert
        var queryResult3 = EnsureSuccess(cardResult);
        var cardData = (IReadOnlyDictionary<string, object>)queryResult3.Data!["addCard"]!;
        Assert.Equal("Task 1", cardData["name"]);
    }

    // B) Hierarchy Check (Deep Nested)
    [Fact]
    public async Task HierarchyCheck_FullTreeOneQuery_ReturnsStructure()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var executor = await GetExecutorAsync(dbName);

        // Setup Data
        await executor.ExecuteAsync("mutation { addBoard(input: { name: \"Root\" }) { id } }"); // Just to have initial state if needed, but we capture IDs below
        
        var r1 = await executor.ExecuteAsync("mutation { addBoard(input: { name: \"B1\" }) { id } }");
        var bIdDict = (IReadOnlyDictionary<string, object>)EnsureSuccess(r1).Data!["addBoard"]!;
        var bId = bIdDict["id"];

        var r2 = await executor.ExecuteAsync($"mutation {{ addColumn(input: {{ boardId: \"{bId}\", name: \"C1\", order: 1 }}) {{ id }} }}");
        var cIdDict = (IReadOnlyDictionary<string, object>)EnsureSuccess(r2).Data!["addColumn"]!;
        var cId = cIdDict["id"];

        await executor.ExecuteAsync($"mutation {{ addCard(input: {{ columnId: \"{cId}\", name: \"Card1\", rank: 5.5 }}) {{ id }} }}");

        // Act - Query Deep
        var query = @"
            query {
                boards {
                    name
                    columns {
                        name
                        cards {
                            name
                            rank
                        }
                    }
                }
            }";

        var result = await executor.ExecuteAsync(query);
        var json = EnsureSuccess(result).ToJson();

        // Assert
        Assert.Contains("B1", json);
        Assert.Contains("C1", json);
        Assert.Contains("Card1", json);
        Assert.Contains("5.5", json);
    }

    // C) Reference Error (Data Integrity)
    // The mutation itself validates that the Column exists before adding a Card.
    // This test verifies that behavior using InMemory.
    [Fact]
    public async Task Integrity_AddCardToNonExistentColumn_ThrowsException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var executor = await GetExecutorAsync(dbName);

        // Act & Assert
        var randomId = Guid.NewGuid();
        
        var error = await Assert.ThrowsAsync<Exception>(async () => 
        {
             var result = await executor.ExecuteAsync($@"
            mutation {{
                addCard(input: {{ columnId: ""{randomId}"", name: ""Ghost Card"", rank: 1.0 }}) {{
                    id
                }}
            }}");
            EnsureSuccess(result);
        });
        
        Assert.Contains("Column not found", error.Message);
    }

    // D) Isolation Test (Multi-Board)
    [Fact]
    public async Task Isolation_BoardsDoNotLeakData()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var executor = await GetExecutorAsync(dbName);

        // Board A
        var rb1 = await executor.ExecuteAsync("mutation { addBoard(input: { name: \"Board A\" }) { id } }");
        var idADict = (IReadOnlyDictionary<string, object>)EnsureSuccess(rb1).Data!["addBoard"]!;
        var idA = idADict["id"];
        await executor.ExecuteAsync($"mutation {{ addColumn(input: {{ boardId: \"{idA}\", name: \"Col A\", order: 1 }}) {{ id }} }}");

        // Board B
        var rb2 = await executor.ExecuteAsync("mutation { addBoard(input: { name: \"Board B\" }) { id } }");
        var idBDict = (IReadOnlyDictionary<string, object>)EnsureSuccess(rb2).Data!["addBoard"]!;
        var idB = idBDict["id"];
        // Board B is empty

        // Act
        var queryA = $@"query {{ boards(where: {{ id: {{ eq: ""{idA}"" }} }}) {{ columns {{ name }} }} }}";
        var queryB = $@"query {{ boards(where: {{ id: {{ eq: ""{idB}"" }} }}) {{ columns {{ name }} }} }}";

        var resA = await executor.ExecuteAsync(queryA);
        var resB = await executor.ExecuteAsync(queryB);

        var jsonA = EnsureSuccess(resA).ToJson();
        var jsonB = EnsureSuccess(resB).ToJson();

        // Assert
        Assert.Contains("Col A", jsonA);
        Assert.DoesNotContain("Col A", jsonB);
    }

    // E) Sorting-Check
    [Fact]
    public async Task Sorting_ColumnsReturnedInCorrectOrder()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var executor = await GetExecutorAsync(dbName);

        var rb = await executor.ExecuteAsync("mutation { addBoard(input: { name: \"Sort Board\" }) { id } }");
        var bIdDict = (IReadOnlyDictionary<string, object>)EnsureSuccess(rb).Data!["addBoard"]!;
        var bId = bIdDict["id"];

        // Add Columns in random order: 3, 1, 2
        await executor.ExecuteAsync($"mutation {{ addColumn(input: {{ boardId: \"{bId}\", name: \"Three\", order: 3 }}) {{ id }} }}");
        await executor.ExecuteAsync($"mutation {{ addColumn(input: {{ boardId: \"{bId}\", name: \"One\", order: 1 }}) {{ id }} }}");
        await executor.ExecuteAsync($"mutation {{ addColumn(input: {{ boardId: \"{bId}\", name: \"Two\", order: 2 }}) {{ id }} }}");

        // Act - Query with sort
        // Note: Sort syntax depends on HotChocolate Filtering/Sorting package. Usually `order: { order: ASC }`
        var query = @"
            query {
                boards {
                    columns(order: { order: ASC }) {
                        name
                        order
                    }
                }
            }";

        var result = await executor.ExecuteAsync(query);
        var curResult = EnsureSuccess(result);
        
        // Parse the result using typed access
        var boards = (IReadOnlyList<object>)curResult.Data!["boards"]!;
        var firstBoard = (IReadOnlyDictionary<string, object>)boards[0];
        var columns = (IReadOnlyList<object>)firstBoard["columns"]!;
        
        var columnNames = columns
            .Cast<IReadOnlyDictionary<string, object>>()
            .Select(c => c["name"].ToString())
            .ToList();

        // Assert order: One, Two, Three
        Assert.Equal(3, columnNames.Count);
        Assert.Equal("One", columnNames[0]);
        Assert.Equal("Two", columnNames[1]);
        Assert.Equal("Three", columnNames[2]);
    }
}
