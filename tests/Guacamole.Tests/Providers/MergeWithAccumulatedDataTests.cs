using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Tests demonstrating that Merge() works correctly, but MISUSE occurs when
/// merging streams extracted from modules that accumulate data from previous stages.
///
/// This is NOT a Guacamole bug - it's incorrect usage pattern in Fugu.
/// </summary>
public class MergeWithAccumulatedDataTests
{
    /// <summary>
    /// Demonstrates the INCORRECT pattern used in Fugu where each stage accumulates
    /// diagnostics from previous stages, then all stages' diagnostics are extracted and merged.
    ///
    /// This is EXPECTED to produce duplicates because earlier-stage diagnostics appear
    /// in multiple accumulated streams.
    /// </summary>
    [Fact]
    public void Merge_WithAccumulatedData_ProducesDuplicates_ThisIsExpected()
    {
        // Arrange - This models the WRONG Fugu pattern
        var graph = new ComputationGraph();

        // Stage 1: Type checking produces TYPE001
        var stage1Diagnostics = ImmutableArray.Create("TYPE001");

        // Stage 2: Exhaustiveness ACCUMULATES stage1 + adds MATCH001
        var stage2Diagnostics = stage1Diagnostics.Add("MATCH001"); // [TYPE001, MATCH001]

        // Create providers for each stage
        var stage1Node = new SimpleCollectionNode<string>("stage1", stage1Diagnostics);
        var stage2Node = new SimpleCollectionNode<string>("stage2", stage2Diagnostics);

        var stage1Provider = new IncrementalValuesProvider<string>(stage1Node, graph, default);
        var stage2Provider = new IncrementalValuesProvider<string>(stage2Node, graph, default);

        // INCORRECT: Merge accumulated streams (Fugu's current pattern)
        var allDiagnostics = stage1Provider.Merge(stage2Provider);

        // Act
        var result = allDiagnostics.GetValues();

        // Assert - This WILL have duplicates, which is CORRECT behavior for this pattern!
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        // We get [TYPE001, TYPE001, MATCH001] because TYPE001 is in BOTH streams
        Assert.Equal(3, diagnostics.Length);
        Assert.Equal("TYPE001", diagnostics[0]); // From stage1
        Assert.Equal("TYPE001", diagnostics[1]); // From stage2 (accumulated)
        Assert.Equal("MATCH001", diagnostics[2]); // From stage2 (new)

        // This demonstrates that Merge() is working CORRECTLY
        // The "bug" is in how Fugu structures its pipeline
    }

    /// <summary>
    /// Demonstrates the CORRECT pattern: Each stage only contains NEW diagnostics
    /// </summary>
    [Fact]
    public void Merge_WithOnlyNewData_NoDuplicates()
    {
        // Arrange - CORRECT pattern
        var graph = new ComputationGraph();

        // Stage 1: Type checking produces only TYPE001
        var stage1OnlyDiagnostics = ImmutableArray.Create("TYPE001");

        // Stage 2: Exhaustiveness produces ONLY new diagnostics
        var stage2OnlyDiagnostics = ImmutableArray.Create("MATCH001");

        var stage1Node = new SimpleCollectionNode<string>("stage1", stage1OnlyDiagnostics);
        var stage2Node = new SimpleCollectionNode<string>("stage2", stage2OnlyDiagnostics);

        var stage1Provider = new IncrementalValuesProvider<string>(stage1Node, graph, default);
        var stage2Provider = new IncrementalValuesProvider<string>(stage2Node, graph, default);

        // CORRECT: Merge non-accumulated streams
        var allDiagnostics = stage1Provider.Merge(stage2Provider);

        // Act
        var result = allDiagnostics.GetValues();

        // Assert - No duplicates
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.Equal("TYPE001", diagnostics[0]);
        Assert.Equal("MATCH001", diagnostics[1]);
    }

    /// <summary>
    /// Demonstrates ALTERNATIVE CORRECT pattern: Use only the final stage's accumulated diagnostics
    /// </summary>
    [Fact]
    public void SelectMany_OnFinalStageOnly_NoMergeNeeded()
    {
        // Arrange - Use final stage only
        var graph = new ComputationGraph();

        // Final stage has ALL accumulated diagnostics
        var finalStageDiagnostics = ImmutableArray.Create("TYPE001", "MATCH001");

        var finalStageNode = new SimpleCollectionNode<string>("final", finalStageDiagnostics);
        var finalStageProvider = new IncrementalValuesProvider<string>(finalStageNode, graph, default);

        // CORRECT: Just use the final stage - no merge needed!
        // Act
        var result = finalStageProvider.GetValues();

        // Assert - No duplicates, all diagnostics present
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.Equal("TYPE001", diagnostics[0]);
        Assert.Equal("MATCH001", diagnostics[1]);
    }

    /// <summary>
    /// Simple test node
    /// </summary>
    private class SimpleCollectionNode<T> : ICollectionComputationNode<T>
    {
        private readonly ImmutableArray<T> _values;

        public SimpleCollectionNode(string id, ImmutableArray<T> values)
        {
            Id = id;
            _values = values;
        }

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<T>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;

        public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
        {
            return Result<IEnumerable<T>, ComputationError>.Success(_values);
        }
    }
}
