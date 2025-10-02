using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class ComputingContextGeneratorTests
{
    [Fact]
    public Task Generator_WithPropertyWithSetter_GeneratesMutationMethods()
    {
        var source = """
            using Guacamole;
            using Guacamole.Core.Providers;
            using System;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base(services) { }

                    public partial IncrementalValuesProvider<string> Items { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }

    [Fact]
    public Task Generator_WithGetOnlyProperty_DoesNotGenerateMutationMethods()
    {
        var source = """
            using Guacamole;
            using Guacamole.Core.Providers;
            using System;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base(services) { }

                    public partial IncrementalValuesProvider<string> DerivedItems { get; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }

    [Fact]
    public Task Generator_WithNoProperties_GeneratesMinimalCode()
    {
        var source = """
            using Guacamole;
            using System;

            namespace Test
            {
                [ComputingContext]
                public partial class TestContext : ComputingContextBase
                {
                    public TestContext(IServiceProvider services) : base(services) { }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
