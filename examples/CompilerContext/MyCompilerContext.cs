using System;
using Guacamole;

namespace CompilerContext;

/// <summary>
/// Example computing context for a simple compiler.
/// Demonstrates incremental computation with source files.
/// </summary>
[ComputingContext]
public partial class MyCompilerContext : ComputingContextBase
{
    // Get-only properties are auto-managed (NEW convention)
    // Framework generates SetProject(), AddSourceFile(), etc.
    public partial IncrementalValueProvider<Project> Project { get; }

    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

}
