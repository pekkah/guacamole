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
    public Task Generator_SaveChanges_HandlesMultipleProperties()
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

                    [Input]
                    public partial IncrementalValueProvider<int> Count { get; set; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
