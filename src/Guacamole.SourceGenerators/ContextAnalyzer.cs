using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Guacamole.SourceGenerators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Performs all semantic analysis of a [ComputingContext] class and converts it into an
/// equatable <see cref="ContextAnalysisResult"/>. This is the only place where symbols are used.
/// </summary>
/// <remarks>
/// Property classification:
/// <list type="bullet">
/// <item>Partial get-only provider properties are input sources: the generator implements them.</item>
/// <item>Non-partial provider properties with setters are user-managed (derived) properties.</item>
/// <item>Non-partial get-only provider properties are reported (GUAC0003).</item>
/// <item>Partial provider properties with setters are reported (GUAC0005).</item>
/// </list>
/// Constructors: the generator emits the public parameterless constructor that initializes the
/// sources. A user-declared parameterless constructor is reported (GUAC0008, error) and the generated
/// constructor is omitted, so the only error is GUAC0008 instead of CS0111 plus follow-ups. Every other
/// instance constructor must reach <c>: this()</c>, directly or through other constructors (GUAC0009).
/// </remarks>
internal static class ContextAnalyzer
{
    public const string ComputingContextAttributeName = "Guacamole.ComputingContextAttribute";
    private const string ComputingContextBaseName = "Guacamole.ComputingContextBase";
    private const string ValueProviderName = "Guacamole.IncrementalValueProvider`1";
    private const string ValuesProviderName = "Guacamole.IncrementalValuesProvider`1";

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ContextAnalysisResult Analyze(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var diagnostics = new List<DiagnosticModel>();
        var canGenerate = true;

        // The context class and every containing type must be partial.
        for (var type = classSymbol; type is not null; type = type.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPartial(type, cancellationToken))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticDescriptors.ContextMustBePartial,
                    GetIdentifierLocation(type, cancellationToken),
                    type.Name,
                    classSymbol.Name));
                canGenerate = false;
            }
        }

        // Generated code relies on ComputingContextBase members (CreateSource, CreateValueSource).
        var baseType = compilation.GetTypeByMetadataName(ComputingContextBaseName);
        if (baseType is null || !DerivesFrom(classSymbol, baseType))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticDescriptors.ContextMustDeriveFromBase,
                GetIdentifierLocation(classSymbol, cancellationToken),
                classSymbol.Name));
            canGenerate = false;
        }

        var valueProvider = compilation.GetTypeByMetadataName(ValueProviderName);
        var valuesProvider = compilation.GetTypeByMetadataName(ValuesProviderName);
        var equatableType = compilation.GetTypeByMetadataName("System.IEquatable`1");

        var sourceProperties = new List<SourcePropertyModel>();

        foreach (var property in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (property.IsStatic || property.IsIndexer)
                continue;

            if (!TryGetProviderKind(property.Type, valueProvider, valuesProvider, out var isCollection, out var itemType))
                continue;

            var hasSetter = property.SetMethod is not null;
            var location = property.Locations.FirstOrDefault();

            if (property.IsPartialDefinition)
            {
                if (hasSetter)
                {
                    // Partial property with setter: setters mean user-managed, which shouldn't be partial.
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticDescriptors.UserManagedPropertyMustNotBePartial,
                        location,
                        property.Name,
                        classSymbol.Name));
                    continue;
                }

                // Partial get-only provider: an input source implemented by the generator,
                // unless the user already wrote the implementation part.
                if (property.PartialImplementationPart is null)
                {
                    sourceProperties.Add(new SourcePropertyModel(
                        property.Name,
                        itemType!.ToDisplayString(FullyQualifiedNullableFormat),
                        isCollection,
                        GetAccessibilityModifiers(property, cancellationToken)));
                }
            }
            else if (!hasSetter)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticDescriptors.GetOnlyProviderMustBePartial,
                    location,
                    property.Name,
                    classSymbol.Name));
                continue;
            }

            // Both input and user-managed providers benefit from item types with value equality.
            if (RequiresEqualityCheck(itemType!) && !HasProperEquality(itemType!, equatableType))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticDescriptors.MissingEqualityImplementation,
                    location,
                    itemType!.ToDisplayString(),
                    property.Name));
            }
        }

        ContextModel? model = null;
        if (canGenerate)
        {
            var generateConstructor = AnalyzeConstructors(context, classSymbol, diagnostics, cancellationToken);

            var containingTypes = new List<ContainingTypeModel>();
            for (var type = classSymbol.ContainingType; type is not null; type = type.ContainingType)
            {
                containingTypes.Insert(0, new ContainingTypeModel(GetTypeKeyword(type), GetNameWithTypeParameters(type)));
            }

            model = new ContextModel(
                GetHintName(classSymbol),
                classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString(),
                containingTypes.ToEquatableArray(),
                classSymbol.Name,
                GetTypeParameterList(classSymbol),
                sourceProperties.ToEquatableArray(),
                generateConstructor);
        }

        return new ContextAnalysisResult(model, diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Reports GUAC0008/GUAC0009 for user-declared instance constructors.
    /// </summary>
    /// <returns>True when the generated parameterless constructor should be emitted.</returns>
    private static bool AnalyzeConstructors(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol classSymbol,
        List<DiagnosticModel> diagnostics,
        CancellationToken cancellationToken)
    {
        var constructors = classSymbol.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();

        var parameterless = constructors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterless is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticDescriptors.UserDeclaredParameterlessConstructor,
                GetConstructorIdentifierLocation(parameterless, cancellationToken),
                classSymbol.Name));

            // Chains to ': this()' reach the user's constructor, not generated code, so GUAC0009
            // would be misleading here; fixing GUAC0008 comes first.
            return false;
        }

        var reaches = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        foreach (var constructor in constructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReachesGeneratedConstructor(context, constructor, reaches, cancellationToken))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticDescriptors.ConstructorMustChainToGeneratedConstructor,
                    GetConstructorIdentifierLocation(constructor, cancellationToken),
                    classSymbol.Name));
            }
        }

        return true;
    }

    /// <summary>
    /// Follows <c>: this(...)</c> initializers. Only <c>: this()</c> with zero arguments reaches the
    /// generated constructor (in the final compilation it is the best overload for an empty argument
    /// list). <c>: base(...)</c> or no initializer never reaches it. Cycles (CS0516) count as not reaching.
    /// Unresolvable targets are given the benefit of the doubt to avoid false positives.
    /// </summary>
    private static bool ReachesGeneratedConstructor(
        GeneratorAttributeSyntaxContext context,
        IMethodSymbol constructor,
        Dictionary<IMethodSymbol, bool> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(constructor, out var known))
            return known;

        // Provisional value guards against cycles.
        cache[constructor] = false;

        var result = Evaluate();
        cache[constructor] = result;
        return result;

        bool Evaluate()
        {
            var reference = constructor.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference?.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration)
                return true; // e.g. a primary constructor: not analysed here

            var initializer = declaration.Initializer;
            if (initializer is null || !initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
                return false;

            if (initializer.ArgumentList.Arguments.Count == 0)
                return true;

            var semanticModel = initializer.SyntaxTree == context.SemanticModel.SyntaxTree
                ? context.SemanticModel
                : context.SemanticModel.Compilation.GetSemanticModel(initializer.SyntaxTree);

            if (semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol is not IMethodSymbol target)
                return true;

            if (target.IsImplicitlyDeclared)
                return true;

            return ReachesGeneratedConstructor(context, target, cache, cancellationToken);
        }
    }

    private static Location? GetConstructorIdentifierLocation(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        var reference = constructor.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference?.GetSyntax(cancellationToken) is ConstructorDeclarationSyntax declaration)
            return declaration.Identifier.GetLocation();

        return constructor.Locations.FirstOrDefault();
    }

    private static DiagnosticModel CreateDiagnostic(DiagnosticDescriptor descriptor, Location? location, params string[] arguments)
        => new(descriptor.Id, LocationModel.From(location), arguments.ToEquatableArray());

    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return type.DeclaringSyntaxReferences.Length > 0;
    }

    private static Location? GetIdentifierLocation(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var reference = type.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference?.GetSyntax(cancellationToken) is BaseTypeDeclarationSyntax declaration)
            return declaration.Identifier.GetLocation();

        return type.Locations.FirstOrDefault();
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    private static bool TryGetProviderKind(
        ITypeSymbol type,
        INamedTypeSymbol? valueProvider,
        INamedTypeSymbol? valuesProvider,
        out bool isCollection,
        out ITypeSymbol? itemType)
    {
        isCollection = false;
        itemType = null;

        if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
            return false;

        var definition = namedType.ConstructedFrom;
        if (valuesProvider is not null && SymbolEqualityComparer.Default.Equals(definition, valuesProvider))
        {
            isCollection = true;
        }
        else if (valueProvider is null || !SymbolEqualityComparer.Default.Equals(definition, valueProvider))
        {
            return false;
        }

        itemType = namedType.TypeArguments[0];
        return true;
    }

    /// <summary>
    /// Returns the accessibility modifiers exactly as written on the property declaration, because
    /// the implementing part of a partial property must repeat them.
    /// </summary>
    private static string GetAccessibilityModifiers(IPropertySymbol property, CancellationToken cancellationToken)
    {
        var reference = property.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference?.GetSyntax(cancellationToken) is not BasePropertyDeclarationSyntax declaration)
            return "public";

        var builder = new StringBuilder();
        foreach (var modifier in declaration.Modifiers)
        {
            switch (modifier.Kind())
            {
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.InternalKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PrivateKeyword:
                    if (builder.Length > 0)
                        builder.Append(' ');
                    builder.Append(modifier.Text);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Primitive types (int, bool, char, ...) have built-in equality; string is checked (and passes).
    /// Type parameters are skipped because their equality depends on the type argument.
    /// Everything else needs checking.
    /// </summary>
    private static bool RequiresEqualityCheck(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.TypeParameter)
            return false;

        if (type.SpecialType != SpecialType.None)
            return type.SpecialType == SpecialType.System_String;

        return true;
    }

    /// <summary>
    /// Records, IEquatable&lt;T&gt; implementations, primitives and enums have proper equality.
    /// </summary>
    private static bool HasProperEquality(ITypeSymbol type, INamedTypeSymbol? equatableType)
    {
        if (type.IsRecord)
            return true;

        if (equatableType is not null && type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, equatableType) &&
                SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)))
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None)
            return true;

        return type.TypeKind == TypeKind.Enum;
    }

    private static string GetTypeKeyword(INamedTypeSymbol type)
    {
        if (type.IsRecord)
            return type.TypeKind == TypeKind.Struct ? "record struct" : "record";

        return type.TypeKind switch
        {
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class",
        };
    }

    private static string GetTypeParameterList(INamedTypeSymbol type)
        => type.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", type.TypeParameters.Select(p => p.Name)) + ">";

    private static string GetNameWithTypeParameters(INamedTypeSymbol type)
        => type.Name + GetTypeParameterList(type);

    /// <summary>
    /// Builds a hint name that is unique per type: namespace, containing types (joined with '+')
    /// and metadata names (which include generic arity), e.g. <c>MyApp.Outer+Context`1.g.cs</c>.
    /// </summary>
    private static string GetHintName(INamedTypeSymbol type)
    {
        var typeName = type.MetadataName;
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            typeName = containing.MetadataName + "+" + typeName;
        }

        var ns = type.ContainingNamespace;
        var prefix = ns is null || ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString() + ".";
        return prefix + typeName + ".g.cs";
    }
}
