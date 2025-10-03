using Guacamole.Benchmarks.Infrastructure.TestScenarios;
using Xunit;

namespace Guacamole.Benchmarks.Tests;

public class ScenarioBuilderTests
{
    [Fact]
    public void SimpleScenario_Creates5NodeLinearGraph()
    {
        // Arrange & Act
        var (provider, scenario) = SimpleScenario.Build();

        // Assert
        Assert.Equal(5, scenario.NodeCount);
        Assert.Equal(4, scenario.DepthLevels);
        Assert.Equal("Simple_5Node_Linear", scenario.Name);
        Assert.False(scenario.IsComplex);

        // Verify provider works
        var result = provider.GetValue();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ComplexScenario_Creates21NodeTreeWith5Levels()
    {
        // Arrange & Act
        var (provider, scenario) = ComplexScenario.Build();

        // Assert
        Assert.Equal(21, scenario.NodeCount);
        Assert.Equal(5, scenario.DepthLevels);
        Assert.Equal("Complex_21Node_Tree", scenario.Name);
        Assert.True(scenario.IsComplex);

        // Verify provider works
        var result = provider.GetValue();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ScenarioBuilder_ProducesDeterministicGraphs()
    {
        // Arrange & Act
        var (provider1, scenario1) = SimpleScenario.Build();
        var (provider2, scenario2) = SimpleScenario.Build();

        // Assert - same structure should be built each time
        Assert.Equal(scenario1.NodeCount, scenario2.NodeCount);
        Assert.Equal(scenario1.DepthLevels, scenario2.DepthLevels);

        // And same results
        var result1 = provider1.GetValue();
        var result2 = provider2.GetValue();
        Assert.Equal(result1.Value, result2.Value);
    }
}
