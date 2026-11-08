using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

/// <summary>
/// Verifies that the generator pipeline is incremental: unrelated edits must not cause the
/// tracked steps to produce new outputs, and models must not capture Roslyn objects.
/// </summary>
public class IncrementalityTests
{
    private static readonly string[] TrackedSteps =
    [
        ComputingContextGenerator.AnalysisStepName,
        ComputingContextGenerator.ModelStepName,
        ComputingContextGenerator.DiagnosticsStepName,
    ];

    private const string ContextSource = """
        using Guacamole;

        namespace Test
        {
            public class Person
            {
                public string Name { get; set; } = "";
            }

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public partial IncrementalValuesProvider<string> Items { get; }

                public partial IncrementalValuesProvider<Person> People { get; }

                // GUAC0003 (reported by the generator): get-only and not partial
                public IncrementalValuesProvider<string> NotPartial { get; }
            }
        }
        """;

    private const string OtherSource = """
        namespace Test
        {
            public class Unrelated
            {
                public int Value => 1;
            }
        }
        """;

    [Fact]
    public void UnrelatedEdit_TrackedStepOutputsAreCachedOrUnchanged()
    {
        var compilation = TestHelper.CreateCompilation(ContextSource, OtherSource);
        var driver = TestHelper.CreateDriver(trackSteps: true);

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var firstRun = driver.GetRunResult().Results.Single();
        Assert.Single(firstRun.GeneratedSources);
        Assert.Single(firstRun.Diagnostics); // GUAC0003 for NotPartial
        AssertAllStepsRan(firstRun);

        // Edit only the unrelated file.
        var otherTree = compilation.SyntaxTrees.Single(t => t.FilePath == "Test1.cs");
        var editedTree = otherTree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
            OtherSource.Replace("public int Value => 1;", "public int Value => 2;\n        public int Other => 3;")));
        var editedCompilation = compilation.ReplaceSyntaxTree(otherTree, editedTree);

        driver = driver.RunGenerators(editedCompilation, TestContext.Current.CancellationToken);
        var secondRun = driver.GetRunResult().Results.Single();

        AssertAllOutputsCachedOrUnchanged(secondRun);
        Assert.Single(secondRun.GeneratedSources);
        Assert.Single(secondRun.Diagnostics);
    }

    [Fact]
    public void AddingUnrelatedFile_TrackedStepOutputsAreCachedOrUnchanged()
    {
        var compilation = TestHelper.CreateCompilation(ContextSource);
        var driver = TestHelper.CreateDriver(trackSteps: true);

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        AssertAllStepsRan(driver.GetRunResult().Results.Single());

        var newTree = CSharpSyntaxTree.ParseText(OtherSource, TestHelper.DefaultParseOptions, path: "Added.cs", cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(newTree), TestContext.Current.CancellationToken);

        AssertAllOutputsCachedOrUnchanged(driver.GetRunResult().Results.Single());
    }

    [Fact]
    public void EditInsideContextBodyAfterProperties_ModelOutputIsCachedOrUnchanged()
    {
        // Adding a member that is not a provider re-runs the analysis but must produce an equal model,
        // so code generation is skipped.
        var compilation = TestHelper.CreateCompilation(ContextSource);
        var driver = TestHelper.CreateDriver(trackSteps: true);
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var tree = compilation.SyntaxTrees.Single();
        var edited = ContextSource.Replace(
            "public partial IncrementalValuesProvider<Person> People { get; }",
            "public partial IncrementalValuesProvider<Person> People { get; }\n\n            public int Unrelated() => 42;");
        var editedCompilation = compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(edited)));

        driver = driver.RunGenerators(editedCompilation, TestContext.Current.CancellationToken);
        var run = driver.GetRunResult().Results.Single();

        var modelOutputs = run.TrackedSteps[ComputingContextGenerator.ModelStepName].SelectMany(s => s.Outputs).ToList();
        Assert.NotEmpty(modelOutputs);
        Assert.All(modelOutputs, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Model step output was {output.Reason}"));
    }

    [Fact]
    public void TrackedStepOutputs_DoNotContainRoslynObjects()
    {
        var compilation = TestHelper.CreateCompilation(ContextSource, OtherSource);
        var driver = TestHelper.CreateDriver(trackSteps: true).RunGenerators(compilation, TestContext.Current.CancellationToken);
        var run = driver.GetRunResult().Results.Single();

        foreach (var stepName in TrackedSteps)
        {
            foreach (var output in run.TrackedSteps[stepName].SelectMany(s => s.Outputs))
            {
                AssertNoRoslynObjects(output.Value, $"{stepName} output", new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
        }
    }

    private static void AssertAllStepsRan(GeneratorRunResult run)
    {
        foreach (var stepName in TrackedSteps)
        {
            Assert.True(run.TrackedSteps.ContainsKey(stepName), $"Step '{stepName}' was not tracked");
            Assert.All(
                run.TrackedSteps[stepName].SelectMany(s => s.Outputs),
                output => Assert.Equal(IncrementalStepRunReason.New, output.Reason));
        }
    }

    private static void AssertAllOutputsCachedOrUnchanged(GeneratorRunResult run)
    {
        foreach (var stepName in TrackedSteps)
        {
            var outputs = run.TrackedSteps[stepName].SelectMany(s => s.Outputs).ToList();
            Assert.NotEmpty(outputs);
            Assert.All(outputs, output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step '{stepName}' output was {output.Reason}"));
        }

        // The source output step itself must not have re-run.
        var sourceOutputs = run.TrackedOutputSteps.SelectMany(kvp => kvp.Value).SelectMany(s => s.Outputs).ToList();
        Assert.All(sourceOutputs, output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    private static void AssertNoRoslynObjects(object? value, string path, HashSet<object> visited)
    {
        if (value is null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
            return;

        var type = value.GetType();
        Assert.False(
            value is ISymbol or SyntaxNode or SemanticModel or Compilation or Location or Diagnostic or SyntaxTree,
            $"{path} contains a Roslyn object of type {type}");

        if (!type.IsValueType && !visited.Add(value))
            return;

        // Roslyn value types used intentionally in the model (TextSpan, LinePositionSpan) are fine.
        if (type.Namespace?.StartsWith("Microsoft.CodeAnalysis.Text", StringComparison.Ordinal) == true)
            return;

        if (value is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                AssertNoRoslynObjects(item, $"{path}[{index++}]", visited);
            }

            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertNoRoslynObjects(field.GetValue(value), $"{path}.{field.Name}", visited);
        }
    }
}
