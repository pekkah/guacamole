using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using SourceFile = Guacamole.Benchmarks.SymbolTableBenchmarks.SourceFile;

namespace Guacamole.Benchmarks;

/// <summary>
/// Fan-out from a shared symbol table to per-file lookups (issue #95). Modelled on
/// <c>examples/CrossFileSymbolsExample</c> and <see cref="SymbolTableBenchmarks"/>: <see cref="Files"/>
/// files each declare <c>A{i}</c>, <c>B{i}</c> and <c>Common{i % 100}</c>, collected with
/// <c>Files.SelectMany(declarations).CollectToDictionary(name, path, StringComparer.Ordinal)</c>.
/// Each file then has its own lookup stage, <c>table.Select(d =&gt; (d.GetValueOrDefault(...), ...))</c>
/// for the three names it references (<c>A{i+7}</c>, <c>B{i+1}</c>, <c>Common{i % 100}</c>), and a
/// downstream per-file result (<c>lookup.Select(Check)</c>).
/// <para>
/// Each operation edits one file (its <c>B</c> declaration toggles between <c>B{j}</c> and
/// <c>B{j}x</c>), which changes the table and the lookup of exactly one other file, then reads.
/// Every lookup stage reads the whole table, so after any edit all N lookup stages are marked dirty
/// and rerun (three dictionary lookups each); their equality cut-off keeps all but one per-file
/// result cached. The <c>Lookup*</c> variants build each file's lookup from the per-key operator
/// instead, <c>table.LookupOrDefault(A).Combine(table.LookupOrDefault(B)).Combine(table.LookupOrDefault(Common))</c>,
/// which depends on the three keys only: an edit marks and reruns only the lookups of the keys
/// whose entries changed.
/// </para>
/// <list type="bullet">
/// <item><description><see cref="DirectReadAll"/>: a mutable <c>Dictionary</c> updated in place and
/// every file's lookups and result computed directly (no framework).</description></item>
/// <item><description><see cref="TableOnly"/>: the same edit and a read of the table, in a context
/// without per-file stages (the cost before the fan-out).</description></item>
/// <item><description><see cref="ReadAll"/>: the edit, then every per-file result is read.</description></item>
/// <item><description><see cref="ReadOne"/>: the edit, then only the affected file's result is read
/// (the other lookups are marked dirty but not re-verified).</description></item>
/// <item><description><see cref="LookupReadAll"/> and <see cref="LookupReadOne"/>: the same with the
/// per-key lookups.</description></item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class SymbolLookupFanOutBenchmarks
{
    private const int SharedNames = 100;

    /// <summary>The three names a file references.</summary>
    public readonly record struct References(string A, string B, string Common);

    /// <summary>The declaring files of a file's references (<c>null</c> when unresolved).</summary>
    public readonly record struct Resolved(string? A, string? B, string? Common);

    private BenchmarkContext _context = null!;
    private IIncrementalSource<SourceFile> _source = null!;
    private FileEdits _edits = null!;
    private IncrementalValueProvider<int>[] _results = null!;
    private int _checks;

    private BenchmarkContext _lookupContext = null!;
    private IIncrementalSource<SourceFile> _lookupSource = null!;
    private FileEdits _lookupEdits = null!;
    private IncrementalValueProvider<int>[] _lookupResults = null!;
    private int _lookupChecks;

    private BenchmarkContext _tableContext = null!;
    private IIncrementalSource<SourceFile> _tableSource = null!;
    private FileEdits _tableEdits = null!;
    private IncrementalValueProvider<ImmutableDictionary<string, string>> _table;

    private Dictionary<string, string> _direct = null!;
    private FileEdits _directEdits = null!;
    private References[] _references = null!;

    /// <summary>Number of files (each with a lookup stage and a result stage).</summary>
    [Params(1_000, 10_000)]
    public int Files { get; set; }

    /// <summary>How many times a per-file result was computed (for the call-count tests).</summary>
    public int Checks => _checks;

    /// <summary>How many times a per-file result of the <c>Lookup*</c> variants was computed.</summary>
    public int LookupChecks => _lookupChecks;

    [GlobalSetup]
    public void Setup()
    {
        _references = Enumerable.Range(0, Files)
            .Select(i => new References($"A{(i + 7) % Files}", $"B{(i + 1) % Files}", $"Common{i % SharedNames}"))
            .ToArray();

        // Fan-out: table -> N lookups -> N results
        _context = new BenchmarkContext();
        _source = _context.AddSource<SourceFile>("Files");
        _edits = new FileEdits(SymbolTableBenchmarks.CreateFiles(Files));
        _source.PushRange(_edits.Files);
        var table = BuildTable(_source);
        _results = new IncrementalValueProvider<int>[Files];
        for (var i = 0; i < Files; i++)
        {
            var references = _references[i];
            _results[i] = table
                .Select(d => Lookup(d, references))
                .Select(Check);
        }

        foreach (var result in _results)
            _ = result.Value;

        // Fan-out through per-key lookups: table -> 3 lookups per file (shared when the key is) -> N results
        _lookupContext = new BenchmarkContext();
        _lookupSource = _lookupContext.AddSource<SourceFile>("Files");
        _lookupEdits = new FileEdits(SymbolTableBenchmarks.CreateFiles(Files));
        _lookupSource.PushRange(_lookupEdits.Files);
        var lookupTable = BuildTable(_lookupSource);
        _lookupResults = new IncrementalValueProvider<int>[Files];
        for (var i = 0; i < Files; i++)
        {
            var references = _references[i];
            _lookupResults[i] = lookupTable.LookupOrDefault(references.A)
                .Combine(lookupTable.LookupOrDefault(references.B))
                .Combine(lookupTable.LookupOrDefault(references.Common))
                .Select(static found => new Resolved(found.Item1.Item1, found.Item1.Item2, found.Item2))
                .Select(CheckLookup);
        }

        foreach (var result in _lookupResults)
            _ = result.Value;

        // The table alone
        _tableContext = new BenchmarkContext();
        _tableSource = _tableContext.AddSource<SourceFile>("Files");
        _tableEdits = new FileEdits(SymbolTableBenchmarks.CreateFiles(Files));
        _tableSource.PushRange(_tableEdits.Files);
        _table = BuildTable(_tableSource);
        _ = _table.Value;

        // Direct
        _directEdits = new FileEdits(SymbolTableBenchmarks.CreateFiles(Files));
        _direct = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in _directEdits.Files)
        {
            foreach (var (name, path) in SymbolTableBenchmarks.Declare(file))
            {
                if (!_direct.TryGetValue(name, out var existing) || StringComparer.Ordinal.Compare(path, existing) < 0)
                    _direct[name] = path;
            }
        }

        _checks = 0;
        _lookupChecks = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _lookupContext.Dispose();
        _tableContext.Dispose();
    }

    /// <summary>Edit one file, then compute every file's lookups and result directly.</summary>
    [Benchmark(Baseline = true)]
    public long DirectReadAll()
    {
        var (before, after) = _directEdits.Next();
        // B names are declared by one file only
        _direct.Remove(before.Declarations[1]);
        _direct[after.Declarations[1]] = after.Path;

        long sum = 0;
        for (var i = 0; i < _references.Length; i++)
            sum += Check(Lookup(_direct, _references[i]));
        return sum;
    }

    /// <summary>Edit one file, then read the table (no per-file stages in this context).</summary>
    [Benchmark]
    public int TableOnly()
    {
        var (before, after) = _tableEdits.Next();
        _tableSource.Replace(before, after);
        return _table.Value.Count;
    }

    /// <summary>Edit one file, then read every file's result.</summary>
    [Benchmark]
    public long ReadAll()
    {
        var (before, after) = _edits.Next();
        _source.Replace(before, after);

        long sum = 0;
        foreach (var result in _results)
            sum += result.Value;
        return sum;
    }

    /// <summary>Edit one file, then read the result of the one file whose lookup it changed.</summary>
    [Benchmark]
    public int ReadOne()
    {
        var (before, after) = _edits.Next();
        _source.Replace(before, after);

        // File i references B{i+1}, so the edit of file j changes the lookup of file j-1
        var reader = (_edits.LastIndex + Files - 1) % Files;
        return _results[reader].Value;
    }

    /// <summary>Edit one file, then read every file's result, looked up per key.</summary>
    [Benchmark]
    public long LookupReadAll()
    {
        var (before, after) = _lookupEdits.Next();
        _lookupSource.Replace(before, after);

        long sum = 0;
        foreach (var result in _lookupResults)
            sum += result.Value;
        return sum;
    }

    /// <summary>Edit one file, then read the result of the one file whose lookup it changed, looked up per key.</summary>
    [Benchmark]
    public int LookupReadOne()
    {
        var (before, after) = _lookupEdits.Next();
        _lookupSource.Replace(before, after);

        var reader = (_lookupEdits.LastIndex + Files - 1) % Files;
        return _lookupResults[reader].Value;
    }

    private static IncrementalValueProvider<ImmutableDictionary<string, string>> BuildTable(IIncrementalSource<SourceFile> source) =>
        source.AsProvider()
            .SelectMany(static file => SymbolTableBenchmarks.Declare(file))
            .CollectToDictionary(static d => d.Name, static d => d.Path, StringComparer.Ordinal);

    private static Resolved Lookup(IReadOnlyDictionary<string, string> table, References references) =>
        new(table.GetValueOrDefault(references.A), table.GetValueOrDefault(references.B), table.GetValueOrDefault(references.Common));

    private int Check(Resolved resolved)
    {
        _checks++;
        return Score(resolved);
    }

    private int CheckLookup(Resolved resolved)
    {
        _lookupChecks++;
        return Score(resolved);
    }

    private static int Score(Resolved resolved) =>
        (resolved.A is null ? 0 : 1) + (resolved.B is null ? 0 : 2) + (resolved.Common is null ? 0 : 4);

    /// <summary>
    /// The files and the next edit: files are edited in turn, and each edit toggles the file's
    /// <c>B</c> declaration between <c>B{j}</c> and <c>B{j}x</c>.
    /// </summary>
    private sealed class FileEdits(SourceFile[] files)
    {
        private int _next;

        public SourceFile[] Files { get; } = files;

        public int LastIndex { get; private set; }

        public (SourceFile Before, SourceFile After) Next()
        {
            var index = _next;
            _next = (_next + 1) % Files.Length;
            LastIndex = index;

            var before = Files[index];
            var name = before.Declarations[1];
            var toggled = name.EndsWith('x') ? name[..^1] : name + "x";
            var after = before with { Declarations = before.Declarations.SetItem(1, toggled) };
            Files[index] = after;
            return (before, after);
        }
    }
}
