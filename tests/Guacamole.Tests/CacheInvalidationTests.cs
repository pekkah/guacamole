using Guacamole;
using Xunit;

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
                var (filePath, baseDir) = tuple;
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

        // These assertions should pass but currently fail due to stale cache
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
                var (filePath, baseDir) = tuple;
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
}
