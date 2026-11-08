using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #100: stages whose results are <see cref="ImmutableDictionary{TKey, TValue}"/> or
/// <see cref="ImmutableSortedDictionary{TKey, TValue}"/> compare them entry by entry by default,
/// whatever operator built them, so a stage that rebuilds an equal dictionary keeps its version and
/// the stages downstream of it stay cached.
/// </summary>
public partial class ImmutableDictionaryResultEqualityTests
{
    public sealed record Declaration(string Name, string Path, string Comment);

    [ComputingContext]
    private partial class DeclarationsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Declaration> Declarations { get; }
        public partial IncrementalValueProvider<ImmutableDictionary<string, string>> Settings { get; }
    }

    private sealed class Counter
    {
        public int Runs { get; private set; }

        public string Describe<TDictionary>(TDictionary table)
            where TDictionary : System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>
        {
            Runs++;
            return string.Join(";", table.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}={e.Value}"));
        }
    }

    [Fact]
    public void CollectSelectToImmutableDictionary_EqualRebuild_CutsOffDownstream()
    {
        using var context = new DeclarationsContext();
        var counter = new Counter();
        var table = context.Declarations.Collect()
            .Select(all => all.ToImmutableDictionary(d => d.Name, d => d.Path, StringComparer.Ordinal));
        var described = table.Select(t => counter.Describe(t));

        var a = new Declaration("A", "a.cs", "first");
        context.DeclarationsSource.Push(a);
        context.DeclarationsSource.Push(new Declaration("B", "b.cs", ""));
        Assert.Equal("A=a.cs;B=b.cs", described.Value);
        var version = context.Graph.Versions.GetOutputVersion(table.Node.Id);

        // The comment is not part of the table: the dictionary is rebuilt, equal entry by entry
        context.DeclarationsSource.Replace(a, a with { Comment = "edited" });

        Assert.Equal("A=a.cs;B=b.cs", described.Value);
        Assert.Equal(1, counter.Runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(table.Node.Id));
    }

    [Fact]
    public void CollectSelectToImmutableDictionary_ChangedEntry_Propagates()
    {
        using var context = new DeclarationsContext();
        var counter = new Counter();
        var described = context.Declarations.Collect()
            .Select(all => all.ToImmutableDictionary(d => d.Name, d => d.Path))
            .Select(t => counter.Describe(t));

        var a = new Declaration("A", "a.cs", "");
        context.DeclarationsSource.Push(a);
        Assert.Equal("A=a.cs", described.Value);

        var moved = a with { Path = "moved.cs" };
        context.DeclarationsSource.Replace(a, moved);
        Assert.Equal("A=moved.cs", described.Value);
        Assert.Equal(2, counter.Runs);

        context.DeclarationsSource.Push(new Declaration("B", "b.cs", ""));
        Assert.Equal("A=moved.cs;B=b.cs", described.Value);
        Assert.Equal(3, counter.Runs);
    }

    [Fact]
    public void ImmutableSortedDictionaryStage_EqualRebuild_CutsOffDownstream()
    {
        using var context = new DeclarationsContext();
        var counter = new Counter();
        var described = context.Declarations.Collect()
            .Select(all => all.ToImmutableSortedDictionary(d => d.Name, d => d.Path, StringComparer.Ordinal))
            .Select(t => counter.Describe(t));

        var a = new Declaration("A", "a.cs", "");
        context.DeclarationsSource.Push(a);
        Assert.Equal("A=a.cs", described.Value);

        context.DeclarationsSource.Replace(a, a with { Comment = "edited" });
        Assert.Equal("A=a.cs", described.Value);
        Assert.Equal(1, counter.Runs);
    }

    [Fact]
    public void ValueSource_SettingAnEqualDictionary_IsNotAChange()
    {
        using var context = new DeclarationsContext();
        var counter = new Counter();
        var described = context.Settings.Select(s => counter.Describe(s));

        context.SettingsSource.Set(ImmutableDictionary<string, string>.Empty.Add("x", "1").Add("y", "2"));
        Assert.Equal("x=1;y=2", described.Value);
        var version = context.Graph.Versions.GetOutputVersion(context.Settings.Node.Id);

        context.SettingsSource.Set(ImmutableDictionary<string, string>.Empty.Add("y", "2").Add("x", "1"));
        Assert.Equal("x=1;y=2", described.Value);
        Assert.Equal(1, counter.Runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(context.Settings.Node.Id));

        context.SettingsSource.Set(ImmutableDictionary<string, string>.Empty.Add("x", "1"));
        Assert.Equal("x=1", described.Value);
        Assert.Equal(2, counter.Runs);
    }
}
