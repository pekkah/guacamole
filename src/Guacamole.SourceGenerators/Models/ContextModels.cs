using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Guacamole.SourceGenerators.Models;

// The types in this file form the incremental pipeline model. They intentionally contain only
// strings, primitives, equatable structs (TextSpan, LinePositionSpan) and EquatableArray<T> so that
// value equality works and the generator pipeline can cache results between runs. They must never
// hold ISymbol, SyntaxNode, SemanticModel, Compilation, Location or Diagnostic instances.

/// <summary>
/// Result of analysing a single [ComputingContext] class: an optional model to generate code from
/// plus the diagnostics to report.
/// </summary>
internal sealed record ContextAnalysisResult(
    ContextModel? Model,
    EquatableArray<DiagnosticModel> Diagnostics);

/// <summary>
/// Everything needed to emit the generated partial class for a computing context.
/// </summary>
/// <param name="HintName">Unique hint name for the generated file.</param>
/// <param name="Namespace">Containing namespace, or <c>null</c> for the global namespace.</param>
/// <param name="ContainingTypes">Containing types from outermost to innermost.</param>
/// <param name="ClassName">Simple class name (used for the constructor).</param>
/// <param name="TypeParameterList">Type parameter list including angle brackets, or empty.</param>
/// <param name="Properties">Source-backed properties to implement.</param>
/// <param name="GenerateConstructor">
/// False when the user declared a parameterless constructor (GUAC0008); the generated one is then omitted.
/// </param>
internal sealed record ContextModel(
    string HintName,
    string? Namespace,
    EquatableArray<ContainingTypeModel> ContainingTypes,
    string ClassName,
    string TypeParameterList,
    EquatableArray<SourcePropertyModel> Properties,
    bool GenerateConstructor);

/// <summary>
/// A type that contains the computing context class (for nested classes).
/// </summary>
/// <param name="Keyword">Declaration keyword, e.g. <c>class</c>, <c>struct</c> or <c>record</c>.</param>
/// <param name="NameWithTypeParameters">Type name including any type parameter list.</param>
internal sealed record ContainingTypeModel(string Keyword, string NameWithTypeParameters);

/// <summary>
/// A partial get-only provider property that the generator implements with an incremental source.
/// </summary>
/// <param name="Name">Property name.</param>
/// <param name="ItemType">Fully qualified item type (<c>global::</c> prefixed).</param>
/// <param name="IsCollection">True for IncrementalValuesProvider, false for IncrementalValueProvider.</param>
/// <param name="Accessibility">Declared accessibility keywords of the property, e.g. <c>public</c>.</param>
internal sealed record SourcePropertyModel(
    string Name,
    string ItemType,
    bool IsCollection,
    string Accessibility);

/// <summary>
/// Equatable representation of a diagnostic to report.
/// </summary>
internal sealed record DiagnosticModel(
    string Id,
    LocationModel? Location,
    EquatableArray<string> MessageArguments)
{
    public Diagnostic ToDiagnostic()
    {
        var descriptor = DiagnosticDescriptors.GetById(Id);
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        return Diagnostic.Create(descriptor, location, [.. MessageArguments]);
    }
}

/// <summary>
/// Equatable representation of a source location that can be turned back into a <see cref="Microsoft.CodeAnalysis.Location"/>.
/// </summary>
internal sealed record LocationModel(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationModel? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
            return null;

        return new LocationModel(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}
