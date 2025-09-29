# Research: Query API Design for Guacamole Framework

## Source Generator Best Practices for Query Registration

**Decision**: Use compile-time source generators with IIncrementalGenerator interface
**Rationale**:
- Provides strong typing for QueryContext with IDE autocomplete support
- Zero runtime overhead compared to reflection-based approaches
- Enables circular dependency detection at compile time
- Integrates with existing Verify.SourceGenerators testing infrastructure

**Alternatives considered**:
- Runtime reflection: Slower performance, no compile-time validation
- Manual registration: High boilerplate, error-prone
- Template-based generation: No IDE integration, brittle maintenance

## Incremental Source Generator Patterns

**Decision**: Use syntax provider pipeline pattern for query class discovery
**Rationale**:
- Only regenerates when query classes change
- Efficient filtering of relevant syntax nodes
- Composable pipeline for complex transformations
- Aligns with Roslyn performance recommendations

**Implementation Pattern**:
```csharp
var provider = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: (node, _) => IsQueryAttributeClass(node),
        transform: (ctx, _) => ExtractQueryInfo(ctx))
    .Where(info => info is not null);

context.RegisterSourceOutput(provider, GenerateQueryRegistration);

static bool IsQueryAttributeClass(SyntaxNode node) =>
    node is ClassDeclarationSyntax cls &&
    cls.AttributeLists.Any(al => al.Attributes.Any(a =>
        a.Name.ToString().Contains("Query")));
```

## Result<T> Pattern for Error Handling

**Decision**: Implement Result<T> with Success/Failure states
**Rationale**:
- Explicit error handling without exceptions in hot paths
- Composable with LINQ-style operators
- Aligns with functional programming principles in query contexts
- Better performance than try-catch for expected error scenarios

**Alternatives considered**:
- Exceptions: Performance overhead in query hot paths
- Optional<T>: Doesn't capture error information
- Tuples: Not strongly typed, lacks composition

## QueryContext Design Pattern

**Decision**: Generate strongly-typed QueryContext extensions for each query class decorated with [Query]
**Rationale**:
- Full IDE support with autocomplete and refactoring
- Compile-time safety for query dependencies
- Clear discovery mechanism for available queries
- Zero runtime lookup overhead

**Generated Pattern**:
```csharp
public static partial class QueryContextExtensions
{
    public static async Task<Result<int>> CalculateLength(
        this QueryContext context,
        string input,
        CancellationToken cancellationToken = default) =>
        await context.ExecuteAsync<CalculateLengthQuery, string, int>(
            input, cancellationToken);

    public static async Task<Result<bool>> ValidateInput(
        this QueryContext context,
        string input,
        CancellationToken cancellationToken = default) =>
        await context.ExecuteAsync<ValidateInputQuery, string, bool>(
            input, cancellationToken);
}
```

## Dependency Injection Integration

**Decision**: Use Microsoft.Extensions.DependencyInjection with automatic query registration
**Rationale**:
- Constitutional requirement
- Mature ecosystem with good tooling
- Supports scoped and singleton lifetimes
- Integrates with ASP.NET Core and other .NET frameworks

**Registration Pattern**:
```csharp
services.AddGuacamole(options =>
{
    options.RegisterQueriesFromAssembly(typeof(MyQuery).Assembly);
});
```

## Circular Dependency Detection Strategy

**Decision**: Implement both compile-time analysis and runtime guards
**Rationale**:
- Compile-time warnings for early detection during development
- Runtime guards as safety net for complex scenarios
- Allows for gradual migration and validation

**Implementation Approach**:
- Source generator analyzes dependency graph and emits warnings
- Runtime uses stack tracking during query execution
- Clear error messages with suggested fixes

## Snapshot Testing for Source Generator Validation

**Decision**: Use Verify.SourceGenerators with XUnit v3 for PoC validation
**Rationale**:
- Captures exact generated code for review
- Detects unintended changes in code generation
- Integrates with existing Guacamole testing infrastructure
- Provides clear diff visualization for changes

**Test Pattern**:
```csharp
[Fact]
public Task GeneratesQueryContextCorrectly()
{
    var source = @"
    [Query]
    public class TestQuery
    {
        public async Task<Result<int>> ExecuteAsync(
            QueryContext context,
            string input,
            CancellationToken cancellationToken = default)
        {
            return Result.Success(input.Length);
        }
    }";

    return TestHelper.Verify(source);
}
```

## Salsa-rs Alignment Analysis

**Decision**: Validate PoC against salsa-rs incremental computation principles
**Rationale**:
- Salsa-rs is proven implementation of incremental computation
- Ensures Guacamole follows established patterns
- Validates architectural decisions against real-world usage
- Identifies gaps in Guacamole's approach

**Key Alignment Points**:
- Memoization strategy (salsa uses LRU with dependency tracking)
- Dependency tracking (salsa uses fine-grained invalidation)
- Query composition (salsa supports derived queries)
- Error propagation (salsa cancels dependent computations on errors)

**Validation Approach**:
- Compare PoC generated code against equivalent salsa patterns
- Document similarities and justified differences
- Identify performance characteristics alignment
- Create validation document for architectural review