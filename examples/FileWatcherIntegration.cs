using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Guacamole;
using Guacamole.Sources;

namespace Guacamole.Examples.Compiler;

/// <summary>
/// Example implementation of a file watcher that pushes source file changes
/// to an incremental compilation context using the proposed push-based architecture.
///
/// This demonstrates how external data sources integrate with Guacamole's
/// incremental computation graph without requiring mutation methods or SaveChanges().
/// </summary>
public sealed class SourceFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly IIncrementalSource<SourceFile> _sourceFilesSource;
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, SourceFile> _fileMap = new();
    private bool _isRunning;

    /// <summary>
    /// Creates a new file watcher for the specified directory.
    /// </summary>
    /// <param name="path">Directory to watch for .cs files</param>
    /// <param name="sourceFilesSource">The incremental source to push file changes to</param>
    public SourceFileWatcher(string path, IIncrementalSource<SourceFile> sourceFilesSource)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        _basePath = Path.GetFullPath(path);
        _sourceFilesSource = sourceFilesSource ?? throw new ArgumentNullException(nameof(sourceFilesSource));

        _watcher = new FileSystemWatcher(_basePath)
        {
            Filter = "*.cs",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = false,
            IncludeSubdirectories = true
        };

        // Wire up event handlers
        _watcher.Created += OnFileCreated;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnError;
    }

    /// <summary>
    /// Starts watching the directory and performs an initial scan of existing files.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        Console.WriteLine($"Scanning directory: {_basePath}");

        // Initial scan: Load all existing .cs files
        var filePaths = Directory.EnumerateFiles(_basePath, "*.cs", SearchOption.AllDirectories);
        var fileCount = 0;

        foreach (var filePath in filePaths)
        {
            var file = LoadSourceFile(filePath);
            if (file != null)
            {
                _fileMap[filePath] = file;
                _sourceFilesSource.Push(file); // Push to source - automatic recomputation!
                fileCount++;
            }
        }

        Console.WriteLine($"Loaded {fileCount} source files");

        _watcher.EnableRaisingEvents = true;
        _isRunning = true;

        Console.WriteLine("File watcher started. Monitoring for changes...");
    }

    /// <summary>
    /// Stops watching the directory.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _watcher.EnableRaisingEvents = false;
        _isRunning = false;

        Console.WriteLine("File watcher stopped");
    }

    /// <summary>
    /// Handles file creation events.
    /// </summary>
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"[Created] {GetRelativePath(e.FullPath)}");

        var file = LoadSourceFile(e.FullPath);
        if (file != null)
        {
            _fileMap[e.FullPath] = file;

            // PUSH-BASED: Directly push to source, no SaveChanges needed!
            _sourceFilesSource.Push(file);
            // The computation graph automatically invalidates caches
            // and triggers output handlers
        }
    }

    /// <summary>
    /// Handles file modification events.
    /// </summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"[Changed] {GetRelativePath(e.FullPath)}");

        var newFile = LoadSourceFile(e.FullPath);
        if (newFile != null && _fileMap.TryGetValue(e.FullPath, out var oldFile))
        {
            // Only update if content actually changed (avoid spurious events)
            if (!oldFile.Equals(newFile))
            {
                _fileMap[e.FullPath] = newFile;

                // PUSH-BASED: Replace the old file with the new one
                _sourceFilesSource.Replace(oldFile, newFile);
                // Automatic invalidation and recomputation!
            }
        }
        else if (newFile != null)
        {
            // File wasn't tracked before, add it
            _fileMap[e.FullPath] = newFile;
            _sourceFilesSource.Push(newFile);
        }
    }

    /// <summary>
    /// Handles file deletion events.
    /// </summary>
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"[Deleted] {GetRelativePath(e.FullPath)}");

        if (_fileMap.TryRemove(e.FullPath, out var file))
        {
            // PUSH-BASED: Remove from source, automatic recomputation!
            _sourceFilesSource.Remove(file);
        }
    }

    /// <summary>
    /// Handles file rename events.
    /// </summary>
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine($"[Renamed] {GetRelativePath(e.OldFullPath)} -> {GetRelativePath(e.FullPath)}");

        // Treat rename as delete + create for simplicity
        // In a real implementation, you might want to optimize this

        // Remove old file
        if (_fileMap.TryRemove(e.OldFullPath, out var oldFile))
        {
            _sourceFilesSource.Remove(oldFile);
        }

        // Add new file
        var newFile = LoadSourceFile(e.FullPath);
        if (newFile != null)
        {
            _fileMap[e.FullPath] = newFile;
            _sourceFilesSource.Push(newFile);
        }
    }

    /// <summary>
    /// Handles file system watcher errors.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e)
    {
        var exception = e.GetException();
        Console.WriteLine($"[Error] File watcher error: {exception?.Message}");

        // In production, you might want to restart the watcher or notify the user
    }

    /// <summary>
    /// Loads a source file from disk with error handling.
    /// </summary>
    private SourceFile? LoadSourceFile(string path)
    {
        try
        {
            // Read file content
            var content = File.ReadAllText(path);

            // Get relative path for better display
            var relativePath = GetRelativePath(path);

            // Get last write time for tracking
            var lastModified = File.GetLastWriteTimeUtc(path);

            return new SourceFile(relativePath, content, lastModified);
        }
        catch (IOException ex)
        {
            // File might be locked or not accessible
            Console.WriteLine($"[Warning] Could not read file {path}: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"[Warning] Access denied to file {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the relative path from the base directory.
    /// </summary>
    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_basePath, fullPath);
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
    }
}

/// <summary>
/// Represents a source file with content and metadata.
/// Implements value equality based on path and content hash for efficient
/// incremental computation.
/// </summary>
public sealed record SourceFile
{
    /// <summary>
    /// Relative path of the source file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// File content as a string.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Last modification time (UTC).
    /// </summary>
    public DateTime LastModified { get; }

    /// <summary>
    /// Lazy-computed content hash for efficient equality comparison.
    /// </summary>
    private readonly Lazy<int> _contentHash;

    public SourceFile(string path, string content, DateTime lastModified)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        LastModified = lastModified;

        // Compute content hash lazily only when needed for comparison
        _contentHash = new Lazy<int>(() => ComputeContentHash(content));
    }

    /// <summary>
    /// Value equality: Two files are equal if they have the same path and content.
    /// Uses content hash for efficient comparison.
    /// </summary>
    public bool Equals(SourceFile? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        // Fast path: compare paths and content hashes
        // This avoids string comparison of potentially large content
        return Path == other.Path && _contentHash.Value == other._contentHash.Value;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Path, _contentHash.Value);
    }

    /// <summary>
    /// Computes a hash of the file content.
    /// In production, consider using xxHash or a similar fast hash algorithm.
    /// </summary>
    private static int ComputeContentHash(string content)
    {
        // Simple hash - in production use xxHash or similar
        return content.GetHashCode();
    }

    /// <summary>
    /// Returns a string representation showing path and content size.
    /// </summary>
    public override string ToString()
    {
        return $"{Path} ({Content.Length} chars, modified {LastModified:yyyy-MM-dd HH:mm:ss})";
    }
}

/// <summary>
/// Example usage of the file watcher with an incremental compilation context.
/// </summary>
public static class FileWatcherExample
{
    public static async Task Main(string[] args)
    {
        // Parse command line arguments
        var watchPath = args.Length > 0 ? args[0] : "./src";

        if (!Directory.Exists(watchPath))
        {
            Console.WriteLine($"Error: Directory not found: {watchPath}");
            Console.WriteLine("Usage: FileWatcherExample [path]");
            return;
        }

        Console.WriteLine("=== Incremental Compiler Example ===");
        Console.WriteLine();

        // Create the compilation context
        using var context = new CompilerContext();

        // Set up the file watcher to push source files into the context
        // NOTE: In the new push-based design, there's no need for SaveChanges!
        using var watcher = new SourceFileWatcher(watchPath, context.SourceFilesSource);

        // Define the compilation pipeline
        // This is identical to how Roslyn generators work
        var parsedFiles = context.SourceFiles
            .Select(file => new ParsedFile(file, ParseSyntax(file)));

        var compilationResults = parsedFiles
            .Select(parsed => Compile(parsed))
            .Collect();

        var diagnostics = compilationResults
            .SelectMany(results => results)
            .Where(result => result.HasErrors)
            .SelectMany(result => result.Diagnostics);

        // Register output handler - automatically called when files change
        // No SaveChanges needed - reactive by default!
        context.RegisterOutput(diagnostics, OnDiagnosticsChanged);

        // Also register a handler for successful compilations
        var successfulCompilations = compilationResults
            .Select(results => results.Count(r => !r.HasErrors));

        context.RegisterOutput(successfulCompilations, OnCompilationCompleted);

        // Start watching - all changes flow automatically through the pipeline!
        watcher.Start();

        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to exit...");
        Console.WriteLine();

        // Keep running until user cancels
        var cts = new System.Threading.CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await Task.Delay(Timeout.Infinite, cts.Token);
    }

    /// <summary>
    /// Called automatically when diagnostics change (incremental recomputation).
    /// </summary>
    private static void OnDiagnosticsChanged(IEnumerable<Diagnostic> errors, ComputingContextBase context)
    {
        Console.WriteLine();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Compilation diagnostics updated");
        Console.WriteLine(new string('=', 60));

        var errorList = errors.ToList();

        if (errorList.Count == 0)
        {
            Console.WriteLine("✓ No errors found!");
        }
        else
        {
            Console.WriteLine($"✗ Found {errorList.Count} error(s):");
            Console.WriteLine();

            foreach (var error in errorList.Take(10))
            {
                Console.WriteLine($"  {error.Severity} {error.Code}: {error.Message}");
                Console.WriteLine($"    at {error.Location}");
                Console.WriteLine();
            }

            if (errorList.Count > 10)
            {
                Console.WriteLine($"  ... and {errorList.Count - 10} more error(s)");
            }
        }

        Console.WriteLine(new string('=', 60));
    }

    /// <summary>
    /// Called automatically when compilation count changes.
    /// </summary>
    private static void OnCompilationCompleted(int successCount, ComputingContextBase context)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Successfully compiled {successCount} file(s)");
    }

    // Mock compilation methods for demonstration
    private static SyntaxTree ParseSyntax(SourceFile file)
    {
        // In a real compiler, this would use a parser (e.g., Roslyn)
        return new SyntaxTree(file.Path);
    }

    private static CompilationResult Compile(ParsedFile parsed)
    {
        // In a real compiler, this would perform semantic analysis, code generation, etc.
        var hasErrors = parsed.File.Content.Contains("ERROR");
        var diagnostics = hasErrors
            ? new[] { new Diagnostic("CS0001", "Compilation error", parsed.File.Path, DiagnosticSeverity.Error) }
            : Array.Empty<Diagnostic>();

        return new CompilationResult(parsed.File.Path, hasErrors, diagnostics);
    }
}

// Supporting types for the example
public record ParsedFile(SourceFile File, SyntaxTree SyntaxTree);

public record SyntaxTree(string FilePath);

public record CompilationResult(string FilePath, bool HasErrors, IEnumerable<Diagnostic> Diagnostics);

public record Diagnostic(string Code, string Message, string Location, DiagnosticSeverity Severity);

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}
