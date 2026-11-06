using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

/// <summary>
/// Snapshot tests for the generated code. Every test also asserts that the compilation including
/// the generated code has no errors or warnings.
/// </summary>
public class ComputingContextGeneratorTests
{
    [Fact]
    public Task Generator_ForCollectionProperty_GeneratesSourceBackedProvider()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_ForSingleValueProperty_GeneratesSourceBackedProvider()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValueProvider<int> Count { get; }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithMultipleProperties_GeneratesAllSources()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<string> Items { get; }

                    public partial IncrementalValueProvider<int> Count { get; }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithNoProperties_GeneratesConstructorOnly()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithUserManagedProperty_DoesNotImplementIt()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<string> Items { get; }

                    public IncrementalValuesProvider<string> Derived { get; set; }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithItemTypeFromAnotherNamespace_UsesFullyQualifiedNames()
    {
        var models = """
            namespace Models
            {
                public record Person(string Name);

                public enum Status { Active, Inactive }
            }
            """;

        var context = """
            using Guacamole;
            using Models;
            using System.Collections.Generic;

            namespace App.Contexts
            {
                [ComputingContext]
                public partial class PeopleContext : ComputingContextBase
                {
                    public partial IncrementalValuesProvider<Person> People { get; }

                    public partial IncrementalValueProvider<Status> CurrentStatus { get; }

                    public partial IncrementalValueProvider<string?> OptionalName { get; }

                    public partial IncrementalValueProvider<Dictionary<string, Person>> Lookup { get; }
                }
            }
            """;

        return TestHelper.Verify([models, context]);
    }

    [Fact]
    public Task Generator_WithNestedClass_EmitsContainingTypes()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                public static partial class Outer<T>
                {
                    internal partial record Middle
                    {
                        [ComputingContext]
                        private partial class NestedContext : ComputingContextBase
                        {
                            public partial IncrementalValuesProvider<T> Items { get; }
                        }
                    }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithGenericContext_PreservesTypeParameters()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                [ComputingContext]
                public partial class GenericContext<TItem> : ComputingContextBase
                    where TItem : notnull
                {
                    public partial IncrementalValuesProvider<TItem> Items { get; }
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_InGlobalNamespace_OmitsNamespaceDeclaration()
    {
        var source = """
            using Guacamole;

            [ComputingContext]
            public partial class GlobalContext : ComputingContextBase
            {
                public partial IncrementalValueProvider<int> Count { get; }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public Task Generator_WithNonPublicProperties_PreservesAccessibility()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                internal record Item(int Id);

                [ComputingContext]
                internal partial class TestContext : ComputingContextBase
                {
                    internal partial IncrementalValuesProvider<Item> Items { get; }

                    protected internal partial IncrementalValueProvider<int> Count { get; }

                    private partial IncrementalValueProvider<string> Name { get; }

                    public object CurrentName => Name;
                }
            }
            """;

        return TestHelper.Verify(source);
    }

    [Fact]
    public void Generator_WithSameClassNameInDifferentNamespaces_UsesDistinctHintNames()
    {
        var first = """
            using Guacamole;

            namespace First
            {
                [ComputingContext]
                public partial class AppContext : ComputingContextBase
                {
                    public partial IncrementalValueProvider<int> Count { get; }
                }
            }
            """;

        var second = """
            using Guacamole;

            namespace Second
            {
                [ComputingContext]
                public partial class AppContext : ComputingContextBase
                {
                    public partial IncrementalValueProvider<int> Count { get; }
                }

                public partial class Outer
                {
                    [ComputingContext]
                    public partial class AppContext : ComputingContextBase
                    {
                        public partial IncrementalValueProvider<int> Count { get; }
                    }
                }
            }
            """;

        var result = TestHelper.Run(first, second);

        TestHelper.AssertCompilesCleanly(result);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Equal(
            ["First.AppContext.g.cs", "Second.AppContext.g.cs", "Second.Outer+AppContext.g.cs"],
            result.GeneratedSources.Select(s => s.HintName).OrderBy(n => n, System.StringComparer.Ordinal));
    }

    [Fact]
    public void Generator_WithoutComputingContextAttribute_GeneratesNothing()
    {
        var source = """
            using Guacamole;

            namespace Test
            {
                public partial class TestContext : ComputingContextBase
                {
                }
            }
            """;

        var result = TestHelper.Run(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Empty(result.GeneratorDiagnostics);
    }
}
