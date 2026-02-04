using FluentAssertions;
using KanbanBackend.API.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KanbanBackend.Tests;

public class OptionsIntegrationTests : IntegrationTestBase
{
    public OptionsIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public void RankRebalancingOptions_ShouldBindFromConfiguration()
    {
        // Assemble
        var overrides = new Dictionary<string, string?>
        {
            { "RankRebalancing:MinGap", "0.000000001" }, // 1e-9
            { "RankRebalancing:Spacing", "42.0" }
        };

        var client = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(overrides);
            });
        }).Services.CreateScope();

        // Act
        // We resolve IOptions<RankRebalancingOptions> from the scope provider
        var options = client.ServiceProvider.GetRequiredService<IOptions<RankRebalancingOptions>>().Value;

        // Assert
        options.Should().NotBeNull();
        options.MinGap.Should().Be(1e-9);
        options.Spacing.Should().Be(42.0);
    }
}
