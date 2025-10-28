using Guacamole;

namespace FileWatcherExample;

// Define file model
public record SourceFile(string Path, string Content)
{
    public int LineCount => Content.Split('\n').Length;
}

// Define computing context for source files
[ComputingContext]
public partial class SourceFileContext : ComputingContextBase
{
    // Source property - generates SourceFilesSource accessor for pushing file changes
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
}
