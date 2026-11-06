using System;
using Microsoft.CodeAnalysis;

namespace Guacamole.SourceGenerators;

/// <summary>
/// All diagnostics reported by the Guacamole source generator.
/// Keep AnalyzerReleases.Unshipped.md in sync when adding or changing rules.
/// </summary>
/// <remarks>
/// GUAC0001 (duplicate property name) and GUAC0002 (conflicting type parameters) were removed:
/// they analysed only properties declared directly on the context class, where C# already forbids
/// duplicate member names (CS0102), so they could never be reported. Their IDs are retired and
/// must not be reused.
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string UsageCategory = "Guacamole.SourceGenerator";
    private const string PerformanceCategory = "Guacamole.Performance";

    public static readonly DiagnosticDescriptor GetOnlyProviderMustBePartial = new(
        id: "GUAC0003",
        title: "IncrementalProvider property must be partial",
        messageFormat: "Get-only property '{0}' in context '{1}' must be declared as partial for auto-management. Use 'public partial IncrementalValueProvider<T> {0} {{ get; }}'.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Get-only IncrementalValueProvider and IncrementalValuesProvider properties of a computing context are implemented by the source generator and therefore must be declared partial.");

    public static readonly DiagnosticDescriptor MissingEqualityImplementation = new(
        id: "GUAC0004",
        title: "Type does not implement proper equality for incremental computation",
        messageFormat: "Type '{0}' in property '{1}' does not implement IEquatable<{0}>. For optimal incremental computation performance, use record types or implement IEquatable<T>. Without proper equality, cache invalidation will always occur even when values are logically equal.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Incremental computation relies on equality comparison to minimize recomputation. Types should implement IEquatable<T> or be records for structural equality.");

    public static readonly DiagnosticDescriptor UserManagedPropertyMustNotBePartial = new(
        id: "GUAC0005",
        title: "User-managed property should not be partial",
        messageFormat: "Property '{0}' in context '{1}' has a setter and should not be declared as partial. User-managed properties (with setters) should be declared normally: 'public IncrementalValueProvider<T> {0} {{ get; set; }}'.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Properties with setters are managed by user code and are not implemented by the source generator, so they must not be declared partial.");

    public static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "GUAC0006",
        title: "Computing context and its containing types must be partial",
        messageFormat: "Type '{0}' must be declared partial so that the computing context '{1}' can be extended by the source generator",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The source generator emits a partial declaration of the computing context class and of every type that contains it. All of these types must be declared partial.");

    public static readonly DiagnosticDescriptor ContextMustDeriveFromBase = new(
        id: "GUAC0007",
        title: "Computing context must derive from ComputingContextBase",
        messageFormat: "Class '{0}' is marked with [ComputingContext] but does not derive from Guacamole.ComputingContextBase",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated code relies on members of Guacamole.ComputingContextBase, so classes marked with [ComputingContext] must derive from it.");

    public static readonly DiagnosticDescriptor UserDeclaredParameterlessConstructor = new(
        id: "GUAC0008",
        title: "Computing context must not declare a parameterless constructor",
        messageFormat: "Computing context '{0}' must not declare a parameterless instance constructor: the source generator emits it to initialize the generated sources. Move the initialization logic into another constructor that chains to ': this()'.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The source generator emits the public parameterless constructor of a computing context, which initializes the generated source backing fields. A user-declared parameterless constructor would collide with it (CS0111), so the generator reports this error and omits its own constructor; the rest of the class is still generated so that no follow-up errors are produced.");

    public static readonly DiagnosticDescriptor ConstructorMustChainToGeneratedConstructor = new(
        id: "GUAC0009",
        title: "Computing context constructor must chain to ': this()'",
        messageFormat: "Constructor of computing context '{0}' must chain to ': this()' so generated sources are initialized",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Generated sources are initialized by the generated parameterless constructor. Every other instance constructor must, directly or through other constructors, chain to ': this()'; otherwise the sources stay null and accessing them throws at runtime.");

    public static DiagnosticDescriptor GetById(string id) => id switch
    {
        "GUAC0003" => GetOnlyProviderMustBePartial,
        "GUAC0004" => MissingEqualityImplementation,
        "GUAC0005" => UserManagedPropertyMustNotBePartial,
        "GUAC0006" => ContextMustBePartial,
        "GUAC0007" => ContextMustDeriveFromBase,
        "GUAC0008" => UserDeclaredParameterlessConstructor,
        "GUAC0009" => ConstructorMustChainToGeneratedConstructor,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown diagnostic id"),
    };
}
