using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class PropertyGeneratorTests
{
    [Fact]
    public Task Generator_ForProperty_ImplementsProviderProperty()
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

                    [Input]
                    public partial IncrementalValuesProvider<string> Items { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }

    [Fact]
    public Task Generator_Property_ConnectsToComputationNode()
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

                    [Input]
                    public partial IncrementalValueProvider<string> Name { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
