using System;
using Guacamole;
using Guacamole.Core.Providers;

namespace CompilerContext;

/// <summary>
/// Example computing context for a simple compiler.
/// Demonstrates incremental computation with source files.
/// </summary>
[ComputingContext]
public partial class MyCompilerContext : ComputingContextBase
{
    public partial IncrementalValueProvider<Project> Project { get; set; }

    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; set; }

    public MyCompilerContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
    }

    partial void InitializeComputationNodes();
}
