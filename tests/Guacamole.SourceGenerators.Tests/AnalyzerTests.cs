using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

/// <summary>
/// Tests for <see cref="ComputingContextAnalyzer"/>: suppression of its diagnostics (#107) and
/// GUAC0010, collection members compared by reference by compiler-generated equality (#108).
/// </summary>
public class AnalyzerTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics.CodeAnalysis;
        using Guacamole;

        namespace Test;

        public class Plain
        {
            public string Name { get; set; } = "";
        }

        """;

    // #107: suppression

    [Fact]
    public void PragmaAroundProperty_SuppressesGUAC0004()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0004 // compared by identity on purpose
                public IncrementalValueProvider<Plain> Suppressed { get; set; }
            #pragma warning restore GUAC0004
                public IncrementalValueProvider<Plain> Reported { get; set; }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        Assert.Empty(result.GeneratorDiagnostics);
        var diagnostic = Assert.Single(diagnostics);
        AssertAt(diagnostic, "GUAC0004", source, "Reported");
    }

    [Fact]
    public void PragmaAroundInputProperty_SuppressesGUAC0004()
    {
        // The issue's example: a provider property in a [ComputingContext] class.
        var source = Header + """
            public sealed class GlobalImplContext;

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0004
                public partial IncrementalValueProvider<GlobalImplContext> GlobalImplContextInput { get; }

                public IncrementalValueProvider<GlobalImplContext> GlobalImplContextProvider { get; set; }
            #pragma warning restore GUAC0004
            }
            """;

        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Fact]
    public void SuppressMessageOnProperty_SuppressesGUAC0004()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                [SuppressMessage("Guacamole", "GUAC0004", Justification = "Compared by identity on purpose")]
                public IncrementalValueProvider<Plain> Suppressed { get; set; }

                public IncrementalValueProvider<Plain> Reported { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        AssertAt(diagnostic, "GUAC0004", source, "Reported");
    }

    [Fact]
    public void SuppressMessageOnClass_SuppressesGUAC0004ForAllProperties()
    {
        var source = Header + """
            [ComputingContext]
            [SuppressMessage("Guacamole.Performance", "GUAC0004:Type does not implement proper equality for incremental computation")]
            public partial class TestContext : ComputingContextBase
            {
                public partial IncrementalValueProvider<Plain> Input { get; }

                public IncrementalValueProvider<Plain> Derived { get; set; }
            }
            """;

        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("silent", null)]
    [InlineData("suggestion", DiagnosticSeverity.Info)]
    [InlineData("error", DiagnosticSeverity.Error)]
    public void EditorConfigSeverity_AppliesToGUAC0004(string configured, DiagnosticSeverity? expected)
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Plain> Derived { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source, $"""
            root = true

            [*.cs]
            dotnet_diagnostic.GUAC0004.severity = {configured}
            """);

        if (expected is null)
        {
            Assert.DoesNotContain(diagnostics, d => d.Severity != DiagnosticSeverity.Hidden);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("GUAC0004", diagnostic.Id);
            Assert.Equal(expected, diagnostic.Severity);
        }
    }

    [Fact]
    public void PragmaAroundConstructor_SuppressesGUAC0009()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0009
                public TestContext(int value) : base()
                {
                }
            #pragma warning restore GUAC0009

                public TestContext(string value) : base()
                {
                }

                public partial IncrementalValuesProvider<string> Items { get; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        Assert.Equal("GUAC0009", diagnostic.Id);
        Assert.Contains("TestContext(string value)", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line].ToString());
    }

    [Fact]
    public void PragmaAndSuppressMessage_SuppressGUAC0010()
    {
        var source = Header + """
            public record Module(string Name, ImmutableArray<string> Members);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0010
                public IncrementalValueProvider<Module> ByPragma { get; set; }
            #pragma warning restore GUAC0010

                [SuppressMessage("Guacamole", "GUAC0010")]
                public IncrementalValueProvider<Module> ByAttribute { get; set; }

                public IncrementalValueProvider<Module> Reported { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        AssertAt(diagnostic, "GUAC0010", source, "Reported");
    }

    // #108: GUAC0010

    [Fact]
    public void RecordWithImmutableArrayMember_ReportsGUAC0010NamingTheMember()
    {
        var source = Header + """
            public record Item(string Name);

            public record Module(string Name, ImmutableArray<Item> Items);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public partial IncrementalValuesProvider<Module> Modules { get; }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        Assert.Empty(result.GeneratorDiagnostics);
        var diagnostic = Assert.Single(diagnostics);
        AssertAt(diagnostic, "GUAC0010", source, "Modules");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.StartsWith("Property 'Modules': 'Test.Module' relies on compiler-generated equality, which compares 'Module.Items' (ImmutableArray<Item>) by reference", message);
        Assert.Contains("EquatableArray<T>", message);
    }

    [Fact]
    public void RecordWithCollectionMembers_ReportsEveryCollectionMember()
    {
        var source = Header + """
            public record Module(
                string Name,
                int Count,
                ImmutableArray<int> Array,
                List<int> List,
                int[] Raw,
                Dictionary<string, int> Map,
                HashSet<int> Set,
                IEnumerable<int> Sequence,
                IReadOnlyList<int> ReadOnly,
                ImmutableDictionary<string, int> Table,
                ImmutableList<int> ImmutableList);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Module> Module { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'Module.Array' (ImmutableArray<int>), 'Module.List' (List<int>), 'Module.Raw' (int[]), 'Module.Map' (Dictionary<string, int>), 'Module.Set' (HashSet<int>) and 4 more by reference", message);
        Assert.DoesNotContain("Module.Name", message);
        Assert.DoesNotContain("Module.Count", message);
    }

    [Fact]
    public void FieldsAndPropertiesOfRecords_AreCheckedButComputedPropertiesAreNot()
    {
        var source = Header + """
            public record Module(string Name)
            {
                private readonly int[] _raw = [];

                public List<int> Values { get; init; } = [];

                public IReadOnlyList<int> Computed => Values;

                public int Length => _raw.Length;
            }

            public readonly record struct Pair(ImmutableArray<string> Left, string Right);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Module> Module { get; set; }

                public IncrementalValuesProvider<Pair> Pairs { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source).OrderBy(d => d.Location.SourceSpan.Start).ToList();

        Assert.Collection(
            diagnostics,
            d =>
            {
                AssertAt(d, "GUAC0010", source, "Module");
                var message = d.GetMessage(CultureInfo.InvariantCulture);
                Assert.Contains("'Module._raw' (int[]), 'Module.Values' (List<int>) by reference", message);
                Assert.DoesNotContain("Computed", message);
            },
            d =>
            {
                AssertAt(d, "GUAC0010", source, "Pairs");
                Assert.Contains("'Pair.Left' (ImmutableArray<string>) by reference", d.GetMessage(CultureInfo.InvariantCulture));
            });
    }

    [Fact]
    public void NestedRecordsTuplesAndBaseRecords_AreFollowed()
    {
        var source = Header + """
            public record Inner(string Name, ImmutableArray<string> Tags);

            public record Outer(Inner Inner, (string Key, List<int> Values) Pair, Inner? Optional);

            public record Base(ImmutableArray<int> BaseItems);

            public record Derived(string Name) : Base(ImmutableArray<int>.Empty);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Outer> Outer { get; set; }

                public IncrementalValueProvider<Derived> Derived { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source).OrderBy(d => d.Location.SourceSpan.Start).ToList();

        Assert.Collection(
            diagnostics,
            d => Assert.Contains(
                "'Outer.Inner.Tags' (ImmutableArray<string>), 'Outer.Pair.Values' (List<int>), 'Outer.Optional.Tags' (ImmutableArray<string>) by reference",
                d.GetMessage(CultureInfo.InvariantCulture)),
            d => Assert.Contains("'Derived.BaseItems' (ImmutableArray<int>) by reference", d.GetMessage(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void RecursiveRecords_AreAnalysedOnce()
    {
        var source = Header + """
            public record Node(string Name, Node? Parent);

            public record Tree(string Name, ImmutableArray<Tree> Children);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Node> Node { get; set; }

                public IncrementalValueProvider<Tree> Tree { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        AssertAt(diagnostic, "GUAC0010", source, "Tree");
        Assert.Contains("'Tree.Children' (ImmutableArray<Tree>) by reference", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ValueEqualMembers_AreNotReported()
    {
        var source = Header + """
            public record Item(string Name);

            public enum Kind { A, B }

            // EquatableArray<T> compares element by element.
            public record WithEquatableArray(string Name, EquatableArray<Item> Items, EquatableArray<EquatableArray<int>> Nested, Kind Kind, int? Count);

            // A user-written Equals replaces the compiler-generated one.
            public record WithCustomEquals(ImmutableArray<int> Values)
            {
                public virtual bool Equals(WithCustomEquals? other) =>
                    other is not null && Values.AsSpan().SequenceEqual(other.Values.AsSpan());

                public override int GetHashCode() => Values.Length;
            }

            public readonly record struct StructWithCustomEquals(int[] Values)
            {
                public bool Equals(StructWithCustomEquals other) => Values.AsSpan().SequenceEqual(other.Values);

                public override int GetHashCode() => Values.Length;
            }

            // Members of class types without value equality may be compared by identity on purpose.
            public record WithIdentityMember(string Name, Plain Owner);

            public sealed class EquatableList : System.IEquatable<EquatableList>, IEnumerable<int>
            {
                public bool Equals(EquatableList? other) => other is not null;
                public override bool Equals(object? obj) => Equals(obj as EquatableList);
                public override int GetHashCode() => 0;
                public IEnumerator<int> GetEnumerator() { yield break; }
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public record WithEquatableCollection(EquatableList Values);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<WithEquatableArray> A { get; set; }
                public IncrementalValueProvider<WithCustomEquals> B { get; set; }
                public IncrementalValueProvider<StructWithCustomEquals> C { get; set; }
                public IncrementalValueProvider<WithIdentityMember> D { get; set; }
                public IncrementalValueProvider<WithEquatableCollection> E { get; set; }
                public IncrementalValueProvider<EquatableArray<Item>> F { get; set; }
                public IncrementalValueProvider<ImmutableArray<Item>> G { get; set; }
                public IncrementalValueProvider<(ImmutableArray<Item>, string)> H { get; set; }
                public IncrementalValueProvider<ImmutableDictionary<string, ImmutableArray<Item>>> I { get; set; }
            }
            """;

        var result = TestHelper.Run(source);
        Assert.Empty(result.CompilationErrors);
        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Fact]
    public void CollectionsOfRecords_AreFollowedIntoTheElements()
    {
        // The runtime compares top-level immutable arrays, dictionaries and EquatableArray element by
        // element, with the element type's equality: a record element still compares its
        // ImmutableArray member by reference.
        var source = Header + """
            public record Module(string Name, ImmutableArray<string> Members);

            public record Group(EquatableArray<Module> Modules);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<ImmutableArray<Module>> All { get; set; }
                public IncrementalValueProvider<EquatableArray<Module>> Equatable { get; set; }
                public IncrementalValueProvider<ImmutableDictionary<string, Module>> ByName { get; set; }
                public IncrementalValueProvider<Group> Group { get; set; }
                public IncrementalValueProvider<(Module, int)> Pair { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source).OrderBy(d => d.Location.SourceSpan.Start).ToList();

        Assert.Collection(
            diagnostics,
            d => AssertMember(d, "All", "'Module.Members' (ImmutableArray<string>)"),
            d => AssertMember(d, "Equatable", "'Module.Members' (ImmutableArray<string>)"),
            d => AssertMember(d, "ByName", "'Module.Members' (ImmutableArray<string>)"),
            d => AssertMember(d, "Group", "'Group.Modules[].Members' (ImmutableArray<string>)"),
            d => AssertMember(d, "Pair", "'Item1.Members' (ImmutableArray<string>)"));

        void AssertMember(Diagnostic diagnostic, string property, string member)
        {
            AssertAt(diagnostic, "GUAC0010", source, property);
            Assert.Contains($"compares {member} by reference", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void ImmutableArrayOfTypeWithoutEquality_ReportsGUAC0004ForTheElementType()
    {
        // The runtime compares the immutable array element by element, so the element type is the
        // one that needs value equality.
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<ImmutableArray<Plain>> Items { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        AssertAt(diagnostic, "GUAC0004", source, "Items");
        Assert.StartsWith("Type 'Test.Plain' in property 'Items'", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RecordWithoutEquality_AndCollectionMember_ReportsBoth()
    {
        var source = Header + """
            public record Module(ImmutableArray<int> Values);

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<(Plain, Module)> Pair { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source);

        Assert.Equal(["GUAC0004", "GUAC0010"], diagnostics.Select(d => d.Id).Order().ToArray());
    }

    [Fact]
    public async Task RecordTypesFromAnotherAssembly_AreChecked()
    {
        // Private backing fields of referenced assemblies are not visible; the analyzer falls back
        // to their init-only properties.
        var library = TestHelper.CreateCompilation("""
            using System.Collections.Immutable;

            namespace Library;

            public record Module(string Name, ImmutableArray<string> Members);
            """).WithAssemblyName("Library");
        using var stream = new System.IO.MemoryStream();
        Assert.True(library.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());

        var source = """
            using Guacamole;

            namespace Test;

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Library.Module> Module { get; set; }
            }
            """;

        var compilation = TestHelper.CreateCompilation(source).AddReferences(reference);
        TestHelper.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);
        var diagnostics = await output.WithAnalyzers([new ComputingContextAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("GUAC0010", diagnostic.Id);
        Assert.Contains("'Module.Members' (ImmutableArray<string>) by reference", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static void AssertAt(Diagnostic diagnostic, string id, string source, string propertyName)
    {
        Assert.Equal(id, diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(propertyName, source.Substring(span.Start, span.Length));
    }
}
