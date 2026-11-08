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
    private readonly ITestOutputHelper _output;

    public CacheInvalidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
    /// Test context that mimics the real Fugu compiler structure with properties assigned in constructor.
    /// </summary>
    [ComputingContext]
    private partial class CompilerLikeContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> SourceFilePaths { get; }
        public partial IncrementalValueProvider<string> DependencyGraph { get; }
        public partial IncrementalValueProvider<int> SymbolTable { get; }

        // Pipeline properties assigned in constructor (like real Fugu compiler)
        public IncrementalValuesProvider<ParsedModule> ParsedUnits { get; private set; }
        public IncrementalValuesProvider<TypeCheckedModule> TypeCheckedModules { get; private set; }

        private readonly Dictionary<string, string> _fileSystem = new();

        public CompilerLikeContext(Dictionary<string, string> fileSystem) : this()
        {
            _fileSystem = fileSystem;

            // Level 1: SourceFilePaths → SourceTexts
            var sourceTexts = SourceFilePaths
                .Select(path => new { Path = path, Content = _fileSystem.GetValueOrDefault(path, "") });

            // Level 2: SourceTexts → Combine(DependencyGraph) → ParsedUnits
            ParsedUnits = sourceTexts
                .Combine(DependencyGraph)
                .Select(tuple =>
                {
                    var (sourceText, depGraph) = tuple;
                    var symbols = sourceText.Content.Contains("LibFunc")
                        ? new[] { "LibFunc" }
                        : new[] { "Main" };
                    return new ParsedModule(sourceText.Path, string.Join(",", symbols));
                });

            // Level 3: ParsedUnits → Combine(SymbolTable) → TypeCheckedModules
            TypeCheckedModules = ParsedUnits
                .Combine(SymbolTable)
                .Select(tuple =>
                {
                    var (parsed, version) = tuple;
                    var symbols = parsed.Content.Split(',');
                    return new TypeCheckedModule(parsed.SourceFile, symbols);
                });
        }
    }

    /// <summary>
    /// Record representing a parsed module with source file tracking.
    /// </summary>
    private record ParsedModule(string SourceFile, string Content);

    /// <summary>
    /// Record representing a type-checked module.
    /// </summary>
    private record TypeCheckedModule(string SourceFile, string[] Symbols);

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
        var firstContent = firstResult.ValueOrDefault!.First();
        Assert.Equal("/project/file1.txt", firstContent.Path);
        Assert.Equal("Content of file 1", firstContent.Content);
        var firstHash = firstContent.Hash;

        // Act - Remove file1 and push file2 (simulating file change in watch mode)
        context.FilePathsSource.Remove("/project/file1.txt");
        context.FilePathsSource.Push("/project/file2.txt");

        // Assert - Should recompute with new file
        var secondResult = fileContents.GetValues();

        Assert.True(secondResult.IsSuccess);
        var secondContent = secondResult.ValueOrDefault!.First();

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
        Assert.Contains("FILE1.TXT", firstResult.ValueOrDefault!);

        // Act - Remove file1 and push file2
        context.FilePathsSource.Remove("file1.txt");
        context.FilePathsSource.Push("file2.txt");

        // Assert - Should have new value
        var secondResult = transformed.GetValues();

        Assert.True(secondResult.IsSuccess);
        Assert.Contains("FILE2.TXT", secondResult.ValueOrDefault!);
        Assert.DoesNotContain("FILE1.TXT", secondResult.ValueOrDefault!);
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
        Assert.Single(firstResult.ValueOrDefault!);

        // Push second file (without removing first)
        context.FilePathsSource.Push("/project/file2.txt");
        var secondResult = fileContents.GetValues();

        // Should have both files
        Assert.Equal(2, secondResult.ValueOrDefault!.Count());
        Assert.Contains(secondResult.ValueOrDefault!, f => f.Path == "/project/file1.txt");
        Assert.Contains(secondResult.ValueOrDefault!, f => f.Path == "/project/file2.txt");
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
        Assert.Equal(2, firstResult.ValueOrDefault!.Count());
        Assert.Contains(firstResult.ValueOrDefault!, f => f.Path == "/project/library.fugu");
        Assert.Contains(firstResult.ValueOrDefault!, f => f.Path == "/project/dependent.fugu");

        // Act - Replace library.fugu with library_v2.fugu
        context.FilePathsSource.Remove("/project/library.fugu");
        context.FilePathsSource.Push("/project/library_v2.fugu");

        // Assert - Should have both files with library_v2 instead of library
        var secondResult = fileContents.GetValues();
        Assert.Equal(2, secondResult.ValueOrDefault!.Count());
        Assert.Contains(secondResult.ValueOrDefault!, f => f.Path == "/project/library_v2.fugu");
        Assert.Contains(secondResult.ValueOrDefault!, f => f.Path == "/project/dependent.fugu");

        // Verify the content is correct for the new file
        var libraryV2 = secondResult.ValueOrDefault!.First(f => f.Path == "/project/library_v2.fugu");
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
        Assert.Equal(2, firstResult.ValueOrDefault!.Count());
        Assert.Contains("library.fugu::Debug:v1", firstResult.ValueOrDefault!);
        Assert.Contains("dependent.fugu::Debug:v1", firstResult.ValueOrDefault!);

        // Check what's in the source directly before Remove
        var sourceBeforeRemove = context.SourceFiles.GetValues();
        Assert.Equal(2, sourceBeforeRemove.ValueOrDefault!.Count());

        // Act - Replace library.fugu with library_v2.fugu
        context.SourceFilesSource.Remove("library.fugu");

        // Check source after Remove, before Push
        var sourceAfterRemove = context.SourceFiles.GetValues();
        Assert.Single(sourceAfterRemove.ValueOrDefault!);
        Assert.Contains("dependent.fugu", sourceAfterRemove.ValueOrDefault!);

        context.SourceFilesSource.Push("library_v2.fugu");

        // Check source after Push
        var sourceAfterPush = context.SourceFiles.GetValues();
        Assert.Equal(2, sourceAfterPush.ValueOrDefault!.Count());
        Assert.Contains("library_v2.fugu", sourceAfterPush.ValueOrDefault!);
        Assert.Contains("dependent.fugu", sourceAfterPush.ValueOrDefault!);

        // Assert - Should have both files with library_v2 instead of library
        var secondResult = withVersion.GetValues();

        // Diagnostic output
        var actualValues = secondResult.ValueOrDefault!.ToArray();
        System.Diagnostics.Debug.WriteLine($"Second result count: {actualValues.Length}");
        foreach (var val in actualValues)
        {
            System.Diagnostics.Debug.WriteLine($"  - {val}");
        }

        Assert.Equal(2, secondResult.ValueOrDefault!.Count());
        Assert.Contains("library_v2.fugu::Debug:v1", secondResult.ValueOrDefault!);
        Assert.Contains("dependent.fugu::Debug:v1", secondResult.ValueOrDefault!);
    }

    [Fact]
    public void ThreeLevelPipeline_RemoveThenPush_ShouldNotContainOldFile()
    {
        // Arrange - 3-level pipeline mimicking the real compiler structure
        var context = new NestedCombineContext();
        context.ConfigSource.Set("DepGraph");
        context.VersionSource.Set(1);

        // Simulated file system
        var fileSystem = new Dictionary<string, string>
        {
            ["/project/library.fugu"] = "int LibFunc() { return 100; }",
            ["/project/library_v2.fugu"] = "int LibFunc() { return 200; }",
            ["/project/dependent.fugu"] = "int Main() { return LibFunc(); }"
        };

        // Level 1: SourceFilePaths → SourceTexts (file reading)
        var sourceTexts = context.SourceFiles
            .Select(filePath => new { Path = filePath, Content = fileSystem.GetValueOrDefault(filePath, "") });

        // Level 2: SourceTexts → Combine(DependencyGraph) → ParsedUnits
        var parsedUnits = sourceTexts
            .Combine(context.Config)
            .Select(tuple =>
            {
                var (sourceText, depGraph) = tuple;
                // Simulate parsing - extract function names
                var symbols = sourceText.Content.Contains("LibFunc")
                    ? new[] { "LibFunc" }
                    : new[] { "Main" };
                return new ParsedModule(sourceText.Path, string.Join(",", symbols));
            });

        // Level 3: ParsedUnits → Combine(SymbolTable) → TypeCheckedModules
        var typeCheckedModules = parsedUnits
            .Combine(context.Version)
            .Select(tuple =>
            {
                var (parsed, version) = tuple;
                var symbols = parsed.Content.Split(',');
                return new TypeCheckedModule(parsed.SourceFile, symbols);
            });

        // Initial state: 2 files
        context.SourceFilesSource.Push("/project/library.fugu");
        context.SourceFilesSource.Push("/project/dependent.fugu");

        var firstResult = typeCheckedModules.GetValues();
        Assert.Equal(2, firstResult.ValueOrDefault!.Count());

        var libraryModule1 = firstResult.ValueOrDefault!.First(m => m.SourceFile.Contains("library"));
        Assert.Equal("/project/library.fugu", libraryModule1.SourceFile);

        // Act - Replace library.fugu with library_v2.fugu
        context.SourceFilesSource.Remove("/project/library.fugu");
        context.SourceFilesSource.Push("/project/library_v2.fugu");

        // Assert - Should NOT contain old file path
        var secondResult = typeCheckedModules.GetValues();
        Assert.Equal(2, secondResult.ValueOrDefault!.Count());

        // The critical assertion: old file should NOT be present
        var oldFile = secondResult.ValueOrDefault!.FirstOrDefault(m => m.SourceFile == "/project/library.fugu");
        Assert.Null(oldFile); // Old file should not be in results!

        // New file SHOULD be present
        var newFile = secondResult.ValueOrDefault!.FirstOrDefault(m => m.SourceFile == "/project/library_v2.fugu");
        Assert.NotNull(newFile); // New file should be in results!

        // Dependent should still be there
        var dependent = secondResult.ValueOrDefault!.FirstOrDefault(m => m.SourceFile == "/project/dependent.fugu");
        Assert.NotNull(dependent);
    }

    [Fact]
    public void CompilerLikeStructure_RemoveThenPush_OldFileShouldNotPersist()
    {
        // Arrange - Context with properties assigned in constructor (like real Fugu compiler)
        var fileSystem = new Dictionary<string, string>
        {
            ["/project/library.fugu"] = "int LibFunc() { return 100; }",
            ["/project/library_v2.fugu"] = "int LibFunc() { return 200; }",
            ["/project/dependent.fugu"] = "int Main() { return LibFunc(); }"
        };

        var context = new CompilerLikeContext(fileSystem);
        context.DependencyGraphSource.Set("depgraph-v1");
        context.SymbolTableSource.Set(1);

        // DIAGNOSTIC: Check runtime node registration
        var runtimeNodeCount = context.Graph.GetRuntimeNodeCount();
        var runtimeNodeIds = context.Graph.GetRuntimeNodeIds().ToList();
        _output.WriteLine($"Runtime nodes registered: {runtimeNodeCount}");
        foreach (var nodeId in runtimeNodeIds)
        {
            _output.WriteLine($"  - {nodeId}");
        }

        // Initial state: 2 files
        context.SourceFilePathsSource.Push("/project/library.fugu");
        context.SourceFilePathsSource.Push("/project/dependent.fugu");

        var firstModules = context.TypeCheckedModules.GetValues();
        Assert.Equal(2, firstModules.ValueOrDefault!.Count());

        var firstPaths = string.Join(", ", firstModules.ValueOrDefault!.Select(m => m.SourceFile).OrderBy(p => p));
        _output.WriteLine($"After initial Push: [{firstPaths}]");

        var libraryModule1 = firstModules.ValueOrDefault!.FirstOrDefault(m => m.SourceFile.EndsWith("library.fugu"));
        Assert.NotNull(libraryModule1);
        Assert.Equal("/project/library.fugu", libraryModule1.SourceFile);

        // Act - Replace library.fugu with library_v2.fugu (exact scenario from issue)
        _output.WriteLine("Executing Remove + Push...");
        context.SourceFilePathsSource.Remove("/project/library.fugu");
        context.SourceFilePathsSource.Push("/project/library_v2.fugu");

        // DIAGNOSTIC: Check what's in the source now
        var sourceSnapshot = context.SourceFilePathsSource.GetSnapshot();
        var sourcePaths = string.Join(", ", sourceSnapshot.OrderBy(p => p));
        _output.WriteLine($"Source collection after Remove+Push: [{sourcePaths}]");

        // Assert
        var secondModules = context.TypeCheckedModules.GetValues();
        var secondPaths = string.Join(", ", secondModules.ValueOrDefault!.Select(m => m.SourceFile).OrderBy(p => p));
        _output.WriteLine($"After Remove+Push: [{secondPaths}]");

        Assert.Equal(2, secondModules.ValueOrDefault!.Count());

        // Critical assertion from issue: library_v2.fugu should be in results
        var libraryModule2 = secondModules.ValueOrDefault!.FirstOrDefault(m => m.SourceFile.EndsWith("library_v2.fugu"));
        if (libraryModule2 == null)
        {
            // If library_v2 not found, check if old library still there (the bug!)
            var oldLibrary = secondModules.ValueOrDefault!.FirstOrDefault(m => m.SourceFile.Contains("library"));
            if (oldLibrary != null)
            {
                Assert.Fail($"Bug reproduced! Old file '{oldLibrary.SourceFile}' still in results instead of '/project/library_v2.fugu'. " +
                           $"Full results: [{secondPaths}]");
            }
            else
            {
                Assert.Fail($"Neither library.fugu nor library_v2.fugu found in results! Full results: [{secondPaths}]");
            }
        }

        // Verify content is correct
        Assert.Equal("/project/library_v2.fugu", libraryModule2.SourceFile);
        Assert.Contains("LibFunc", libraryModule2.Symbols);

        // Verify old file is NOT in results
        var oldFile = secondModules.ValueOrDefault!.FirstOrDefault(m => m.SourceFile == "/project/library.fugu");
        Assert.Null(oldFile);

        // Dependent should still be present
        var dependent = secondModules.ValueOrDefault!.FirstOrDefault(m => m.SourceFile == "/project/dependent.fugu");
        Assert.NotNull(dependent);
    }
}
