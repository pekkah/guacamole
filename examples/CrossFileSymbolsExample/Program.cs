using CrossFileSymbolsExample;

Console.WriteLine("=== Guacamole Cross-File Symbols Example ===\n");

using var context = new CompilerContext(restrictTables: true);

// Register the symbol table first: output handlers run in registration order, so the table is
// cached (and its version kept when it is unchanged) before the type checks read it.
context.RegisterOutput(context.Symbols, (table, _) => Console.WriteLine($"[symbols] {table}"));
context.RegisterOutput(context.TypeChecked, (files, _) =>
{
    foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
    {
        var errors = file.Errors.Count == 0 ? "no errors" : string.Join("; ", file.Errors);
        Console.WriteLine($"[checked] {file.Path}: [{string.Join(", ", file.Resolved)}] {errors}");
    }
});

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

// A comment does not change any declarations: the table is recomputed but equal, so only the
// edited file is type checked again.
var stdWithComment = std with { Text = "// standard library\n" + std.Text };
Step("Add a comment to std.fugu", () => context.FilesSource.Replace(std, stdWithComment));

// A declaration main does not use changes the table, but not main's restricted view of it.
var stdWithMap = stdWithComment with { Text = stdWithComment.Text + "\ndecl Map" };
Step("Declare Map in std.fugu (main does not use it)", () => context.FilesSource.Replace(stdWithComment, stdWithMap));

// Removing a declaration main uses re-resolves main.
var stdWithoutOption = new SourceFile("std.fugu", "decl List\ndecl Map");
Step("Remove Option from std.fugu", () => context.FilesSource.Replace(stdWithMap, stdWithoutOption));

Console.WriteLine("\nDone.");
