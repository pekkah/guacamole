using System;
using Guacamole;
using Guacamole.Core.Providers;

namespace FileWatcherContext;

/// <summary>
/// Example computing context for file system watching with incremental updates.
///
/// This demonstrates how ComputingContextBase can be used to build efficient
/// file monitoring systems that only reprocess changed files.
/// </summary>
[ComputingContext]
public partial class MyFileWatcherContext : ComputingContextBase
{
    // Properties with setters will generate mutation methods:
    // - AddFile(TrackedFile file)
    // - AddFiles(IEnumerable<TrackedFile> files)
    // - RemoveFile(TrackedFile file)
    // - ReplaceFile(TrackedFile oldFile, TrackedFile newFile)
    // - SaveChangesAsync() / SaveChanges()
    // - DiscardChanges()

    public partial IncrementalValuesProvider<TrackedFile> Files { get; set; }

    public MyFileWatcherContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
    }

    partial void InitializeComputationNodes();
}
