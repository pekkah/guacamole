using Guacamole;
using FileWatcherExample;

Console.WriteLine("=== Guacamole File Watcher Example ===\n");

// Create context
var context = new SourceFileContext();

// Create computed properties using provider extension methods
var csharpFiles = context.SourceFiles.Where(f => f.Path.EndsWith(".cs"));
var csharpCollection = csharpFiles.Collect();
var statistics = csharpCollection.Select(files =>
    $"{files.Length} C# files, {files.Sum(f => f.LineCount)} total lines");

// Register output handlers
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(statistics, (stats, ctx) =>
{
    Console.WriteLine($"[STATS] {stats}");
});

context.RegisterOutput(csharpCollection, (files, ctx) =>
{
    Console.WriteLine($"[FILES] C# files in project:");
    foreach (var file in files.OrderBy(f => f.Path))
    {
        Console.WriteLine($"  - {file.Path} ({file.LineCount} lines)");
    }
    Console.WriteLine();
});

// Simulate file system events by pushing files
Console.WriteLine("Simulating file added: Program.cs");
context.SourceFilesSource.Push(new SourceFile(
    "Program.cs",
    "using System;\n\nclass Program\n{\n    static void Main() { }\n}"
));

Console.WriteLine("\nSimulating file added: Utils.cs");
context.SourceFilesSource.Push(new SourceFile(
    "Utils.cs",
    "using System;\n\nclass Utils\n{\n    public static void Log(string msg) => Console.WriteLine(msg);\n}"
));

Console.WriteLine("\nSimulating non-C# file (ignored by pipeline): README.md");
context.SourceFilesSource.Push(new SourceFile(
    "README.md",
    "# My Project\n\nThis is a readme."
));

// Simulate file modification using Replace
Console.WriteLine("\nSimulating file modified: Program.cs");
var oldProgram = new SourceFile(
    "Program.cs",
    "using System;\n\nclass Program\n{\n    static void Main() { }\n}"
);
var newProgram = new SourceFile(
    "Program.cs",
    "using System;\n\nclass Program\n{\n    static void Main()\n    {\n        Console.WriteLine(\"Hello\");\n    }\n}"
);
context.SourceFilesSource.Replace(oldProgram, newProgram);

// Simulate file deletion
Console.WriteLine("\nSimulating file deleted: Utils.cs");
context.SourceFilesSource.Remove(new SourceFile(
    "Utils.cs",
    "using System;\n\nclass Utils\n{\n    public static void Log(string msg) => Console.WriteLine(msg);\n}"
));

Console.WriteLine("\nDone! File system changes triggered incremental recomputation automatically.");
