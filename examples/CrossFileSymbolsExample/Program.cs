using CrossFileSymbolsExample;
using Guacamole;

Console.WriteLine("=== Guacamole Cross-File Symbols Example ===\n");

using var context = new CompilerContext(restrictTables: true);

// Print the symbol table whenever it changes. (Every stage is cached and keeps its version when
// its recomputed value is unchanged, whether or not it has an output handler.)
context.RegisterOutput(context.Symbols, (table, _) => Console.WriteLine($"[symbols] {SymbolTable.Format(table)}"));
context.RegisterOutput(context.TypeChecked, (files, _) =>
{
    foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
    {
        var errors = file.Errors.Count == 0 ? "no errors" : string.Join("; ", file.Errors);
        Console.WriteLine($"[checked] {file.Path}: [{string.Join(", ", file.Resolved)}] {errors}");
    }
});

// One entry of the table: Lookup depends on that key only, so this handler runs when Option's
// entry changes, not whenever the table does (declaring Map below changes the table, not Option).
context.RegisterOutput(context.Symbols.LookupOrDefault("Option"), (path, _) =>
    Console.WriteLine($"[lookup] Option -> {path ?? "(not declared)"}"));

var std = new SourceFile("std.fugu", "decl List\ndecl Option");
var main = new SourceFile("main.fugu", "decl Main\nuse List\nuse Option");

void Step(string title, Action change)
{
    Console.WriteLine($"\n--- {title} ---");
    var before = context.TypeChecks;
    change();
    Console.WriteLine($"(type checks run: {context.TypeChecks - before})");
}

// main arrives before the file that declares what it uses. With a side-effecting symbol builder
// this order produced unresolved symbols; here main is simply re-checked once std arrives.
Step("Push main.fugu (std.fugu not loaded yet)", () => context.FilesSource.Push(main));
Step("Push std.fugu", () => context.FilesSource.Push(std));

// A comment does not change any declarations: the extracted declarations are equal, so the table
// is not even merged again, and only the edited file is type checked again.
var stdWithComment = std with { Text = "// standard library\n" + std.Text };
Step("Add a comment to std.fugu", () => context.FilesSource.Replace(std, stdWithComment));

// A declaration main does not use changes the table, but not main's restricted view of it.
var stdWithMap = stdWithComment with { Text = stdWithComment.Text + "\ndecl Map" };
Step("Declare Map in std.fugu (main does not use it)", () => context.FilesSource.Replace(stdWithComment, stdWithMap));

// Removing a declaration main uses re-resolves main.
var stdWithoutOption = new SourceFile("std.fugu", "decl List\ndecl Map");
Step("Remove Option from std.fugu", () => context.FilesSource.Replace(stdWithMap, stdWithoutOption));

Console.WriteLine("\nDone.");
