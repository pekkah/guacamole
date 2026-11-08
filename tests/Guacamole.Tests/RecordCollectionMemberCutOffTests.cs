using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #108: a record whose collection member is an <see cref="EquatableArray{T}"/> is equal when
/// recomputed with the same elements, so the equality cut-off keeps downstream stages cached; with an
/// <see cref="ImmutableArray{T}"/> member, the compiler-generated Equals compares the member by
/// reference, so every recomputation counts as a change (and the analyzer reports GUAC0010).
/// </summary>
public partial class RecordCollectionMemberCutOffTests
{
    private sealed record EquatableModule(string Name, EquatableArray<string> Members);

    private sealed record ReferenceModule(string Name, ImmutableArray<string> Members);

    [ComputingContext]
    private partial class ModuleContext : ComputingContextBase
    {
        public ModuleContext(int unused) : this()
        {
            // "m: a, b" -> name "m", members ["a", "b"]; surrounding whitespace does not matter.
            Equatable = Text.Select(text => new EquatableModule(ParseName(text), ParseMembers(text)));
            ByReference = Text.Select(text => new ReferenceModule(ParseName(text), ParseMembers(text)));
        }

        public partial IncrementalValueProvider<string> Text { get; }

        public IncrementalValueProvider<EquatableModule> Equatable { get; set; }

        // Deliberately compared by reference to show the difference; without this suppression the
        // build fails with GUAC0010 (warnings are errors here).
#pragma warning disable GUAC0010
        public IncrementalValueProvider<ReferenceModule> ByReference { get; set; }
#pragma warning restore GUAC0010

        private static string ParseName(string text) => text.Split(':')[0].Trim();

        private static ImmutableArray<string> ParseMembers(string text) =>
            [.. text.Split(':')[1].Split(',').Select(m => m.Trim())];
    }

    [Fact]
    public void EquatableArrayMember_EqualRecomputation_CutsOffDownstream()
    {
        using var context = new ModuleContext(0);
        var runs = 0;
        var memberCount = context.Equatable.Select(m => { runs++; return m.Members.Count; });

        context.TextSource.Set("m: a, b");
        Assert.Equal(2, memberCount.Value);

        // Recomputes an equal record (a new array with the same members): downstream stays cached.
        context.TextSource.Set("  m:a,b  ");
        Assert.Equal(2, memberCount.Value);
        Assert.Equal(1, runs);

        // A different member list is a change.
        context.TextSource.Set("m: a, b, c");
        Assert.Equal(3, memberCount.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ImmutableArrayMember_EqualRecomputation_StillRecomputesDownstream()
    {
        using var context = new ModuleContext(0);
        var runs = 0;
        var memberCount = context.ByReference.Select(m => { runs++; return m.Members.Length; });

        context.TextSource.Set("m: a, b");
        Assert.Equal(2, memberCount.Value);

        // Same members, but the record's Equals compares the new ImmutableArray by reference.
        context.TextSource.Set("  m:a,b  ");
        Assert.Equal(2, memberCount.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void EquatableArrayMember_EqualRecomputation_DoesNotFireOutputHandlers()
    {
        using var context = new ModuleContext(0);
        var equatableCalls = 0;
        var referenceCalls = 0;
        using var equatable = context.RegisterOutput(context.Equatable, (_, _) => equatableCalls++);
        using var byReference = context.RegisterOutput(context.ByReference, (_, _) => referenceCalls++);

        context.TextSource.Set("m: a, b");
        var afterFirst = (equatableCalls, referenceCalls);

        context.TextSource.Set("  m:a,b  ");

        Assert.Equal(afterFirst.equatableCalls, equatableCalls);
        Assert.Equal(afterFirst.referenceCalls + 1, referenceCalls);
    }
}
