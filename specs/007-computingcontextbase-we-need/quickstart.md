# Quickstart: ComputingContextBase

## Overview
This quickstart demonstrates how to create and use a ComputingContextBase for incremental computation scenarios. We'll build a simple compiler context that tracks source files and automatically recomputes derived data when files change.

## Prerequisites
- .NET 10.0 RC1 or later
- Guacamole NuGet package
- Basic understanding of incremental computation concepts

## Step 1: Create a Computing Context

```csharp
using Guacamole;
using Guacamole.Core.Providers;

[ComputingContext]
public class CompilerContext : ComputingContextBase
{
    // Input sources (mutable) - will generate mutation methods
    public IncrementalValueProvider<Project> Project { get; set; }
    public IncrementalValuesProvider<SourceFile> SourceFiles { get; set; }

    // Derived computations (read-only) - no mutation methods generated
    public IncrementalValuesProvider<SyntaxTree> ParsedTrees { get; }
    public IncrementalValuesProvider<TypeDeclaration> Types { get; }
    public IncrementalValuesProvider<Diagnostic> Diagnostics { get; }

    public CompilerContext(IServiceProvider services) : base(services)
    {
        ConfigurePipeline();
    }

    private void ConfigurePipeline()
    {
        // Configure derived computations from input sources
        ParsedTrees = SourceFiles.Select(file => CSharpSyntaxTree.ParseText(file.Content));
        Types = ParsedTrees.SelectMany(tree => ExtractTypes(tree));
        Diagnostics = Types.SelectMany(type => AnalyzeType(type));
    }
}
```

## Step 2: Register Services

```csharp
// In your service configuration
services.AddComputingContext<CompilerContext>();
```

## Step 3: Use the Context

### Basic Operations
```csharp
var context = serviceProvider.GetRequiredService<CompilerContext>();

// Set project information (generated method)
var project = new Project("MyApp", ["Program.cs", "Utils.cs"]);
context.SetProject(project);

// Add source files (generated methods)
context.AddSourceFile(new SourceFile("Program.cs", "class Program { }"));
context.AddSourceFile(new SourceFile("Utils.cs", "class Utils { }"));

// Commit changes to trigger incremental recomputation
await context.SaveChangesAsync();

// Query results through providers (existing API)
var types = await context.Types.ToListAsync();
var diagnostics = await context.Diagnostics.ToListAsync();
```

### Batch Operations
```csharp
// Add multiple files in a batch
var sourceFiles = new[]
{
    new SourceFile("Model.cs", "class Model { }"),
    new SourceFile("Service.cs", "class Service { }"),
    new SourceFile("Controller.cs", "class Controller { }")
};

context.AddSourceFiles(sourceFiles); // Generated batch method
await context.SaveChangesAsync(); // Single commit triggers incremental update
```

### Error Handling
```csharp
try
{
    // This will throw - duplicate add
    context.AddSourceFile(existingFile);
    await context.SaveChangesAsync();
}
catch (ContextOperationException ex)
{
    Console.WriteLine($"Operation {ex.OperationType} failed on {ex.PropertyName}");
    Console.WriteLine($"Failed item: {ex.FailedItem}");
}
```

## Step 4: Incremental Updates

### File System Watcher Integration
```csharp
public class CompilerWatchMode
{
    private readonly CompilerContext _context;
    private readonly FileSystemWatcher _watcher;

    public void StartWatching(string projectPath)
    {
        _watcher.Changed += async (sender, e) =>
        {
            if (IsProjectFile(e.FullPath))
            {
                var project = await LoadProject(e.FullPath);
                _context.SetProject(project);
            }
            else if (IsSourceFile(e.FullPath))
            {
                var content = await File.ReadAllTextAsync(e.FullPath);
                var sourceFile = new SourceFile(e.Name, content);

                // Replace existing file - only this file and dependents recompute
                var existing = FindExistingFile(e.Name);
                if (existing != null)
                {
                    _context.ReplaceSourceFile(existing, sourceFile);
                }
                else
                {
                    _context.AddSourceFile(sourceFile);
                }
            }

            // Incremental recomputation happens automatically
            await _context.SaveChangesAsync();

            // Only changed types and diagnostics are recomputed
            var newDiagnostics = await _context.Diagnostics.ToListAsync();
            OnDiagnosticsUpdated(newDiagnostics);
        };
    }
}
```

### UI Integration
```csharp
public class CodeEditor
{
    private readonly CompilerContext _context;

    public async Task OnTextChanged(string fileName, string newContent)
    {
        var oldFile = FindFile(fileName);
        var newFile = new SourceFile(fileName, newContent);

        // Update context - triggers incremental parsing and analysis
        _context.ReplaceSourceFile(oldFile, newFile);
        await _context.SaveChangesAsync();

        // Get updated diagnostics for this file
        var fileDiagnostics = await _context.Diagnostics
            .Where(d => d.SourceFile == fileName)
            .ToListAsync();

        UpdateErrorList(fileDiagnostics);
    }
}
```

## Step 5: Testing Your Context

### Basic Functionality Test
```csharp
[Fact]
public async Task Context_WithSourceFiles_GeneratesTypes()
{
    // Arrange
    using var context = new CompilerContext(_services);

    // Act
    context.AddSourceFile(new SourceFile("Test.cs", "class Test { }"));
    await context.SaveChangesAsync();

    // Assert
    var types = await context.Types.ToListAsync();
    Assert.Single(types);
    Assert.Equal("Test", types[0].Name);
}
```

### Incremental Behavior Test
```csharp
[Fact]
public async Task Context_WhenFileChanges_OnlyAffectedTypesRecompute()
{
    // Arrange - track computation counts
    var parseCount = 0;
    var analyzeCount = 0;

    // Act - initial setup
    context.AddSourceFile(new SourceFile("A.cs", "class A { }"));
    context.AddSourceFile(new SourceFile("B.cs", "class B { }"));
    await context.SaveChangesAsync();

    // Act - change only one file
    var newFile = new SourceFile("A.cs", "class A { public void Method() { } }");
    context.ReplaceSourceFile(oldFileA, newFile);
    await context.SaveChangesAsync();

    // Assert - only A.cs was reparsed and reanalyzed
    Assert.Equal(1, parseCount); // Only A.cs reparsed
    Assert.Equal(1, analyzeCount); // Only A.cs reanalyzed
}
```

## Generated Methods Reference

For each input source property with a setter, the source generator creates:

### Single Value Properties (`IncrementalValueProvider<T>`)
- `Set{PropertyName}(T value)` - Set the value
- `Set{PropertyName}Async(T value, CancellationToken ct)` - Async version

### Collection Properties (`IncrementalValuesProvider<T>`)
- `Add{PropertyName}(T item)` - Add single item (throws on duplicate)
- `Add{PropertyName}Async(T item, CancellationToken ct)` - Async version
- `Remove{PropertyName}(T item)` - Remove item (returns bool)
- `Remove{PropertyName}Async(T item, CancellationToken ct)` - Async version
- `Replace{PropertyName}(T oldItem, T newItem)` - Replace item
- `Replace{PropertyName}Async(T oldItem, T newItem, CancellationToken ct)` - Async version
- `Add{PropertyName}s(IEnumerable<T> items)` - Batch add
- `Remove{PropertyName}s(IEnumerable<T> items)` - Batch remove

## Performance Characteristics

- **Cache Access**: <1ms for cached provider values
- **Incremental Updates**: <10ms for change propagation
- **Memory Overhead**: <2x baseline through LRU caching
- **Complexity**: O(changes) not O(total_data_size)

## Next Steps

1. **Add Custom Validations**: Implement custom validation logic in your context
2. **Performance Monitoring**: Use built-in metrics to track computation performance
3. **Error Recovery**: Implement retry logic for failed operations
4. **Advanced Pipelines**: Create complex dependency chains between providers
5. **Testing**: Write comprehensive tests covering incremental behavior

## Troubleshooting

### Common Issues
1. **No mutation methods generated**: Ensure properties have setters and class has `[ComputingContext]` attribute
2. **Slow performance**: Check if O(changes) complexity is maintained, monitor cache hit rates
3. **Memory leaks**: Ensure contexts are properly disposed, especially in long-running applications
4. **Thread safety**: All operations are thread-safe with multiple readers/single writer model

### Debug Tips
- Use the computation graph visualizer to understand dependencies
- Monitor cache statistics to identify performance bottlenecks
- Enable detailed logging to trace incremental recomputation paths