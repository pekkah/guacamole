using System.Collections;
using System.Collections.Immutable;

namespace CrossFileSymbolsExample;

/// <summary>A source file of a tiny language: lines are <c>decl Name</c>, <c>use Name</c> or <c>// comment</c>.</summary>
public sealed record SourceFile(string Path, string Text);

public sealed record ParsedFile(string Path, EquatableArray<string> Declarations, EquatableArray<string> Uses, EquatableArray<string> Comments)
{
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
}

/// <summary>A name declared by a file: all the symbol table needs from it.</summary>
public readonly record struct Declaration(string Name, string Path)
{
    public static IEnumerable<Declaration> Extract(ParsedFile file) =>
        file.Declarations.Select(name => new Declaration(name, file.Path));
}

/// <summary>A resolved reference per use: <c>Name -> declaring file</c>, or an error.</summary>
public sealed record TypeCheckedFile(string Path, EquatableArray<string> Resolved, EquatableArray<string> Errors);

/// <summary>
/// The part of the symbol table a file uses. Immutable, with structural equality, so a restricted
/// table that is recomputed but equal does not count as a change.
/// </summary>
public sealed class SymbolTable : IEquatable<SymbolTable>
{
    private SymbolTable(ImmutableSortedDictionary<string, string> symbols) => Symbols = symbols;

    public ImmutableSortedDictionary<string, string> Symbols { get; }

    /// <summary>
    /// Narrows the table to the names a file references. Two files' restricted tables stay equal
    /// when only declarations they do not use change, so their type checks are not rerun.
    /// </summary>
    public static SymbolTable Restrict(IReadOnlyDictionary<string, string> symbols, IEnumerable<string> names)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (symbols.TryGetValue(name, out var path))
                builder[name] = path;
        }

        return new SymbolTable(builder.ToImmutable());
    }

    /// <summary>
    /// Formats a table in name order (a dictionary's own enumeration order is unspecified).
    /// </summary>
    public static string Format(IEnumerable<KeyValuePair<string, string>> symbols) =>
        string.Join(", ", symbols.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key} -> {s.Value}"));

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

    public override string ToString() => Format(Symbols);
}

/// <summary>An immutable array with structural equality, for use in records.</summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToImmutableArray();

    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
