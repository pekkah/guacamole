using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Analyzes properties in computing context classes to classify them as input sources or derived computations.
/// Properties with setters are input sources (generate mutation methods).
/// Properties without setters are derived computations (read-only).
/// </summary>
internal static class PropertyAnalyzer
{
    /// <summary>
    /// Analyzes all properties in a context class and returns classification results.
    /// </summary>
    public static PropertyAnalysisResult Analyze(INamedTypeSymbol classSymbol, SourceProductionContext context)
    {
        var inputProperties = new List<PropertyInfo>();
        var derivedProperties = new List<PropertyInfo>();
        var diagnostics = new List<Diagnostic>();

        var properties = classSymbol.GetMembers().OfType<IPropertySymbol>();

        foreach (var property in properties)
        {
            // Skip inherited properties from base class
            if (!SymbolEqualityComparer.Default.Equals(property.ContainingType, classSymbol))
                continue;

            // Only process partial properties (C# 13 feature)
            // Partial properties have IsPartialDefinition = true for the defining declaration
            if (!property.IsPartialDefinition)
            {
                // Non-partial properties should produce a diagnostic
                if (IsIncrementalProvider(property.Type))
                {
                    var diagnostic = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GUAC0003",
                            "IncrementalProvider property must be partial",
                            "Property '{0}' in context '{1}' must be declared as partial. Use 'public partial IncrementalValueProvider<T> {0} {{ get; set; }}'",
                            "Guacamole.SourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None,
                        property.Name,
                        classSymbol.Name);
                    diagnostics.Add(diagnostic);
                }
                continue;
            }

            // Check if property is IncrementalValueProvider or IncrementalValuesProvider
            var propertyType = property.Type;
            if (!IsIncrementalProvider(propertyType))
                continue;

            var propertyInfo = new PropertyInfo(
                property.Name,
                propertyType.Name,
                GetGenericTypeArgument(propertyType),
                property.SetMethod != null,
                IsCollectionProvider(propertyType));

            if (property.SetMethod != null)
            {
                inputProperties.Add(propertyInfo);
            }
            else
            {
                derivedProperties.Add(propertyInfo);
            }
        }

        // T029a: Detect circular dependencies (duplicate property names causing conflicts)
        DetectDuplicateProperties(inputProperties, derivedProperties, diagnostics, classSymbol);

        // T029b: Detect conflicting type parameters
        DetectConflictingTypeParameters(inputProperties, derivedProperties, diagnostics, classSymbol);

        return new PropertyAnalysisResult(
            inputProperties,
            derivedProperties,
            diagnostics);
    }

    /// <summary>
    /// Detects duplicate property names that would cause circular reference issues in generated code.
    /// Produces diagnostic GUAC0001.
    /// </summary>
    private static void DetectDuplicateProperties(
        List<PropertyInfo> inputProperties,
        List<PropertyInfo> derivedProperties,
        List<Diagnostic> diagnostics,
        INamedTypeSymbol classSymbol)
    {
        var allProperties = inputProperties.Concat(derivedProperties).ToList();
        var propertyGroups = allProperties.GroupBy(p => p.Name).Where(g => g.Count() > 1);

        foreach (var group in propertyGroups)
        {
            var propertyNames = string.Join(", ", group.Select(p => $"{p.Name} ({p.ItemType})"));
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "GUAC0001",
                    "Duplicate property name detected",
                    "Property '{0}' is declared multiple times in context '{1}'. This creates a circular reference. Property chain: {2}",
                    "Guacamole.SourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None,
                group.Key,
                classSymbol.Name,
                propertyNames);

            diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Detects properties with the same name but different type parameters in the inheritance chain.
    /// Produces diagnostic GUAC0002.
    /// </summary>
    private static void DetectConflictingTypeParameters(
        List<PropertyInfo> inputProperties,
        List<PropertyInfo> derivedProperties,
        List<Diagnostic> diagnostics,
        INamedTypeSymbol classSymbol)
    {
        // Check for properties that have the same name but different generic type arguments
        var allProperties = inputProperties.Concat(derivedProperties).ToList();
        var propertyGroups = allProperties.GroupBy(p => p.Name);

        foreach (var group in propertyGroups)
        {
            var distinctTypes = group.Select(p => p.ItemType).Distinct().ToList();
            if (distinctTypes.Count > 1)
            {
                var conflictingTypes = string.Join(", ", distinctTypes);
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "GUAC0002",
                        "Conflicting type parameters detected",
                        "Property '{0}' in context '{1}' has conflicting type parameters: {2}. All properties with the same name must have the same type parameter.",
                        "Guacamole.SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    group.Key,
                    classSymbol.Name,
                    conflictingTypes);

                diagnostics.Add(diagnostic);
            }
        }
    }

    private static bool IsIncrementalProvider(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            return false;

        var fullName = namedType.ConstructedFrom.ToDisplayString();
        return fullName == "Guacamole.Core.Providers.IncrementalValueProvider<T>" ||
               fullName == "Guacamole.Core.Providers.IncrementalValuesProvider<T>";
    }

    private static bool IsCollectionProvider(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            return false;

        var fullName = namedType.ConstructedFrom.ToDisplayString();
        return fullName == "Guacamole.Core.Providers.IncrementalValuesProvider<T>";
    }

    private static string GetGenericTypeArgument(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
        {
            return namedType.TypeArguments[0].ToDisplayString();
        }

        return "object";
    }
}

/// <summary>
/// Information about a property in a computing context.
/// </summary>
internal class PropertyInfo
{
    public string Name { get; }
    public string ProviderType { get; }
    public string ItemType { get; }
    public bool HasSetter { get; }
    public bool IsCollection { get; }

    public PropertyInfo(string name, string providerType, string itemType, bool hasSetter, bool isCollection)
    {
        Name = name;
        ProviderType = providerType;
        ItemType = itemType;
        HasSetter = hasSetter;
        IsCollection = isCollection;
    }
}

/// <summary>
/// Results of property analysis for a computing context.
/// </summary>
internal class PropertyAnalysisResult
{
    public List<PropertyInfo> InputProperties { get; }
    public List<PropertyInfo> DerivedProperties { get; }
    public List<Diagnostic> Diagnostics { get; }

    public PropertyAnalysisResult(
        List<PropertyInfo> inputProperties,
        List<PropertyInfo> derivedProperties,
        List<Diagnostic> diagnostics)
    {
        InputProperties = inputProperties;
        DerivedProperties = derivedProperties;
        Diagnostics = diagnostics;
    }
}
