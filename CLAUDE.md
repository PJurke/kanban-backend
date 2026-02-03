# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
# Run the application
dotnet run --project src/KanbanBackend.API/KanbanBackend.API.csproj

# Run all tests
dotnet test src/KanbanBackend.Tests/KanbanBackend.Tests.csproj

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests with verbose output
dotnet test --verbosity=detailed

# Build the solution
dotnet build
```

## Architecture Overview

This is a .NET 10 GraphQL API for a Kanban board system using HotChocolate 15, Entity Framework Core with SQLite, and ASP.NET Identity with JWT authentication.

### Project Structure

- `src/KanbanBackend.API/` - Main API project
- `src/KanbanBackend.Tests/` - xUnit tests with FluentAssertions and Moq

### Key Architectural Patterns

**Service Layer**: Business logic is encapsulated in services (`CardService`, `BoardService`, `ColumnService`) with interfaces for DI.

**GraphQL Organization**: Mutations are organized by domain in `GraphQL/Mutations/` (AuthMutations, BoardMutations, ColumnMutations, CardMutations). Queries, subscriptions, inputs, and payloads each have dedicated folders.

**Multi-Tenancy**: Boards have an `OwnerId` field; ownership validation is required for all board operations.

**Card Ranking**: Cards use fractional ranking (Jira-style) with automatic rebalancing when rank gaps become too small. Configuration is in `RankRebalancingOptions`.

**Optimistic Concurrency**: Cards have a `RowVersion` field for concurrent update detection.

### Domain Model

- **Board** → owns multiple **Columns** → each contains multiple **Cards**
- **AppUser** (Identity) → owns **Boards** and **RefreshTokens**
- All relationships cascade delete

### Custom Exceptions

Located in `Exceptions/`: `DomainException`, `EntityNotFoundException`, `PreconditionRequiredException`, `RebalanceFailedException`. These are mapped to GraphQL errors via `GraphQLErrorFilter`.

### Testing Patterns

Integration tests extend `IntegrationTestBase` which provides:
- Isolated SQLite database per test (`test_{guid}.db`)
- `CreateAuthenticatedClientAsync()` for authenticated requests
- Automatic cleanup
