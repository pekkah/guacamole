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
    public IncrementalValueProvider<SymbolTable> Symbols { get; private set; }
    public IncrementalValuesProvider<TypeCheckedFile> TypeChecked { get; private set; }

    /// <summary>How many times a file was type checked (to show what reruns).</summary>
    public int TypeChecks => Volatile.Read(ref _typeChecks);

    /// <param name="restrictTables">
    /// Narrow the table to the names each file uses before type checking (see below).
    /// </param>
    public CompilerContext(bool restrictTables) : this()
    {
        Parsed = Files.Select(ParsedFile.Parse);

        // Cross-file data is computed from ALL files at once: per-file declarations, collected and
        // merged in a deterministic (path) order into a table with structural equality.
        Symbols = Parsed
            .Select(FileDeclarations.Extract)
            .Collect()
            .Select(SymbolTable.Merge);

        // Type checking is a pure function of (file, table). Restricting the table to the names the
        // file uses keeps the input equal when unrelated declarations change, so the per-item memo
        // of the next Select skips the file.
        var inputs = Parsed.Combine(Symbols);
        TypeChecked = (restrictTables
                ? inputs.Select(pair => (pair.Left, pair.Right.Restrict(pair.Left.Uses)))
                : inputs)
            .Select(pair => TypeCheck(pair.Item1, pair.Item2));
    }

    private TypeCheckedFile TypeCheck(ParsedFile file, SymbolTable table)
    {
        Interlocked.Increment(ref _typeChecks);

        var resolved = new List<string>();
        var errors = new List<string>();
        foreach (var name in file.Uses)
        {
            if (table.Symbols.TryGetValue(name, out var declaringFile))
                resolved.Add($"{name} -> {declaringFile}");
            else
                errors.Add($"unknown symbol '{name}'");
        }

        return new TypeCheckedFile(file.Path, new(resolved), new(errors));
    }
}
