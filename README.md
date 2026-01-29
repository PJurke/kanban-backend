# Kanban Backend

This is a .NET 8 Web API project using HotChocolate for GraphQL and Entity Framework Core with SQLite.

## Structure

- **src/KanbanBackend.API**: The main Web API project.
  - **Data**: Entity Framework DbContext and configurations.
  - **Models**: Domain entities (e.g., `TaskItem`).
  - **GraphQL**: GraphQL types, queries, and mutations.
    - **Queries**: Read operations.
  - **Services**: Business logic services (currently placeholder).

## Getting Started

1.  **Run the application**:
    ```bash
    dotnet run --project src/KanbanBackend.API/KanbanBackend.API.csproj
    ```

2.  **Access GraphQL Playground**:
    Open your browser and navigate to `http://localhost:5000/graphql` (or the port shown in the terminal).

3.  **Sample Query**:
    ```graphql
    query {
      tasks {
        id
        title
        description
        isCompleted
      }
    }
    ```
