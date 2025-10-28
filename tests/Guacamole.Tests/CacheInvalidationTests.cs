using Guacamole;
using Xunit;
using System.Linq;

namespace Guacamole.Tests;

/// <summary>
/// Tests for cache invalidation issues (GitHub Issue #20).
/// Tests for IncrementalSource.Remove() + Push() with Combine() operations.
/// </summary>
public partial class CacheInvalidationTests
{

    /// <summary>
    /// Test context for reproducing the cache invalidation bug.
    /// Simulates a file path -> file content pipeline similar to the real-world use case.
    /// </summary>
    [ComputingContext]
    private partial class FileContentContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> FilePaths { get; }
        public partial IncrementalValueProvider<string> BaseDirectory { get; }
    }

    /// <summary>
    /// Test context with nested Combine operations to reproduce the multi-file bug.
    /// Mimics: SourceTexts -> Combine(DependencyGraph) -> ParsedUnits -> Combine(SymbolTable) -> TypeCheckedModules
    /// </summary>
    [ComputingContext]
    private partial class NestedCombineContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> SourceFiles { get; }
        public partial IncrementalValueProvider<string> Config { get; }
        public partial IncrementalValueProvider<int> Version { get; }
    }

    /// <summary>
    /// Simple record simulating file content with structural equality.
    /// </summary>
    private record FileContent(string Path, string Content)
    {
        public string Hash => Content.GetHashCode().ToString("X8");
    }

    [Fact]
    public void RemoveThenPush_WithCombine_ShouldInvalidateCache()
    {
        // Arrange
        var context = new FileContentContext();
        context.BaseDirectorySource.Set("/project");

        // Create pipeline: FilePaths + BaseDirectory -> Select to FileContent
        var fileContents = context.FilePaths
            .Combine(context.BaseDirectory)
            .Select(tuple =>
            {
                var (filePath, _) = tuple;
                // Simulate reading file content based on path
                var content = filePath switch
                {
                    "/project/file1.txt" => "Content of file 1",
                    "/project/file2.txt" => "Content of file 2",
                    _ => $"Content for {filePath}"
                };
                return new FileContent(filePath, content);
            });

        // Push initial file and read result
        context.FilePathsSource.Push("/project/file1.txt");
        var firstResult = fileContents.GetValues();

        Assert.True(firstResult.IsSuccess);
        var firstContent = firstResult.Value!.First();
        Assert.Equal("/project/file1.txt", firstContent.Path);
        Assert.Equal("Content of file 1", firstContent.Content);
        var firstHash = firstContent.Hash;

        // Act - Remove file1 and push file2 (simulating file change in watch mode)
        context.FilePathsSource.Remove("/project/file1.txt");
        context.FilePathsSource.Push("/project/file2.txt");

        // Assert - Should recompute with new file
        var secondResult = fileContents.GetValues();

        Assert.True(secondResult.IsSuccess);
        var secondContent = secondResult.Value!.First();

        Assert.Equal("/project/file2.txt", secondContent.Path);
        Assert.Equal("Content of file 2", secondContent.Content);
        Assert.NotEqual(firstHash, secondContent.Hash);
    }

    [Fact]
    public void RemoveThenPush_Simple_ShouldInvalidateCache()
    {
        // Arrange
        var context = new FileContentContext();

        // Create pipeline without Combine - just Select
        var transformed = context.FilePaths.Select(path => path.ToUpper());

        // Push initial value and read result
        context.FilePathsSource.Push("file1.txt");
        var firstResult = transformed.GetValues();

        Assert.True(firstResult.IsSuccess);
        Assert.Contains("FILE1.TXT", firstResult.Value!);

        // Act - Remove file1 and push file2
        context.FilePathsSource.Remove("file1.txt");
        context.FilePathsSource.Push("file2.txt");

        // Assert - Should have new value
        var secondResult = transformed.GetValues();

        Assert.True(secondResult.IsSuccess);
        Assert.Contains("FILE2.TXT", secondResult.Value!);
        Assert.DoesNotContain("FILE1.TXT", secondResult.Value!);
    }

    [Fact]
    public void MultiplePushes_WithoutRemove_ShouldWorkCorrectly()
    {
        // Arrange - Control test: pushing without removing should work
        var context = new FileContentContext();
        context.BaseDirectorySource.Set("/project");

        var fileContents = context.FilePaths
            .Combine(context.BaseDirectory)
            .Select(tuple =>
            {
                var (filePath, _) = tuple;
                var content = filePath switch
                {
                    "/project/file1.txt" => "Content of file 1",
                    "/project/file2.txt" => "Content of file 2",
                    _ => $"Content for {filePath}"
                };
                return new FileContent(filePath, content);
            });

        // Push first file
        context.FilePathsSource.Push("/project/file1.txt");
        var firstResult = fileContents.GetValues();
        Assert.Single(firstResult.Value!);

        // Push second file (without removing first)
        context.FilePathsSource.Push("/project/file2.txt");
        var secondResult = fileContents.GetValues();

        // Should have both files
        Assert.Equal(2, secondResult.Value!.Count());
        Assert.Contains(secondResult.Value!, f => f.Path == "/project/file1.txt");
        Assert.Contains(secondResult.Value!, f => f.Path == "/project/file2.txt");
    }

    [Fact]
    public void MultiFile_RemoveThenPush_ShouldIncludeNewFile()
    {
        // Arrange - Multi-file scenario: library.fugu + dependent.fugu
        var context = new FileContentContext();
        context.BaseDirectorySource.Set("/project");

        var fileContents = context.FilePaths
            .Combine(context.BaseDirectory)
            .Select(tuple =>
            {
                var (filePath, _) = tuple;
                var content = filePath switch
                {
                    "/project/library.fugu" => "Library v1",
                    "/project/library_v2.fugu" => "Library v2",
                    "/project/dependent.fugu" => "Dependent code",
                    _ => $"Content for {filePath}"
                };
                return new FileContent(filePath, content);
            });

        // Initial state: 2 files
        context.FilePathsSource.Push("/project/library.fugu");
        context.FilePathsSource.Push("/project/dependent.fugu");

        var firstResult = fileContents.GetValues();
        Assert.Equal(2, firstResult.Value!.Count());
        Assert.Contains(firstResult.Value!, f => f.Path == "/project/library.fugu");
        Assert.Contains(firstResult.Value!, f => f.Path == "/project/dependent.fugu");

        // Act - Replace library.fugu with library_v2.fugu
        context.FilePathsSource.Remove("/project/library.fugu");
        context.FilePathsSource.Push("/project/library_v2.fugu");

        // Assert - Should have both files with library_v2 instead of library
        var secondResult = fileContents.GetValues();
        Assert.Equal(2, secondResult.Value!.Count());
        Assert.Contains(secondResult.Value!, f => f.Path == "/project/library_v2.fugu");
        Assert.Contains(secondResult.Value!, f => f.Path == "/project/dependent.fugu");

        // Verify the content is correct for the new file
        var libraryV2 = secondResult.Value!.First(f => f.Path == "/project/library_v2.fugu");
        Assert.Equal("Library v2", libraryV2.Content);
    }

    [Fact]
    public void NestedCombine_MultiFile_RemoveThenPush_ShouldIncludeNewFile()
    {
        // Arrange - Nested Combine operations: SourceFiles -> Combine(Config) -> Combine(Version)
        var context = new NestedCombineContext();
        context.ConfigSource.Set("Debug");
        context.VersionSource.Set(1);

        // First layer: Combine with Config
        var withConfig = context.SourceFiles
            .Combine(context.Config)
            .Select(tuple => $"{tuple.Left}::{tuple.Right}");

        // Second layer: Combine with Version (nested Combine)
        var withVersion = withConfig
            .Combine(context.Version)
            .Select(tuple => $"{tuple.Left}:v{tuple.Right}");

        // Initial state: 2 files
        context.SourceFilesSource.Push("library.fugu");
        context.SourceFilesSource.Push("dependent.fugu");

        var firstResult = withVersion.GetValues();
        Assert.Equal(2, firstResult.Value!.Count());
        Assert.Contains("library.fugu::Debug:v1", firstResult.Value!);
        Assert.Contains("dependent.fugu::Debug:v1", firstResult.Value!);

        // Check what's in the source directly before Remove
        var sourceBeforeRemove = context.SourceFiles.GetValues();
        Assert.Equal(2, sourceBeforeRemove.Value!.Count());

        // Act - Replace library.fugu with library_v2.fugu
        context.SourceFilesSource.Remove("library.fugu");

        // Check source after Remove, before Push
        var sourceAfterRemove = context.SourceFiles.GetValues();
        Assert.Single(sourceAfterRemove.Value!);
        Assert.Contains("dependent.fugu", sourceAfterRemove.Value!);

        context.SourceFilesSource.Push("library_v2.fugu");

        // Check source after Push
        var sourceAfterPush = context.SourceFiles.GetValues();
        Assert.Equal(2, sourceAfterPush.Value!.Count());
        Assert.Contains("library_v2.fugu", sourceAfterPush.Value!);
        Assert.Contains("dependent.fugu", sourceAfterPush.Value!);

        // Assert - Should have both files with library_v2 instead of library
        var secondResult = withVersion.GetValues();

        // Diagnostic output
        var actualValues = secondResult.Value!.ToArray();
        System.Diagnostics.Debug.WriteLine($"Second result count: {actualValues.Length}");
        foreach (var val in actualValues)
        {
            System.Diagnostics.Debug.WriteLine($"  - {val}");
        }

        Assert.Equal(2, secondResult.Value!.Count());
        Assert.Contains("library_v2.fugu::Debug:v1", secondResult.Value!);
        Assert.Contains("dependent.fugu::Debug:v1", secondResult.Value!);
    }
}
