# Roslyn Architecture: IIncrementalGenerator API Analysis

## Overview

Roslyn's IIncrementalGenerator represents a fundamental shift from the deprecated ISourceGenerator API, providing a pipeline-based architecture optimized for performance, incremental computation, and IDE responsiveness. This analysis examines the core architectural principles and design decisions that make Roslyn's incremental source generation effective for large-scale development.

## Core Concepts

### IIncrementalGenerator Interface

The `IIncrementalGenerator` interface provides a single entry point for source generation:

```csharp
namespace Microsoft.CodeAnalysis
{
    public interface IIncrementalGenerator
    {
        void Initialize(IncrementalGeneratorInitializationContext context);
    }
}
```

**Key Characteristics:**
- **Single Entry Point**: Unlike ISourceGenerator with separate `Initialize()` and `Execute()` methods
- **Host-Controlled Lifetime**: The compiler controls generator lifetime; state should not be stored on generator instances
- **One-Time Initialization**: Initialize method called exactly once by the host
- **Shared Instances**: Generator instances may be shared across multiple projects

### Pipeline Architecture

The incremental generator uses a pipeline-based architecture with three core components:

1. **IncrementalValueProvider<T>**: Always provides a single value
2. **IncrementalValuesProvider<T>**: Provides zero or more values
3. **Transformation Operations**: Select, SelectMany, Where, Combine

**Pipeline Stages:**
1. **Predicate Stage**: Fast syntax-only filtering (executes on every keystroke)
2. **Transform Stage**: Semantic analysis and model creation
3. **Output Stage**: Code generation and diagnostic emission

**Performance Benefits:**
- Each stage caches inputs/outputs for memoization
- Transformations only execute when input data changes
- Early filtering reduces downstream processing overhead

### Compilation Integration

**Execution Timing:**
- **IDE Integration**: Generators execute on every keystroke
- **Build Integration**: Fresh instances per MSBuild process
- **Incremental Builds**: Leverage cached transformations

**Key Integration Points:**
- Generators are "explicitly additive" - can only add source, not modify existing code
- Generators run "unordered" and see the same input compilation
- Support for both source output and diagnostic generation

## Key Components

### Syntax Providers

The `SyntaxValueProvider` offers two main patterns:

```csharp
// General syntax provider
var classProvider = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => ExtractClassInfo(ctx.Node, ctx.SemanticModel)
);

// Attribute-based provider (optimized)
var attributeProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
    "MyFramework.GenerateAttribute",
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => ExtractAttributeInfo(ctx)
);
```

### Value Providers

Core providers available during initialization:

- **CompilationProvider**: Access to compilation (use sparingly)
- **AdditionalTextsProvider**: Configuration files and additional inputs
- **AnalyzerConfigOptionsProvider**: MSBuild properties and configuration
- **MetadataReferencesProvider**: Referenced assemblies
- **ParseOptionsProvider**: Language version and compilation settings

### Output Registration

Three types of output registration:

```csharp
// Main source output
context.RegisterSourceOutput(provider, GenerateSource);

// Post-initialization (runs before other generators)
context.RegisterPostInitializationOutput(GenerateEarlySource);

// Implementation source (optimization hint)
context.RegisterImplementationSourceOutput(provider, GenerateImplementation);
```

## Design Principles

### 1. Value Equality for Caching

All pipeline values must be value-equatable for effective caching:

```csharp
// ❌ Poor caching - not value-equatable
public class BadModel
{
    public ISymbol Symbol { get; set; }
    public SyntaxNode Node { get; set; }
}

// ✅ Excellent caching - value-equatable
public record GoodModel(string TypeName, string Namespace, bool IsPartial);
```

### 2. Early Filtering Optimization

Predicates must be fast since they run on every keystroke:

```csharp
// ❌ Expensive predicate
static bool BadPredicate(SyntaxNode node, CancellationToken ct)
{
    // Semantic analysis in predicate - too expensive!
    var model = compilation.GetSemanticModel(node.SyntaxTree);
    return model.GetSymbolInfo(node).Symbol != null;
}

// ✅ Fast predicate
static bool GoodPredicate(SyntaxNode node, CancellationToken ct)
{
    // Syntax-only checks - fast execution
    return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
}
```

### 3. Immutable Pipeline Design

Transformations are defined declaratively and immutably:

```csharp
var pipeline = context.SyntaxProvider
    .ForAttributeWithMetadataName("GenerateAttribute", predicate, transform)
    .Select(model => EnrichModel(model))
    .Where(model => model.ShouldGenerate)
    .Collect(); // Convert to IncrementalValuesProvider
```

## Performance Characteristics

### IIncrementalGenerator vs ISourceGenerator

| Aspect | ISourceGenerator (Deprecated) | IIncrementalGenerator |
|--------|-------------------------------|----------------------|
| **Execution Model** | Execute runs on every generation | Pipeline-based with intelligent caching |
| **Filtering** | ISyntaxReceiver for basic filtering | Multi-stage pipeline with memoization |
| **Performance** | Regenerates everything on changes | Incremental with sophisticated caching |
| **IDE Impact** | Poor responsiveness in large solutions | Optimized for IDE scenarios |

### Real-World Performance Gains

- **10x+ performance improvement** in large codebases
- **Reduced CPU usage** during IDE editing
- **Better memory utilization** through intelligent caching
- **Responsive IntelliSense** even with multiple generators

## Architectural Recommendations

### For Framework Development

**Adopt These Patterns:**
1. **Pipeline Architecture**: Use staged transformations for complex operations
2. **Value Equality**: Design all models with structural equality
3. **Early Filtering**: Implement selective predicates for performance
4. **Immutable Design**: Leverage immutable transformations throughout
5. **Caching Strategy**: Design with memoization as first-class concern

**Avoid These Anti-Patterns:**
1. **Direct CompilationProvider Usage**: Causes regeneration on every keystroke
2. **Symbol/SyntaxNode in Models**: Breaks caching effectiveness
3. **Expensive Predicates**: Impacts IDE responsiveness
4. **Late Semantic Analysis**: Reduces caching opportunities
5. **Mutable State**: Breaks incremental computation guarantees

## Integration Considerations

### IDE Integration Benefits

- **Real-time Feedback**: Generators run as developers type
- **Incremental Updates**: Only affected code regenerates
- **Debugging Support**: Better tooling for generator development
- **Error Reporting**: Rich diagnostics integration

### MSBuild Integration

- **Clean Builds**: Full regeneration when needed
- **Incremental Builds**: Leverage file timestamps and dependency tracking
- **Configuration**: Integration with MSBuild properties and AdditionalFiles
- **Distribution**: Generators packaged as NuGet analyzers

## References

- [IIncrementalGenerator API Documentation](https://docs.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator)
- [Source Generators Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)
- [Incremental Generators Design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Performance Best Practices](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)