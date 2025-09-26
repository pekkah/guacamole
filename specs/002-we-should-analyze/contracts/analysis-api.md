# Analysis API Contract

## ISalsaAnalysisService

Interface for analyzing salsa-rs concepts and generating .NET adaptations.

```csharp
public interface ISalsaAnalysisService
{
    /// <summary>
    /// Analyzes a salsa-rs concept and extracts key patterns
    /// </summary>
    /// <param name="conceptName">Name of the concept to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis result with Rust implementation details</returns>
    Task<SalsaConceptAnalysis> AnalyzeConceptAsync(
        string conceptName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates .NET adaptation for a analyzed salsa concept
    /// </summary>
    /// <param name="concept">The analyzed concept</param>
    /// <param name="targetFeatures">Preferred .NET features to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>C# adaptation with implementation guidance</returns>
    Task<DotNetAdaptation> GenerateAdaptationAsync(
        SalsaConceptAnalysis concept,
        DotNetFeatureSet targetFeatures,
        CancellationToken cancellationToken = default);
}
```

## IPerformanceBenchmarkService

Interface for defining and validating performance characteristics.

```csharp
public interface IPerformanceBenchmarkService
{
    /// <summary>
    /// Creates BenchmarkDotNet specification for a .NET adaptation
    /// </summary>
    /// <param name="adaptation">The adaptation to benchmark</param>
    /// <param name="benchmarkCategories">Types of benchmarks to create</param>
    /// <returns>Benchmark definition ready for execution</returns>
    PerformanceBenchmarkDefinition CreateBenchmark(
        DotNetAdaptation adaptation,
        BenchmarkCategory[] benchmarkCategories);

    /// <summary>
    /// Validates that performance meets constitutional O(changes) requirement
    /// </summary>
    /// <param name="benchmarkResults">Results from BenchmarkDotNet execution</param>
    /// <returns>Validation result with compliance status</returns>
    PerformanceValidationResult ValidateComplexity(
        BenchmarkResults benchmarkResults);
}
```

## IIntegrationPatternService

Interface for generating Microsoft DI integration patterns.

```csharp
public interface IIntegrationPatternService
{
    /// <summary>
    /// Generates service registration patterns for DI container
    /// </summary>
    /// <param name="adaptation">The adaptation needing DI integration</param>
    /// <param name="serviceLifetime">Desired service lifetime</param>
    /// <returns>Integration guide with registration examples</returns>
    IntegrationGuide GenerateServiceRegistration(
        DotNetAdaptation adaptation,
        ServiceLifetime serviceLifetime);

    /// <summary>
    /// Creates configuration management patterns
    /// </summary>
    /// <param name="adaptation">The adaptation needing configuration</param>
    /// <returns>Configuration examples using Microsoft.Extensions.Options</returns>
    ConfigurationPattern GenerateConfigurationPattern(
        DotNetAdaptation adaptation);
}
```

## Data Transfer Objects

### SalsaConceptAnalysis
```csharp
public record SalsaConceptAnalysis(
    string ConceptId,
    string Name,
    ConceptCategory Category,
    string RustImplementation,
    ComplexityLevel Complexity,
    string[] Prerequisites)
{
    public bool IsReadyForAdaptation => Prerequisites.All(p => !string.IsNullOrEmpty(p));
}
```

### DotNetAdaptation
```csharp
public record DotNetAdaptation(
    string AdaptationId,
    string CSharpPattern,
    DotNetFeature[] RequiredFeatures,
    PerformanceCharacteristics Performance,
    bool ConstitutionalCompliance,
    string ExampleUsage)
{
    public bool IsConstitutionallyCompliant => ConstitutionalCompliance;
}
```

### DotNetFeatureSet
```csharp
[Flags]
public enum DotNetFeature
{
    Spans = 1,
    SourceGenerators = 2,
    ValueTypes = 4,
    NullableReferenceTypes = 8,
    PatternMatching = 16,
    AsyncStreams = 32
}
```

### BenchmarkCategory
```csharp
public enum BenchmarkCategory
{
    Memory,
    CPU,
    Throughput,
    Latency,
    Allocation
}
```

## Error Responses

### AnalysisException
Thrown when salsa-rs concept analysis fails
- `ConceptNotFound`: Specified concept doesn't exist in salsa-rs
- `InsufficientDocumentation`: Not enough information to analyze pattern
- `ComplexityTooHigh`: Concept too complex for current analysis capability

### AdaptationException
Thrown when .NET adaptation generation fails
- `FeatureNotSupported`: Requested .NET feature not available in target version
- `ConstitutionalViolation`: Generated pattern violates project requirements
- `PerformanceRegression`: Adaptation shows worse than O(changes) complexity

### IntegrationException
Thrown when DI integration pattern generation fails
- `ServiceLifetimeConflict`: Requested lifetime incompatible with pattern
- `DependencyCycleDetected`: Integration would create circular dependencies
- `ConfigurationMismatch`: Configuration pattern incompatible with adaptation