using System.Collections.Immutable;
using Guacamole;

namespace CrossFileSymbolsExample;

[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    private int _typeChecks;

    // Input: generates a FilesSource accessor
    public partial IncrementalValuesProvider<SourceFile> Files { get; }

    // Derived pipeline, built once
    public IncrementalValuesProvider<ParsedFile> Parsed { get; private set; }
    public IncrementalValueProvider<ImmutableDictionary<string, string>> Symbols { get; private set; }
    public IncrementalValuesProvider<TypeCheckedFile> TypeChecked { get; private set; }

    /// <summary>How many times a file was type checked (to show what reruns).</summary>
    public int TypeChecks => Volatile.Read(ref _typeChecks);

    /// <param name="restrictTables">
    /// Narrow the table to the names each file uses before type checking (see below).
    /// </param>
    public CompilerContext(bool restrictTables) : this()
    {
        Parsed = Files.Select(ParsedFile.Parse);

        // Cross-file data is computed from ALL files at once: each file's (name, path) declarations,
        // collected into a dictionary from name to declaring file. When several files declare a
        // name, the smallest path (ordinal) wins, so the table depends only on the files, not on the
        // order they were pushed or processed in. CollectToDictionary updates the table from the
        // changed files only, and keeps the same table instance (and version) when no entry changed.
        Symbols = Parsed
            .SelectMany(Declaration.Extract)
            .CollectToDictionary(d => d.Name, d => d.Path, conflictComparer: StringComparer.Ordinal);

        // Type checking is a pure function of (file, table). Restricting the table to the names the
        // file uses keeps the input equal when unrelated declarations change, so the per-item memo
        // of the next Select skips the file.
        var inputs = Parsed.Combine(Symbols);
        TypeChecked = restrictTables
            ? inputs
                .Select(pair => (File: pair.Left, Table: SymbolTable.Restrict(pair.Right, pair.Left.Uses)))
                .Select(pair => TypeCheck(pair.File, pair.Table.Symbols))
            : inputs.Select(pair => TypeCheck(pair.Left, pair.Right));
    }

    private TypeCheckedFile TypeCheck(ParsedFile file, IReadOnlyDictionary<string, string> table)
    {
        Interlocked.Increment(ref _typeChecks);

        var resolved = new List<string>();
        var errors = new List<string>();
        foreach (var name in file.Uses)
        {
            if (table.TryGetValue(name, out var declaringFile))
                resolved.Add($"{name} -> {declaringFile}");
            else
                errors.Add($"unknown symbol '{name}'");
        }

        return new TypeCheckedFile(file.Path, new(resolved), new(errors));
    }
}
