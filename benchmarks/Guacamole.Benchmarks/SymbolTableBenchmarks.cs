using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;

namespace Guacamole.Benchmarks;

/// <summary>
/// A symbol table over <see cref="Files"/> files (modelled on <c>examples/CrossFileSymbolsExample</c>):
/// each file declares two names of its own and one of 100 shared names, and the table maps each
/// name to the first declaring file in path order. <see cref="Changes"/> files get a new declaration
/// before each read of the table.
/// <list type="bullet">
/// <item><description><see cref="CollectAndMerge"/>: <c>Files.Select(Extract).Collect().Select(Merge)</c>,
/// the merge sorting the files by path and folding them into an immutable sorted dictionary (as in
/// the example before #82): O(N log N) per change.</description></item>
/// <item><description><see cref="CollectToDictionary"/>: <c>Files.SelectMany(declarations).CollectToDictionary(name, path, StringComparer.Ordinal)</c>
/// (#82): the dictionary is updated from the collection's delta, O(K log N).</description></item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class SymbolTableBenchmarks
{
    /// <summary>A source file: its path and the names it declares.</summary>
    public sealed record SourceFile(string Path, ImmutableArray<string> Declarations)
    {
        /// <inheritdoc />
        public bool Equals(SourceFile? other) =>
            other is not null && Path == other.Path && Declarations.AsSpan().SequenceEqual(other.Declarations.AsSpan());

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Path, Declarations.Length);
    }

    private BenchmarkContext _mergeContext = null!;
    private IIncrementalSource<SourceFile> _mergeSource = null!;
    private SourceFile[] _mergeFiles = null!;
    private IncrementalValueProvider<ImmutableSortedDictionary<string, string>> _merged;

    private BenchmarkContext _dictionaryContext = null!;
    private IIncrementalSource<SourceFile> _dictionarySource = null!;
    private SourceFile[] _dictionaryFiles = null!;
    private IncrementalValueProvider<ImmutableDictionary<string, string>> _dictionary;

    private int _start;
    private int _version;

    /// <summary>Number of files.</summary>
    [Params(10_000)]
    public int Files { get; set; }

    /// <summary>Number of files changed before each read.</summary>
    [Params(1, 10, 100)]
    public int Changes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _mergeContext = new BenchmarkContext();
        _mergeSource = _mergeContext.AddSource<SourceFile>("Files");
        _mergeFiles = CreateFiles(Files);
        _mergeSource.PushRange(_mergeFiles);
        _merged = _mergeSource.AsProvider()
            .Select(static file => file) // the per-file extraction stage of the example
            .Collect()
            .Select(static files => Merge(files));
        _ = _merged.Value;

        _dictionaryContext = new BenchmarkContext();
        _dictionarySource = _dictionaryContext.AddSource<SourceFile>("Files");
        _dictionaryFiles = CreateFiles(Files);
        _dictionarySource.PushRange(_dictionaryFiles);
        _dictionary = _dictionarySource.AsProvider()
            .SelectMany(static file => Declare(file))
            .CollectToDictionary(static d => d.Name, static d => d.Path, StringComparer.Ordinal);
        _ = _dictionary.Value;

        _start = 0;
        _version = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mergeContext.Dispose();
        _dictionaryContext.Dispose();
    }

    /// <summary>Change K files, then read the table built with <c>Collect().Select(Merge)</c>.</summary>
    [Benchmark(Baseline = true)]
    public int CollectAndMerge()
    {
        ChangeFiles(_mergeFiles, _mergeSource);
        return _merged.Value.Count;
    }

    /// <summary>Change K files, then read the table built with <c>CollectToDictionary</c>.</summary>
    [Benchmark]
    public int CollectToDictionary()
    {
        ChangeFiles(_dictionaryFiles, _dictionarySource);
        return _dictionary.Value.Count;
    }

    private void ChangeFiles(SourceFile[] files, IIncrementalSource<SourceFile> source)
    {
        _version++;
        for (var k = 0; k < Changes; k++)
        {
            var index = (_start + k) % files.Length;
            var file = files[index];
            var changed = file with { Declarations = file.Declarations.SetItem(1, $"V{_version}_{index}") };
            source.Replace(file, changed);
            files[index] = changed;
        }

        _start += Changes;
    }

    /// <summary>File i declares <c>A{i}</c>, <c>B{i}</c> and the shared <c>Common{i % 100}</c>.</summary>
    public static SourceFile[] CreateFiles(int count) =>
        Enumerable.Range(0, count)
            .Select(static i => new SourceFile($"src/file{i:D5}.fugu", [$"A{i}", $"B{i}", $"Common{i % 100}"]))
            .ToArray();

    /// <summary>The (name, declaring file) pairs of a file.</summary>
    public static IEnumerable<(string Name, string Path)> Declare(SourceFile file)
    {
        foreach (var name in file.Declarations)
            yield return (name, file.Path);
    }

    /// <summary>The merge of the example: files in path order, the first declaration of a name wins.</summary>
    public static ImmutableSortedDictionary<string, string> Merge(ImmutableArray<SourceFile> files)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(static f => f.Path, StringComparer.Ordinal))
        {
            foreach (var name in file.Declarations)
                builder.TryAdd(name, file.Path);
        }

        return builder.ToImmutable();
    }
}
