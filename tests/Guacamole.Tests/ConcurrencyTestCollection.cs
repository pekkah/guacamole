using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Multi-threaded stress tests run in this collection, without other tests in parallel, so they
/// neither skew timing-sensitive tests nor are skewed by them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConcurrencyTestCollection
{
    public const string Name = "Concurrency";
}
