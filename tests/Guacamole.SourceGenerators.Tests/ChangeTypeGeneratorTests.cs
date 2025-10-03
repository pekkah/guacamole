using System.Threading.Tasks;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public class ChangeTypeGeneratorTests
{
    [Fact]
    public Task Generator_ForCollectionProperty_GeneratesStronglyTypedChangeRecords()
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
    public Task Generator_ForSingleValueProperty_GeneratesStronglyTypedChangeRecords()
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
                    public TestContext(IServiceProvider services) : base()
                    {
                        Initialize();
                    }

                    public partial IncrementalValueProvider<int> Count { get; }
                }
            }
            """;

        return TestHelper.Verify<ComputingContextGenerator>(source);
    }
}
