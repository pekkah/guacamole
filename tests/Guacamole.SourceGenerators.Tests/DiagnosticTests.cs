using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

/// <summary>
/// Tests that the generator and the analyzer report their GUAC diagnostics at the right source locations.
/// </summary>
public class DiagnosticTests
{
    [Fact]
    public void NonPartialGetOnlyProvider_ReportsGUAC0003AtProperty()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0003", DiagnosticSeverity.Error, source, "Items");
        Assert.Contains("'Items'", diagnostic.GetMessage());
        Assert.Contains("'TestContext'", diagnostic.GetMessage());
    }

    [Fact]
    public void PartialProviderWithSetter_ReportsGUAC0005AtProperty()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<string> DerivedItems { get; set; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0005", DiagnosticSeverity.Error, source, "DerivedItems");

        // The property is not implemented by the generator.
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.DoesNotContain("DerivedItems", generated);
    }

    [Fact]
    public void ItemTypeWithoutEquality_ReportsGUAC0004AtProperty()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                public class Person
                {
                    public string Name { get; set; } = "";
                }

                public record PersonRecord(string Name);

                public class EquatablePerson : System.IEquatable<EquatablePerson>
                {
                    public bool Equals(EquatablePerson? other) => other is not null;
                    public override bool Equals(object? obj) => Equals(obj as EquatablePerson);
                    public override int GetHashCode() => 0;
                }

                public enum Kind { A, B }

                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<Person> People { get; }
                    public partial IncrementalValuesProvider<PersonRecord> Records { get; }
                    public partial IncrementalValuesProvider<EquatablePerson> Equatables { get; }
                    public partial IncrementalValueProvider<Kind> CurrentKind { get; }
                    public partial IncrementalValueProvider<string> Name { get; }
                    public partial IncrementalValueProvider<int> Count { get; }
                    public IncrementalValueProvider<Person> Selected { get; set; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        // Only Person-typed properties (input and user-managed) are reported.
        Assert.Collection(
            diagnostics.OrderBy(d => d.Location.SourceSpan.Start),
            d => AssertDiagnostic(d, "GUAC0004", DiagnosticSeverity.Warning, source, "People"),
            d => AssertDiagnostic(d, "GUAC0004", DiagnosticSeverity.Warning, source, "Selected"));

        // Warnings do not prevent generation, and the generated code compiles.
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void ImmutableDictionaryWithValueEquality_DoesNotReportGUAC0004()
    {
        // Issue #100: the runtime compares immutable dictionaries entry by entry (whatever produced
        // them), so they have value equality when their value type has it, for every provider kind.
        var source = """
            using System.Collections.Immutable;
            using Guacamole;

            namespace Test
            {
                public record Symbol(string Name);

                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValueProvider<ImmutableDictionary<string, int>> Input { get; }
                    public IncrementalValueProvider<ImmutableDictionary<string, int>> Table { get; set; }
                    public IncrementalValuesProvider<ImmutableDictionary<string, int>> Tables { get; set; }
                    public IncrementalValueProvider<ImmutableDictionary<string, Symbol>> Symbols { get; set; }
                    public IncrementalValueProvider<ImmutableDictionary<string, string>> Strings { get; set; }
                    public IncrementalValueProvider<ImmutableDictionary<string, ImmutableArray<Symbol>>> Groups { get; set; }
                    public IncrementalValueProvider<ImmutableDictionary<string, ImmutableDictionary<int, Symbol>>> Nested { get; set; }
                    public IncrementalValueProvider<ImmutableSortedDictionary<string, Symbol>> Sorted { get; set; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void ImmutableDictionaryWithoutValueEquality_ReportsGUAC0004ForTheValueType()
    {
        // Issue #100: a dictionary whose values lack value equality still compares as changed on
        // every recomputation, whichever provider kind or operator produced it.
        var source = """
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using Guacamole;

            namespace Test
            {
                public class Plain
                {
                    public string Name { get; set; } = "";
                }

                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValueProvider<ImmutableDictionary<string, Plain>> Input { get; }
                    public IncrementalValueProvider<ImmutableDictionary<string, Plain>> Table { get; set; }
                    public IncrementalValuesProvider<ImmutableDictionary<string, Plain>> Tables { get; set; }
                    public IncrementalValueProvider<ImmutableDictionary<string, ImmutableDictionary<int, Plain>>> Nested { get; set; }
                    public IncrementalValueProvider<ImmutableSortedDictionary<string, Plain>> Sorted { get; set; }
                    public IncrementalValueProvider<Dictionary<string, int>> Mutable { get; set; }
                    public IncrementalValueProvider<ImmutableList<int>> List { get; set; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        Assert.Collection(
            diagnostics.OrderBy(d => d.Location.SourceSpan.Start),
            d => AssertValueType(d, "Input", "Test.Plain"),
            d => AssertValueType(d, "Table", "Test.Plain"),
            d => AssertValueType(d, "Tables", "Test.Plain"),
            d => AssertValueType(d, "Nested", "Test.Plain"),
            d => AssertValueType(d, "Sorted", "Test.Plain"),
            // Other collection types are unaffected: still reported for themselves
            d => AssertValueType(d, "Mutable", "System.Collections.Generic.Dictionary<string, int>"),
            d => AssertValueType(d, "List", "System.Collections.Immutable.ImmutableList<int>"));
        Assert.Empty(result.CompilationErrors);

        void AssertValueType(Diagnostic diagnostic, string property, string type)
        {
            AssertDiagnostic(diagnostic, "GUAC0004", DiagnosticSeverity.Warning, source, property);
            Assert.StartsWith($"Type '{type}' in property '{property}'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void NonPartialContext_ReportsGUAC0006AndGeneratesNothing()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public class TestContext : ComputingContextBase
                {
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0006", DiagnosticSeverity.Error, source, "TestContext");
        Assert.Empty(result.GeneratedSources);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void NonPartialContainingType_ReportsGUAC0006AtContainingType()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                public class Outer
                {
                    [ComputingContext]
                    public partial class TestContext : ComputingContextBase
                    {
                    }
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0006", DiagnosticSeverity.Error, source, "Outer");
        Assert.Contains("'TestContext'", diagnostic.GetMessage());
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ContextNotDerivingFromBase_ReportsGUAC0007AndGeneratesNothing()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext
                {
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0007", DiagnosticSeverity.Error, source, "TestContext");
        Assert.Empty(result.GeneratedSources);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void ValidContext_ReportsNoDiagnostics()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<string> Items { get; }
                    public IncrementalValueProvider<int> Derived { get; set; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(TestHelper.RunAnalyzer(source));
        TestHelper.AssertCompilesCleanly(result);
    }

    [Fact]
    public void UserParameterlessConstructor_ReportsGUAC0008AndOmitsGeneratedConstructor()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext()
                    {
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        AssertDiagnostic(diagnostic, "GUAC0008", DiagnosticSeverity.Error, source, "TestContext", occurrence: 1);
        Assert.Contains("'TestContext'", diagnostic.GetMessage());

        // The rest of the class is still generated, without a colliding constructor (no CS0111).
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.DoesNotContain("public TestContext()", generated);
        Assert.Contains("ItemsSource", generated);
        Assert.Empty(result.CompilationErrors);

        // Chains to ': this()' reach the user's constructor: no GUAC0009 on top of GUAC0008.
        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Fact]
    public void ConstructorChainingToThis_ReportsNoDiagnostics()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(int value) : this()
                    {
                        Value = value;
                    }

                    public int Value { get; }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(TestHelper.RunAnalyzer(source));
        TestHelper.AssertCompilesCleanly(result);
    }

    [Fact]
    public void ConstructorChainingToBase_ReportsGUAC0009AtConstructor()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(int value) : base()
                    {
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        var diagnostic = Assert.Single(diagnostics);
        AssertDiagnostic(diagnostic, "GUAC0009", DiagnosticSeverity.Warning, source, "TestContext", occurrence: 1);
        Assert.Equal("Constructor of computing context 'TestContext' must chain to ': this()' so generated sources are initialized", diagnostic.GetMessage());
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void ConstructorWithoutInitializer_ReportsGUAC0009AtConstructor()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    private TestContext(string name)
                    {
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        var diagnostic = Assert.Single(diagnostics);
        AssertDiagnostic(diagnostic, "GUAC0009", DiagnosticSeverity.Warning, source, "TestContext", occurrence: 1);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void ConstructorChainReachingThis_ReportsNoDiagnostics()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(string name) : this(1)
                    {
                    }

                    public TestContext(int value) : this()
                    {
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(TestHelper.RunAnalyzer(source));
        TestHelper.AssertCompilesCleanly(result);
    }

    [Fact]
    public void ConstructorChainNotReachingThis_ReportsGUAC0009OnEveryConstructorInChain()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(string name) : this(1)
                    {
                    }

                    public TestContext(int value) : base()
                    {
                    }

                    public TestContext(double value) : this()
                    {
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        // Reported by the analyzer (suppressible), not by the generator (#107).
        Assert.Empty(result.GeneratorDiagnostics);

        Assert.Collection(
            diagnostics.OrderBy(d => d.Location.SourceSpan.Start),
            d => AssertDiagnostic(d, "GUAC0009", DiagnosticSeverity.Warning, source, "TestContext", occurrence: 1),
            d => AssertDiagnostic(d, "GUAC0009", DiagnosticSeverity.Warning, source, "TestContext", occurrence: 2));
        Assert.Empty(result.CompilationErrors);
    }

    private static void AssertDiagnostic(Diagnostic diagnostic, string id, DiagnosticSeverity severity, string source, string expectedText, int occurrence)
    {
        AssertDiagnostic(diagnostic, id, severity, source, expectedText);

        // Verify it is the expected occurrence of the text (e.g. the n-th constructor, after the class name).
        var index = -1;
        for (var i = 0; i <= occurrence; i++)
            index = source.IndexOf(expectedText, index + 1, System.StringComparison.Ordinal);

        Assert.Equal(index, diagnostic.Location.SourceSpan.Start);
    }

    private static void AssertDiagnostic(Diagnostic diagnostic, string id, DiagnosticSeverity severity, string source, string expectedText)
    {
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(severity, diagnostic.Severity);

        var location = diagnostic.Location;
        Assert.NotEqual(Location.None, location);

        var lineSpan = location.GetLineSpan();
        Assert.Equal("Test0.cs", lineSpan.Path);
        Assert.Equal(expectedText, source.Substring(location.SourceSpan.Start, location.SourceSpan.Length));

        // The line span must agree with the text span.
        var lines = source.Split('\n');
        var line = lines[lineSpan.StartLinePosition.Line];
        Assert.Equal(expectedText, line.Substring(lineSpan.StartLinePosition.Character, expectedText.Length));
    }
}
