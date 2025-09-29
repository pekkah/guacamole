# Roslyn Patterns: Implementation Best Practices

## Overview

This document explores common implementation patterns, best practices, and practical examples for working with Roslyn's IIncrementalGenerator API. These patterns represent current best practices for building high-performance, maintainable source generators.

## Common Implementation Patterns

### Basic IIncrementalGenerator Structure

The foundation of every incremental generator follows this pattern:

```csharp
[Generator]
public class MyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Register static content (attributes, etc.)
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("Attribute.g.cs", SourceText.From(attributeSource, Encoding.UTF8)));

        // 2. Create pipeline
        var pipeline = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidate(s),
                transform: static (ctx, _) => GetModel(ctx))
            .Where(static m => m is not null);

        // 3. Generate output
        context.RegisterSourceOutput(pipeline,
            static (spc, model) => GenerateCode(spc, model));
    }
}
```

### Value-Equatable Data Models

Critical for effective caching - all models must be value-equatable:

```csharp
// ✅ Good: Value-equatable record
public readonly record struct ClassToGenerate(
    string Name,
    string Namespace,
    EquatableArray<string> Properties);

// ❌ Bad: Contains non-equatable types
public class BadModel
{
    public INamedTypeSymbol Symbol { get; set; } // Breaks caching
    public ClassDeclarationSyntax Syntax { get; set; } // Breaks caching
}
```

## CreateSyntaxProvider Usage

### Efficient Filtering Pattern

The predicate runs on every syntax node during IDE editing, so it must be extremely fast:

```csharp
var classDeclarations = context.SyntaxProvider
    .CreateSyntaxProvider(
        // Fast predicate - runs on every keystroke!
        predicate: static (node, _) => node is ClassDeclarationSyntax cls
            && cls.AttributeLists.Count > 0,

        // Transform - extract minimal data
        transform: static (ctx, _) =>
        {
            var cls = (ClassDeclarationSyntax)ctx.Node;
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(cls);

            if (symbol?.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "GenerateAttribute") != true)
                return null;

            return new ClassInfo(
                Name: symbol.Name,
                Namespace: symbol.ContainingNamespace.ToDisplayString()
            );
        })
    .Where(static m => m is not null);
```

### Performance Guidelines

- **Predicate optimization**: Syntax-only checks, avoid semantic model access
- **Early extraction**: Convert symbols to value types immediately
- **Multiple transformations**: Better caching with smaller transformation steps

## ForAttributeWithMetadataName Patterns

### Optimal Attribute-Based Generation

This is the preferred pattern for attribute-based generators (.NET 7+):

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // Register the attribute first
    context.RegisterPostInitializationOutput(static ctx =>
        ctx.AddSource("GenerateAttribute.g.cs", """
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public class GenerateAttribute : System.Attribute { }
            """));

    // Use ForAttributeWithMetadataName (99% more efficient!)
    var pipeline = context.SyntaxProvider
        .ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "MyNamespace.GenerateAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => new ClassToGenerate(
                Name: ctx.TargetSymbol.Name,
                Namespace: ctx.TargetSymbol.ContainingNamespace.ToDisplayString(),
                Attributes: ctx.Attributes.Select(a => a.ToString()).ToArray()
            ));

    context.RegisterSourceOutput(pipeline, GenerateClass);
}
```

### Requirements

- **.NET 7+ SDK** required
- **Microsoft.CodeAnalysis.CSharp 4.4.0+**
- Use **full metadata name** including "Attribute" suffix

## Transformation Patterns

### SelectMany for Filtering

More efficient than Where().Select() chains:

```csharp
// ✅ Good: Use SelectMany instead of Where().Select()
var filtered = provider.SelectMany(static (item, _) =>
    item.IsValid ? [Transform(item)] : []);

// ❌ Less efficient: Where().Select() chain
var filtered = provider
    .Where(static item => item.IsValid)
    .Select(static item => Transform(item));
```

### Combine Operations

Combining multiple providers for richer context:

```csharp
// Combine with configuration
var combined = syntaxProvider
    .Combine(context.AnalyzerConfigOptionsProvider
        .Select(static (options, _) => options.GlobalOptions
            .TryGetValue("build_property.MyOption", out var value) ? value : "default"));

context.RegisterSourceOutput(combined,
    static (spc, data) => GenerateWithConfig(spc, data.Left, data.Right));
```

### Pipeline Batching

Processing collections efficiently:

```csharp
// Collect and process batches
var batched = pipeline
    .Collect()
    .Select(static (items, _) => items.Distinct().ToArray());

context.RegisterSourceOutput(batched, GenerateBatchedOutput);
```

## Best Practices

### Performance Optimization

```csharp
// 1. Use custom equality for collections
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly T[] _array;

    public bool Equals(EquatableArray<T> other) =>
        _array.AsSpan().SequenceEqual(other._array.AsSpan());

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        foreach (var item in _array)
            hashCode.Add(item);
        return hashCode.ToHashCode();
    }
}

// 2. Create diagnostic-friendly models
public readonly record struct GenerationResult(
    ClassToGenerate? Class,
    EquatableArray<DiagnosticInfo> Diagnostics);
```

### Error Handling

Proper diagnostic reporting:

```csharp
private static readonly DiagnosticDescriptor InvalidClassError = new(
    id: "GEN001",
    title: "Invalid class for generation",
    messageFormat: "Class '{0}' cannot be used for generation: {1}",
    category: "Generator",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

// In transform method
transform: static (ctx, _) =>
{
    try
    {
        return ProcessClass(ctx);
    }
    catch (Exception ex)
    {
        var diagnostic = Diagnostic.Create(
            InvalidClassError,
            ctx.Node.GetLocation(),
            ctx.TargetSymbol.Name,
            ex.Message);

        return new GenerationResult(null, [DiagnosticInfo.FromDiagnostic(diagnostic)]);
    }
}
```

### Testing Strategies

Using Verify.SourceGenerators for snapshot testing:

```csharp
[ModuleInitializer]
public static void Initialize() => VerifySourceGenerators.Enable();

[Fact]
public async Task GeneratesExpectedOutput()
{
    var source = """
        [Generate]
        public partial class TestClass
        {
            public string Name { get; set; }
        }
        """;

    await TestHelper.Verify<MyGenerator>(source);
}

// Test caching behavior
[Fact]
public void PipelineOutputsAreCacheable()
{
    var driver = CSharpGeneratorDriver.Create(new MyGenerator());
    var compilation = CreateCompilation(source);

    var result1 = driver.RunGenerators(compilation);
    var result2 = result1.GetRunResult().Driver.RunGenerators(compilation);

    // Verify same output without regeneration
    Assert.Same(result1.GeneratedTrees[0], result2.GeneratedTrees[0]);
}
```

## Anti-Patterns to Avoid

### Common Mistakes

1. **Don't store ISymbol or SyntaxNode in models** - breaks caching
2. **Don't use reflection in generators** - operates on wrong runtime
3. **Don't combine with CompilationProvider frequently** - causes regeneration on every keystroke
4. **Don't forget .NET 7+ requirement** for ForAttributeWithMetadataName
5. **Don't skip custom equality comparers** for collections

### Performance Killers

```csharp
// ❌ Bad: Expensive predicate
static bool BadPredicate(SyntaxNode node, CancellationToken ct)
{
    var compilation = ctx.SemanticModel.Compilation; // Too expensive!
    return true;
}

// ❌ Bad: Non-equatable model
public class BadModel
{
    public List<string> Items { get; set; } // No value equality
}

// ❌ Bad: Late semantic analysis
var provider = context.SyntaxProvider
    .CreateSyntaxProvider(predicate, transform)
    .Select(model => DoExpensiveSemanticAnalysis(model)); // Should be in transform
```

## Framework-Specific Patterns

### Query Generation Pattern

Example pattern for framework code generation:

```csharp
// Pattern for query generation
var queryMethods = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        "Guacamole.QueryAttribute",
        static (node, _) => node is MethodDeclarationSyntax,
        static (ctx, _) => new QueryMethod(
            Name: ctx.TargetSymbol.Name,
            ReturnType: ctx.TargetSymbol.ReturnType.ToDisplayString(),
            Parameters: ctx.TargetSymbol.Parameters
                .Select(p => new Parameter(p.Name, p.Type.ToDisplayString()))
                .ToEquatableArray()));

// Combine with project configuration
var withConfig = queryMethods
    .Combine(context.AnalyzerConfigOptionsProvider
        .Select(static (opts, _) => new Config(
            EnableLogging: opts.GlobalOptions.TryGetValue("build_property.GuacamoleLogging", out var logging)
                && bool.Parse(logging),
            OutputPath: opts.GlobalOptions.GetValueOrDefault("build_property.GuacamoleOutput", "Generated"))));
```

## Integration Recommendations

### MSBuild Integration

```xml
<!-- In .csproj -->
<ItemGroup>
  <Analyzer Include="MyGenerator.dll" />
  <AdditionalFiles Include="config.json" />
</ItemGroup>

<PropertyGroup>
  <MyGeneratorEnabled>true</MyGeneratorEnabled>
</PropertyGroup>
```

### IDE Debugging

Use `System.Diagnostics.Debugger.Launch()` in generator initialization for debugging, or use the new Visual Studio generator debugging features in VS 2022 17.8+.

## References

- [IIncrementalGenerator Documentation](https://docs.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator)
- [Source Generators Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)
- [Performance Best Practices](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Verify.SourceGenerators](https://github.com/VerifyTests/Verify.SourceGenerators)