using FluentAssertions;
using KanbanBackend.API.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace KanbanBackend.Tests;

public class RankRebalancingOptionsValidatorTests
{
    private readonly RankRebalancingOptionsValidator _validator = new();

    [Fact]
    public void Validate_ShouldReturnSuccess_WhenOptionsAreValid()
    {
        // Arrange
        var options = new RankRebalancingOptions { MinGap = 0.001, Spacing = 1000.0 };

        // Act
        var result = _validator.Validate("RankRebalancing", options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenMinGapIsZeroOrNegative()
    {
        // Arrange
        var options = new RankRebalancingOptions { MinGap = 0, Spacing = 1000.0 };

        // Act
        var result = _validator.Validate("RankRebalancing", options);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("MinGap must be > 0");
    }

    [Fact]
    public void Validate_ShouldFail_WhenSpacingIsZeroOrNegative()
    {
        // Arrange
        var options = new RankRebalancingOptions { MinGap = 0.001, Spacing = -10 };

        // Act
        var result = _validator.Validate("RankRebalancing", options);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Spacing must be > 0");
    }

    [Fact]
    public void Validate_ShouldFail_WhenMinGapIsGreaterOrEqualSpacing()
    {
        // Arrange
        var options = new RankRebalancingOptions { MinGap = 100, Spacing = 50 };

        // Act
        var result = _validator.Validate("RankRebalancing", options);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("MinGap must be less than Spacing");
    }
}
