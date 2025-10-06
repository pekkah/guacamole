using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class ComputingContextGeneratorTests
{
    [Fact]
    public Task Generator_WithGetOnlyProperty_GeneratesMutationMethods()
    {
        var source = """
            using Guacamole;
            using System;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base()
                    {
                        Initialize();
                    }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }

    [Fact]
    public Task Generator_WithPropertyWithSetter_DoesNotGenerateMutationMethods()
    {
        var source = """

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base()
                    {
                        Initialize();
                    }

                    public partial IncrementalValuesProvider<string> DerivedItems { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }

    [Fact]
    public Task Generator_WithNoProperties_GeneratesMinimalCode()
    {
        var source = """

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base()
                    {
                        Initialize();
                    }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
