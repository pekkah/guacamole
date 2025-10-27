#!/usr/bin/env dotnet script
// Example 2: File Watcher Integration
// Demonstrates integrating external event sources (file system watcher) with incremental computation

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guacamole;
using Guacamole.Sources;

Console.WriteLine("=== Guacamole File Watcher Example ===\n");

// Define file model
public record SourceFile(string Path, string Content)
{
    public int LineCount => Content.Split('\n').Length;
}

// Define computing context for source files
[ComputingContext]
public partial class SourceFileContext : ComputingContextBase
{
    // Source files collection - generates SourceFilesSource accessor
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

    // Computed: C# files only
    public IncrementalValuesProvider<SourceFile> CSharpFiles =>
        SourceFiles.Where(f => f.Path.EndsWith(".cs"));

    // Computed: Total lines of code
    public IncrementalValueProvider<int> TotalLines =>
        CSharpFiles.Select(files => files.Sum(f => f.LineCount));

    // Computed: File statistics
    public IncrementalValueProvider<string> Statistics =>
        CSharpFiles.Select(files =>
            $"{files.Count()} C# files, {files.Sum(f => f.LineCount)} total lines");
}

// Create context
var context = new SourceFileContext();

// Register output handlers
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(context.Statistics, (stats, ctx) =>
{
    Console.WriteLine($"[STATS] {stats}");
});

context.RegisterOutput(context.CSharpFiles, (files, ctx) =>
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
