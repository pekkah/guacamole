using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Nodes;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers;

/// <summary>
/// Regression tests for Issue #23: Duplicate diagnostics when using Merge() on SelectMany() providers
///
/// The issue occurs when:
/// 1. Multiple compilation stages accumulate diagnostics (later stages include previous diagnostics)
/// 2. Each stage's diagnostics are extracted via SelectMany(m => m.Diagnostics)
/// 3. The streams are combined via Merge()
///
/// Expected: Each diagnostic appears once
/// Actual: Diagnostics appear duplicated
/// </summary>
public class SelectManyMergeDuplicationTests
{
    /// <summary>
    /// Represents a module that has been type-checked (first stage)
    /// </summary>
    private record TypeCheckedModule(
        string SourceFile,
        ImmutableArray<string> Diagnostics,
        bool Success
    );

    /// <summary>
    /// Represents a module that has been exhaustiveness-checked (second stage)
    /// This stage accumulates diagnostics from the previous stage
    /// </summary>
    private record ExhaustivenessCheckedModule(
        string SourceFile,
        ImmutableArray<string> Diagnostics, // Contains TypeCheck + Exhaustiveness diagnostics
        bool Success
    );

    /// <summary>
    /// Test node that produces a single TypeCheckedModule
    /// </summary>
    private class TypeCheckedModuleNode : ICollectionComputationNode<TypeCheckedModule>
    {
        private readonly TypeCheckedModule _module;

        public TypeCheckedModuleNode(string id, TypeCheckedModule module)
        {
            Id = id;
            _module = module;
        }

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<TypeCheckedModule>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;

        public Result<IEnumerable<TypeCheckedModule>, ComputationError> Compute(ComputationContext context)
        {
            return Result<IEnumerable<TypeCheckedModule>, ComputationError>.Success(new[] { _module });
        }
    }

    /// <summary>
    /// Test node that produces a single ExhaustivenessCheckedModule
    /// </summary>
    private class ExhaustivenessCheckedModuleNode : ICollectionComputationNode<ExhaustivenessCheckedModule>
    {
        private readonly ExhaustivenessCheckedModule _module;

        public ExhaustivenessCheckedModuleNode(string id, ExhaustivenessCheckedModule module)
        {
            Id = id;
            _module = module;
        }

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<ExhaustivenessCheckedModule>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;

        public Result<IEnumerable<ExhaustivenessCheckedModule>, ComputationError> Compute(ComputationContext context)
        {
            return Result<IEnumerable<ExhaustivenessCheckedModule>, ComputationError>.Success(new[] { _module });
        }
    }

    /// <summary>
    /// REGRESSION TEST: Reproduces Issue #23
    ///
    /// Simulates the exact scenario from Fugu compiler:
    /// - TypeCheckedModules contains 1 module with diagnostics: [TYPE001]
    /// - ExhaustivenessCheckedModules contains 1 module with diagnostics: [TYPE001, MATCH001]
    /// - TypeCheckDiagnostics.SelectMany extracts: [TYPE001]
    /// - ExhaustivenessDiagnostics.SelectMany extracts: [TYPE001, MATCH001]
    /// - AllDiagnostics.Merge should yield: [TYPE001, MATCH001] (2 diagnostics total)
    ///
    /// Actual behavior (FAILING): [TYPE001, MATCH001] appears twice
    /// </summary>
    [Fact]
    public void Merge_WithSelectMany_ShouldNotDuplicateDiagnostics()
    {
        // Arrange - Simulate the exact Fugu compiler pipeline
        var graph = new ComputationGraph();

        // Stage 1: TypeCheckedModules (only TYPE001 diagnostic)
        var typeCheckedModule = new TypeCheckedModule(
            SourceFile: "/test/main.fugu",
            Diagnostics: ImmutableArray.Create("TYPE001"),
            Success: true
        );
        var typeCheckedNode = new TypeCheckedModuleNode("TypeChecked", typeCheckedModule);
        var typeCheckedProvider = new IncrementalValuesProvider<TypeCheckedModule>(typeCheckedNode, graph, default);

        // Stage 2: ExhaustivenessCheckedModules (accumulated: TYPE001 + MATCH001)
        var exhaustivenessCheckedModule = new ExhaustivenessCheckedModule(
            SourceFile: "/test/main.fugu",
            Diagnostics: ImmutableArray.Create("TYPE001", "MATCH001"), // Accumulated from previous stage
            Success: false
        );
        var exhaustivenessNode = new ExhaustivenessCheckedModuleNode("Exhaustiveness", exhaustivenessCheckedModule);
        var exhaustivenessProvider = new IncrementalValuesProvider<ExhaustivenessCheckedModule>(exhaustivenessNode, graph, default);

        // Extract diagnostics using SelectMany (as in FuguCompilerContext lines 305-307)
        var typeCheckDiagnostics = typeCheckedProvider.SelectMany(m => m.Diagnostics);
        var exhaustivenessDiagnostics = exhaustivenessProvider.SelectMany(m => m.Diagnostics);

        // Merge the diagnostic streams (as in FuguCompilerContext lines 312-316)
        var allDiagnostics = typeCheckDiagnostics.Merge(exhaustivenessDiagnostics);

        // Act - Compute the merged diagnostics
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = allDiagnostics.Node.Compute(context);

        // Assert - Should have exactly 3 diagnostics without duplication
        // typeCheckDiagnostics yields: [TYPE001]
        // exhaustivenessDiagnostics yields: [TYPE001, MATCH001]
        // merged should yield: [TYPE001, TYPE001, MATCH001]
        //
        // NOTE: We expect TYPE001 to appear twice because it's in BOTH streams.
        // The issue is if MATCH001 appears twice (which would mean 4 total items).
        Assert.True(result.IsSuccess, "Merge should succeed");
        var diagnostics = result.Value!.ToArray();

        // Expected: 3 diagnostics total (TYPE001 from first stream, TYPE001 and MATCH001 from second stream)
        // If this fails with 4 or more items, it means duplication is occurring
        Assert.Equal(3, diagnostics.Length);

        // Verify the exact content
        Assert.Equal("TYPE001", diagnostics[0]); // From typeCheckDiagnostics
        Assert.Equal("TYPE001", diagnostics[1]); // From exhaustivenessDiagnostics
        Assert.Equal("MATCH001", diagnostics[2]); // From exhaustivenessDiagnostics
    }

    /// <summary>
    /// REGRESSION TEST: Simplified case with multiple Merge operations
    ///
    /// Tests the scenario where multiple diagnostic streams are merged:
    /// AllDiagnostics = Stream1.Merge(Stream2).Merge(Stream3)
    ///
    /// This is even more likely to trigger duplication issues.
    /// </summary>
    [Fact]
    public void Merge_WithMultipleSelectMany_ShouldNotDuplicateDiagnostics()
    {
        // Arrange
        var graph = new ComputationGraph();

        // Create three modules with overlapping diagnostics
        var module1 = new TypeCheckedModule("/file1.fugu", ImmutableArray.Create("DIAG1"), true);
        var module2 = new ExhaustivenessCheckedModule("/file2.fugu", ImmutableArray.Create("DIAG1", "DIAG2"), true);
        var module3 = new ExhaustivenessCheckedModule("/file3.fugu", ImmutableArray.Create("DIAG1", "DIAG2", "DIAG3"), true);

        var provider1 = new IncrementalValuesProvider<TypeCheckedModule>(
            new TypeCheckedModuleNode("mod1", module1), graph, default);
        var provider2 = new IncrementalValuesProvider<ExhaustivenessCheckedModule>(
            new ExhaustivenessCheckedModuleNode("mod2", module2), graph, default);
        var provider3 = new IncrementalValuesProvider<ExhaustivenessCheckedModule>(
            new ExhaustivenessCheckedModuleNode("mod3", module3), graph, default);

        // Extract diagnostics
        var diag1 = provider1.SelectMany(m => m.Diagnostics);
        var diag2 = provider2.SelectMany(m => m.Diagnostics);
        var diag3 = provider3.SelectMany(m => m.Diagnostics);

        // Chain multiple merges (as in FuguCompilerContext lines 312-316)
        var allDiagnostics = diag1.Merge(diag2).Merge(diag3);

        // Act
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = allDiagnostics.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        // Expected: 6 total diagnostics
        // diag1: [DIAG1]
        // diag2: [DIAG1, DIAG2]
        // diag3: [DIAG1, DIAG2, DIAG3]
        // Total: 1 + 2 + 3 = 6
        Assert.Equal(6, diagnostics.Length);

        // Verify content (order: diag1, then diag2, then diag3)
        Assert.Equal("DIAG1", diagnostics[0]);  // From diag1
        Assert.Equal("DIAG1", diagnostics[1]);  // From diag2
        Assert.Equal("DIAG2", diagnostics[2]);  // From diag2
        Assert.Equal("DIAG1", diagnostics[3]);  // From diag3
        Assert.Equal("DIAG2", diagnostics[4]);  // From diag3
        Assert.Equal("DIAG3", diagnostics[5]);  // From diag3
    }

    /// <summary>
    /// REGRESSION TEST: Edge case with empty collections
    ///
    /// Ensures Merge doesn't duplicate when one stream is empty
    /// </summary>
    [Fact]
    public void Merge_WithEmptySelectMany_ShouldNotDuplicate()
    {
        // Arrange
        var graph = new ComputationGraph();

        var module1 = new TypeCheckedModule("/file1.fugu", ImmutableArray<string>.Empty, true);
        var module2 = new ExhaustivenessCheckedModule("/file2.fugu", ImmutableArray.Create("MATCH001"), true);

        var provider1 = new IncrementalValuesProvider<TypeCheckedModule>(
            new TypeCheckedModuleNode("mod1", module1), graph, default);
        var provider2 = new IncrementalValuesProvider<ExhaustivenessCheckedModule>(
            new ExhaustivenessCheckedModuleNode("mod2", module2), graph, default);

        var diag1 = provider1.SelectMany(m => m.Diagnostics);
        var diag2 = provider2.SelectMany(m => m.Diagnostics);
        var merged = diag1.Merge(diag2);

        // Act
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = merged.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        // Expected: 1 diagnostic (only MATCH001)
        Assert.Single(diagnostics);
        Assert.Equal("MATCH001", diagnostics[0]);
    }

    /// <summary>
    /// BASELINE TEST: Verify that Merge works correctly with direct values (not SelectMany)
    ///
    /// This test should PASS, confirming that Merge itself is not broken,
    /// but rather the interaction with SelectMany causes the issue.
    /// </summary>
    [Fact]
    public void Merge_WithoutSelectMany_ShouldWork()
    {
        // Arrange
        var graph = new ComputationGraph();

        var node1 = new SimpleValuesNode<string>("diag1", new[] { "DIAG1" });
        var node2 = new SimpleValuesNode<string>("diag2", new[] { "DIAG2", "DIAG3" });

        var provider1 = new IncrementalValuesProvider<string>(node1, graph, default);
        var provider2 = new IncrementalValuesProvider<string>(node2, graph, default);

        var merged = provider1.Merge(provider2);

        // Act
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = merged.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        // Expected: 3 diagnostics
        Assert.Equal(3, diagnostics.Length);
        Assert.Equal("DIAG1", diagnostics[0]);
        Assert.Equal("DIAG2", diagnostics[1]);
        Assert.Equal("DIAG3", diagnostics[2]);
    }

    /// <summary>
    /// Test node for baseline tests - simple values node
    /// </summary>
    private class SimpleValuesNode<T> : ICollectionComputationNode<T>
    {
        private readonly T[] _values;

        public SimpleValuesNode(string id, T[] values)
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
