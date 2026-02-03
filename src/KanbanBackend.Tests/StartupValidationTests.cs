using FluentAssertions;
using KanbanBackend.API.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KanbanBackend.Tests;

public class StartupValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StartupValidationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Application_ShouldThrow_WhenConfigurationIsInvalid()
    {
        // Assemble
        var invalidConfig = new Dictionary<string, string?>
        {
            { "RankRebalancing:MinGap", "5000" }, 
            { "RankRebalancing:Spacing", "10" } // MinGap > Spacing -> Invalid
        };

        var clientFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(invalidConfig);
            });
        });

        // Act & Assert
        // Creating the client forces Host build and startup actions.
        // ValidateOnStart runs during DI resolution or at startup if eager validation is enabled (it is by ValidateOnStart).
        // However, ValidateOnStart typically throws when you resolve the Options or when the host starts HostedServices.
        // Usually creating a client triggers the server start.
        
        Action act = () => clientFactory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MinGap must be less than Spacing*");
    }
}
