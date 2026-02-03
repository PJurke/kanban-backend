using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KanbanBackend.Tests;

public abstract class IntegrationTestBase : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    protected readonly WebApplicationFactory<Program> Factory;
    private readonly string _dbFileName;

    protected IntegrationTestBase(WebApplicationFactory<Program> factory)
    {
        _dbFileName = $"test_{Guid.NewGuid()}.db";
        Factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Auth:JwtSecret", "SuperSecretKeyForTesting1234567890!@" },
                    { "ConnectionStrings:DefaultConnection", $"Data Source={_dbFileName}" },
                    { "RankRebalancing:MinGap", "0.001" },
                    { "RankRebalancing:Spacing", "1000" },
                    { "RankRebalancing:MaxAttempts", "3" }
                });
            });
        });
    }

    public void Dispose()
    {
        if (File.Exists(_dbFileName))
        {
            try
            {
                File.Delete(_dbFileName);
            }
            catch
            {
                // Ignored to prevent test failures during cleanup
            }
        }
    }

    protected async Task<(HttpClient Client, string UserId, string Email)> CreateAuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();
        var email = $"user_{Guid.NewGuid()}@example.com";
        var password = "Password123!";

        // Register
        await client.PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ register(email: ""{email}"", password: ""{password}"") {{ email }} }}"
        });

        // Login
        var loginRes = await client.PostAsJsonAsync("/graphql", new
        {
            query = $@"mutation {{ login(email: ""{email}"", password: ""{password}"") {{ accessToken user {{ email }} }} }}"
        });
        
        var body = await loginRes.Content.ReadAsStringAsync();
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (Exception ex)
        {
             throw new Exception($"Failed to parse JSON. Status: {loginRes.StatusCode}. Body: {body}", ex);
        }
        var token = json?["data"]?["login"]?["accessToken"]?.GetValue<string>();
        
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception($"Failed to extract token. Body: {body}");
        }
        
        // Create authenticated client
        var authClient = Factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        return (authClient, token, email);
    }
}
