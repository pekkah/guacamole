using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Xunit;

namespace Guacamole.Tests.Regressions;

/// <summary>
/// Regression tests for issue #25: "Support on-demand/lazy dependency resolution for cross-file
/// symbol lookup".
/// </summary>
/// <remarks>
/// <para>
/// The Fugu compiler type-checked each file in a collection <c>Select</c> whose lambda also added
/// the file's declarations to a shared, mutable symbol builder. A file could therefore only see the
/// symbols of files processed <em>before</em> it, so a reference from <c>main</c> to a type declared
/// in <c>std</c> resolved to <c>ErrorType</c> whenever <c>main</c> happened to be processed first.
/// Collection sources used to enumerate in hash order, and string hashes are randomized per
/// process, so the failure was intermittent. Sources now enumerate in insertion order, which makes
/// the anti-pattern deterministic, but its result still depends on the order files are pushed.
/// </para>
/// <para>
/// The supported pattern makes type checking a pure function of (file, symbol table): extract each
/// file's declarations, <c>Collect()</c> them, merge them deterministically into a table with
/// structural equality, and <c>Combine</c> every file with that table.
/// </para>
/// </remarks>
public partial class Issue25CrossFileSymbolLookupTests
{
    public const string ErrorType = "ErrorType";

    public sealed record SourceFile(string Path, string Text);

    /// <summary>Lines are <c>decl Name</c>, <c>use Name</c> or <c>// comment</c>.</summary>
    public sealed record ParsedFile(string Path, ValueArray<string> Declarations, ValueArray<string> Uses, ValueArray<string> Comments);

    public sealed record FileDeclarations(string Path, ValueArray<string> Names);

    /// <summary>A resolved reference: the name and the path that declares it, or <see cref="ErrorType"/>.</summary>
    public sealed record TypeCheckedFile(string Path, ValueArray<string> Resolved);

    /// <summary>Symbol name to declaring file path, with structural equality.</summary>
    public sealed class SymbolTable : IEquatable<SymbolTable>
    {
        public static readonly SymbolTable Empty = new(ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal));

        public SymbolTable(ImmutableSortedDictionary<string, string> symbols) => Symbols = symbols;

        public ImmutableSortedDictionary<string, string> Symbols { get; }

        /// <summary>
        /// Merges per-file declarations in path order (ordinal), so the result does not depend on
        /// the order the files arrive in. The first declaration of a name wins.
        /// </summary>
        public static SymbolTable Merge(ImmutableArray<FileDeclarations> files)
        {
            var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                foreach (var name in file.Names)
                {
                    builder.TryAdd(name, file.Path);
                }
            }

            return new SymbolTable(builder.ToImmutable());
        }

        /// <summary>Narrows the table to the given names (only what a file references).</summary>
        public SymbolTable Restrict(IEnumerable<string> names)
        {
            var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (Symbols.TryGetValue(name, out var path))
                    builder[name] = path;
            }

            return new SymbolTable(builder.ToImmutable());
        }

        public bool TryResolve(string name, out string path) => Symbols.TryGetValue(name, out path!);

        public bool Equals(SymbolTable? other) =>
            other is not null && Symbols.Count == other.Symbols.Count && Symbols.SequenceEqual(other.Symbols);

        public override bool Equals(object? obj) => Equals(obj as SymbolTable);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var (name, path) in Symbols)
            {
                hash.Add(name);
                hash.Add(path);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>The anti-pattern: a builder that accumulates symbols as a side effect.</summary>
    private sealed class SymbolBuilder
    {
        private readonly Dictionary<string, string> _symbols = new();

        public void Declare(ParsedFile file)
        {
            foreach (var name in file.Declarations)
                _symbols.TryAdd(name, file.Path);
        }

        public SymbolTable Snapshot() =>
            new(_symbols.ToImmutableSortedDictionary(StringComparer.Ordinal, StringComparer.Ordinal));
    }

    [ComputingContext]
    private partial class FuguContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<SourceFile> Files { get; }
    }

    private const string StdText = "decl List\ndecl Option";
    private const string MainText = "decl Main\nuse List\nuse Option";

    public static ParsedFile Parse(SourceFile file)
    {
        var declarations = new List<string>();
        var uses = new List<string>();
        var comments = new List<string>();
        foreach (var line in file.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("//", StringComparison.Ordinal))
                comments.Add(line);
            else if (line.StartsWith("decl ", StringComparison.Ordinal))
                declarations.Add(line["decl ".Length..]);
            else if (line.StartsWith("use ", StringComparison.Ordinal))
                uses.Add(line["use ".Length..]);
        }

        return new ParsedFile(file.Path, new(declarations), new(uses), new(comments));
    }

    public static FileDeclarations ExtractDeclarations(ParsedFile file) => new(file.Path, file.Declarations);

    public static TypeCheckedFile TypeCheck(ParsedFile file, SymbolTable table) =>
        new(file.Path, new(file.Uses.Select(name => table.TryResolve(name, out var path) ? $"{name}:{path}" : $"{name}:{ErrorType}")));

    public static TheoryData<bool> StdFirst => new() { true, false };

    /// <summary>The files in push (and therefore processing) order.</summary>
    private static SourceFile[] Files(bool stdFirst) => stdFirst
        ? [new("std.fugu", StdText), new("main.fugu", MainText)]
        : [new("main.fugu", MainText), new("std.fugu", StdText)];

    [Fact]
    public void SideEffectingSymbolBuilder_ResultsDependOnPushOrder()
    {
        TypeCheckedFile MainResult(bool stdFirst)
        {
            using var context = new FuguContext();
            var builder = new SymbolBuilder();

            // Anti-pattern: declarations are registered while type checking, so a file only sees
            // the symbols of files that were processed before it.
            var typeChecked = context.Files.Select(Parse).Select(file =>
            {
                builder.Declare(file);
                return TypeCheck(file, builder.Snapshot());
            });

            context.FilesSource.PushRange(Files(stdFirst));
            return typeChecked.Values.Single(f => f.Path == "main.fugu");
        }

        var stdFirst = MainResult(stdFirst: true);
        var mainFirst = MainResult(stdFirst: false);

        Assert.Equal(["List:std.fugu", "Option:std.fugu"], stdFirst.Resolved);
        Assert.Equal([$"List:{ErrorType}", $"Option:{ErrorType}"], mainFirst.Resolved);
    }

    /// <summary>The recommended pipeline: type checking is a pure function of (file, table).</summary>
    private sealed class Pipeline
    {
        private int _typeChecks;
        private int _merges;

        public Pipeline(FuguContext context)
        {
            Parsed = context.Files.Select(Parse);
            var declarations = Parsed.Select(ExtractDeclarations);
            Table = declarations.Collect().Select(files =>
            {
                Interlocked.Increment(ref _merges);
                return SymbolTable.Merge(files);
            });
            TypeChecked = Parsed.Combine(Table).Select(pair =>
            {
                Interlocked.Increment(ref _typeChecks);
                return TypeCheck(pair.Left, pair.Right);
            });
        }

        public IncrementalValuesProvider<ParsedFile> Parsed { get; }
        public IncrementalValueProvider<SymbolTable> Table { get; }
        public IncrementalValuesProvider<TypeCheckedFile> TypeChecked { get; }

        public int TypeChecks => Volatile.Read(ref _typeChecks);
        public int Merges => Volatile.Read(ref _merges);

        public Dictionary<string, TypeCheckedFile> Results() => TypeChecked.Values.ToDictionary(f => f.Path);
    }

    [Theory]
    [MemberData(nameof(StdFirst))]
    public void CollectMergeCombine_ResolvesCrossFileReferences_InEveryOrder(bool stdFirst)
    {
        using var context = new FuguContext();
        var pipeline = new Pipeline(context);

        context.FilesSource.PushRange(Files(stdFirst));

        var results = pipeline.Results();
        Assert.Equal(["List:std.fugu", "Option:std.fugu"], results["main.fugu"].Resolved);
        Assert.Empty(results["std.fugu"].Resolved);
    }

    [Theory]
    [MemberData(nameof(StdFirst))]
    public void CollectMergeCombine_ReadBetweenPushes_ResolvesOnceDeclarationsArrive(bool stdFirst)
    {
        using var context = new FuguContext();
        var pipeline = new Pipeline(context);

        foreach (var file in Files(stdFirst))
        {
            context.FilesSource.Push(file);
            _ = pipeline.Results();
        }

        Assert.Equal(["List:std.fugu", "Option:std.fugu"], pipeline.Results()["main.fugu"].Resolved);
    }

    [Fact]
    public void ReplacingStd_ReResolvesMain()
    {
        using var context = new FuguContext();
        var pipeline = new Pipeline(context);
        var files = Files(stdFirst: false);
        var std = files.Single(f => f.Path == "std.fugu");
        context.FilesSource.PushRange(files);

        Assert.Equal(["List:std.fugu", "Option:std.fugu"], pipeline.Results()["main.fugu"].Resolved);

        // Drop Option from std: main's reference to it no longer resolves.
        var stdWithoutOption = std with { Text = "decl List" };
        context.FilesSource.Replace(std, stdWithoutOption);
        Assert.Equal(["List:std.fugu", $"Option:{ErrorType}"], pipeline.Results()["main.fugu"].Resolved);

        // Restore it.
        context.FilesSource.Replace(stdWithoutOption, std);
        Assert.Equal(["List:std.fugu", "Option:std.fugu"], pipeline.Results()["main.fugu"].Resolved);
    }

    [Fact]
    public void CommentOnlyEdit_KeepsSymbolTableVersion()
    {
        using var context = new FuguContext();
        var pipeline = new Pipeline(context);
        var files = Files(stdFirst: true);
        var std = files.Single(f => f.Path == "std.fugu");
        context.FilesSource.PushRange(files);

        // Read the table so it is cached and versioned, then the type checks built from it.
        Assert.True(pipeline.Table.GetValue().IsSuccess);
        _ = pipeline.Results();
        var tableVersion = context.Graph.Versions.GetOutputVersion(pipeline.Table.Node.Id);
        var mergesBefore = pipeline.Merges;
        var typeChecksBefore = pipeline.TypeChecks;

        // A comment does not change std's declarations.
        context.FilesSource.Replace(std, std with { Text = "// the standard library\n" + StdText });

        var table = pipeline.Table.GetValue();
        Assert.True(table.IsSuccess);
        Assert.Equal(mergesBefore + 1, pipeline.Merges);
        Assert.Equal(tableVersion, context.Graph.Versions.GetOutputVersion(pipeline.Table.Node.Id));

        // Only std (whose parsed file changed) is type checked again; main hits the per-item memo
        // because neither its parsed file nor the table changed.
        Assert.Equal(["List:std.fugu", "Option:std.fugu"], pipeline.Results()["main.fugu"].Resolved);
        Assert.Equal(typeChecksBefore + 1, pipeline.TypeChecks);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void UnrelatedDeclarationChange_WithRestrictedTable_SkipsUnaffectedFiles(bool restrict, int expectedTypeChecks)
    {
        using var context = new FuguContext();
        var typeChecks = 0;
        var parsed = context.Files.Select(Parse);
        var table = parsed.Select(ExtractDeclarations).Collect().Select(SymbolTable.Merge);
        var pairs = parsed.Combine(table);
        var inputs = restrict
            // Narrow the table to the names each file references: the pair stays equal when
            // only unrelated declarations change.
            ? pairs.Select(pair => (pair.Left, pair.Right.Restrict(pair.Left.Uses)))
            : pairs;
        var typeChecked = inputs.Select(pair =>
        {
            Interlocked.Increment(ref typeChecks);
            return TypeCheck(pair.Item1, pair.Item2);
        });

        var files = Files(stdFirst: true);
        var std = files.Single(f => f.Path == "std.fugu");
        context.FilesSource.PushRange(files);
        _ = typeChecked.Values.ToArray();
        var before = typeChecks;

        // std gains a declaration that main does not reference.
        context.FilesSource.Replace(std, std with { Text = StdText + "\ndecl Map" });

        var results = typeChecked.Values.ToDictionary(f => f.Path);
        Assert.Equal(["List:std.fugu", "Option:std.fugu"], results["main.fugu"].Resolved);
        Assert.Equal(expectedTypeChecks, typeChecks - before);
    }
}

/// <summary>An immutable array with structural equality, for records in these tests.</summary>
public readonly struct ValueArray<T> : IEquatable<ValueArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;

    public ValueArray(IEnumerable<T> items) => _items = items.ToImmutableArray();

    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(ValueArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is ValueArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
