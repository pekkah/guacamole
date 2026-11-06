using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Nodes;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers;

/// <summary>
/// Debug test to understand how Merge+SelectMany works
/// </summary>
public class SelectManyMergeDebuggingTests
{
    private readonly ITestOutputHelper _output;

    public SelectManyMergeDebuggingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private record TypeCheckedModule(
        string SourceFile,
        ImmutableArray<string> Diagnostics,
        bool Success
    );

    private record ExhaustivenessCheckedModule(
        string SourceFile,
        ImmutableArray<string> Diagnostics,
        bool Success
    );

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

    [Fact]
    public void Debug_MergeSelectMany_ExecutionCount()
    {
        // Arrange
        var graph = new ComputationGraph();

        var typeCheckedModule = new TypeCheckedModule("/test/main.fugu", ImmutableArray.Create("TYPE001"), true);
        var typeCheckedNode = new TypeCheckedModuleNode("TypeChecked", typeCheckedModule);
        var typeCheckedProvider = new IncrementalValuesProvider<TypeCheckedModule>(typeCheckedNode, graph, default);

        var exhaustivenessCheckedModule = new ExhaustivenessCheckedModule("/test/main.fugu", ImmutableArray.Create("TYPE001", "MATCH001"), false);
        var exhaustivenessNode = new ExhaustivenessCheckedModuleNode("Exhaustiveness", exhaustivenessCheckedModule);
        var exhaustivenessProvider = new IncrementalValuesProvider<ExhaustivenessCheckedModule>(exhaustivenessNode, graph, default);

        int selectMany1CallCount = 0;
        int selectMany2CallCount = 0;

        // Use tracking selectors to count how many times they're called
        var typeCheckDiagnostics = typeCheckedProvider.SelectMany(m =>
        {
            selectMany1CallCount++;
            _output.WriteLine($"SelectMany1 called (count: {selectMany1CallCount}), returning {m.Diagnostics.Length} diagnostics");
            return m.Diagnostics;
        });

        var exhaustivenessDiagnostics = exhaustivenessProvider.SelectMany(m =>
        {
            selectMany2CallCount++;
            _output.WriteLine($"SelectMany2 called (count: {selectMany2CallCount}), returning {m.Diagnostics.Length} diagnostics");
            return m.Diagnostics;
        });

        var allDiagnostics = typeCheckDiagnostics.Merge(exhaustivenessDiagnostics);

        // Act - Call GetValues() which should execute the computation
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);

        _output.WriteLine("=== First Compute ===");
        var result = allDiagnostics.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var diagnostics = result.Value!.ToArray();

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"SelectMany1 was called {selectMany1CallCount} times");
        _output.WriteLine($"SelectMany2 was called {selectMany2CallCount} times");
        _output.WriteLine($"Total diagnostics returned: {diagnostics.Length}");
        for (int i = 0; i < diagnostics.Length; i++)
        {
            _output.WriteLine($"  [{i}] = {diagnostics[i]}");
        }

        // The critical question: How many times is each SelectMany called?
        // Expected: Each should be called once per merge
        //assert: selectMany1CallCount should be 1 or 2 depending on implementation
        _output.WriteLine($"\nExpected 3 total diagnostics, got {diagnostics.Length}");
    }
}
