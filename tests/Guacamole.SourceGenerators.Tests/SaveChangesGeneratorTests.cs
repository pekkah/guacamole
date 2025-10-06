using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class SaveChangesGeneratorTests
{
    [Fact]
    public Task Generator_SaveChanges_GeneratesOptimizedCommitLogic()
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
    public Task Generator_SaveChanges_HandlesMultipleProperties()
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

                    public partial IncrementalValuesProvider<string> Items { get; }

                    public partial IncrementalValueProvider<int> Count { get; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
