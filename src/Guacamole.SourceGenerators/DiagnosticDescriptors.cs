using System;
using Microsoft.CodeAnalysis;

namespace Guacamole.SourceGenerators;

/// <summary>
/// All diagnostics of the Guacamole source generator package. Keep AnalyzerReleases.Unshipped.md
/// in sync when adding or changing rules.
/// </summary>
/// <remarks>
/// <para>
/// Diagnostics that decide whether code can be generated (GUAC0003, GUAC0005-GUAC0008, all errors)
/// are reported by <see cref="ComputingContextGenerator"/>. Warnings a user may want to suppress
/// (GUAC0004, GUAC0009, GUAC0010) are reported by <see cref="ComputingContextAnalyzer"/>: Roslyn does
/// not apply <c>#pragma warning disable</c> or <c>[SuppressMessage]</c> to generator diagnostics.
/// </para>
/// <para>
/// GUAC0001 (duplicate property name) and GUAC0002 (conflicting type parameters) were removed:
/// they analysed only properties declared directly on the context class, where C# already forbids
/// duplicate member names (CS0102), so they could never be reported. Their IDs are retired and
/// must not be reused.
/// </para>
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
        messageFormat: "Type '{0}' in property '{1}' does not implement IEquatable<{0}>, so equal values compare as different and every recomputation counts as a change. Use a record whose members all have value equality (for collection members, EquatableArray<T>), or implement IEquatable<T>.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Incremental computation relies on equality comparison to minimize recomputation. Provider value types should implement IEquatable<T> or be records. A record is only value-equal when all its members are: its compiler-generated Equals compares ImmutableArray<T>, arrays, lists, dictionaries and other collections by reference (see GUAC0010). Reported by an analyzer, so it can be suppressed with #pragma warning disable, [SuppressMessage] or .editorconfig.");

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

    public static readonly DiagnosticDescriptor CollectionMemberComparedByReference = new(
        id: "GUAC0010",
        title: "Compiler-generated equality compares collection members by reference",
        messageFormat: "Property '{0}': '{1}' relies on compiler-generated equality, which compares {2} by reference, so equal values count as changed and recomputation below this provider is never cut off. Use EquatableArray<T> for array members, or write Equals and GetHashCode that compare the collections element by element.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The compiler-generated Equals of a record (or the default Equals of a struct) compares each member with EqualityComparer<T>.Default, which compares ImmutableArray<T>, arrays, List<T>, dictionaries, sets, IEnumerable<T> and other collections by reference. A provider value that holds a newly built collection is then never equal to the previous one, even with the same elements, so the equality cut-off never fires. Nested records and tuples are checked too. Use Guacamole.EquatableArray<T> for such members, or declare Equals(T) and GetHashCode() that compare the collections element by element.");

    public static DiagnosticDescriptor GetById(string id) => id switch
    {
        "GUAC0003" => GetOnlyProviderMustBePartial,
        "GUAC0004" => MissingEqualityImplementation,
        "GUAC0005" => UserManagedPropertyMustNotBePartial,
        "GUAC0006" => ContextMustBePartial,
        "GUAC0007" => ContextMustDeriveFromBase,
        "GUAC0008" => UserDeclaredParameterlessConstructor,
        "GUAC0009" => ConstructorMustChainToGeneratedConstructor,
        "GUAC0010" => CollectionMemberComparedByReference,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown diagnostic id"),
    };
}
