using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class ComputationNodeGeneratorTests
{
    [Fact]
    public Task Generator_ForProperty_GeneratesPropertySpecificNode()
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
    public Task Generator_Nodes_IntegrateWithComputationGraph()
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
                    public partial IncrementalValueProvider<int> Count { get; set; }

                    [Input]
                    public partial IncrementalValuesProvider<string> Items { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
