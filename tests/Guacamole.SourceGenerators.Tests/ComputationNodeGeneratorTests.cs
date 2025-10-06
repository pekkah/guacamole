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
    public Task Generator_Nodes_IntegrateWithComputationGraph()
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

                    public partial IncrementalValueProvider<int> Count { get; }

                    public partial IncrementalValuesProvider<string> Items { get; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
