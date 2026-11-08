using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Reports the warnings about [ComputingContext] classes that users may want to suppress:
/// GUAC0004 (provider value type without value equality), GUAC0009 (constructor that does not
/// chain to <c>: this()</c>) and GUAC0010 (collection members compared by reference by
/// compiler-generated equality).
/// </summary>
/// <remarks>
/// These used to be reported by <see cref="ComputingContextGenerator"/>, but Roslyn does not apply
/// <c>#pragma warning disable</c> or <c>[SuppressMessage]</c> to generator diagnostics (only
/// .editorconfig severities). Diagnostics of an analyzer get all suppression mechanisms. The errors
/// that decide whether code can be generated stay in the generator, so no diagnostic is reported twice.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComputingContextAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat MemberTypeFormat = SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    /// <summary>At most this many members are named in one GUAC0010 message.</summary>
    private const int MaxNamedMembers = EqualityAnalysis.MaxCollectedMembers;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.MissingEqualityImplementation,
        DiagnosticDescriptors.ConstructorMustChainToGeneratedConstructor,
        DiagnosticDescriptors.CollectionMemberComparedByReference,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            var attribute = compilation.GetTypeByMetadataName(ContextAnalyzer.ComputingContextAttributeName);
            if (attribute is null)
                return;

            var types = new WellKnownTypes(
                attribute,
                compilation.GetTypeByMetadataName(ContextAnalyzer.ComputingContextBaseName),
                compilation.GetTypeByMetadataName(ContextAnalyzer.ValueProviderName),
                compilation.GetTypeByMetadataName(ContextAnalyzer.ValuesProviderName),
                new EqualityAnalysis(compilation));

            compilationContext.RegisterSymbolAction(symbolContext => AnalyzeProperties(symbolContext, types), SymbolKind.NamedType);
            compilationContext.RegisterOperationAction(operationContext => AnalyzeConstructor(operationContext, types), OperationKind.ConstructorBody);
        });
    }

    private sealed record WellKnownTypes(
        INamedTypeSymbol Attribute,
        INamedTypeSymbol? ContextBase,
        INamedTypeSymbol? ValueProvider,
        INamedTypeSymbol? ValuesProvider,
        EqualityAnalysis Equality);

    private static void AnalyzeProperties(SymbolAnalysisContext context, WellKnownTypes types)
    {
        var classSymbol = (INamedTypeSymbol)context.Symbol;
        if (classSymbol.TypeKind != TypeKind.Class || !IsComputingContext(classSymbol, types))
            return;

        foreach (var property in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (property.IsStatic || property.IsIndexer)
                continue;

            if (!ContextAnalyzer.TryGetProviderKind(property.Type, types.ValueProvider, types.ValuesProvider, out _, out var itemType))
                continue;

            // Properties with GUAC0003/GUAC0005 errors are skipped, as the generator skips them.
            var hasSetter = property.SetMethod is not null;
            if (property.IsPartialDefinition ? hasSetter : !hasSetter)
                continue;

            var location = property.Locations.FirstOrDefault() ?? Location.None;
            var result = types.Equality.Analyze(itemType!);

            if (result.TypeWithoutEquality is { } typeWithoutEquality)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingEqualityImplementation,
                    location,
                    typeWithoutEquality.ToDisplayString(),
                    property.Name));
            }

            if (result.ReferenceMemberCount > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CollectionMemberComparedByReference,
                    location,
                    property.Name,
                    itemType!.ToDisplayString(),
                    DescribeMembers(result)));
            }
        }
    }

    private static string DescribeMembers(EqualityAnalysis.Result result)
    {
        var named = result.ReferenceMembers
            .Take(MaxNamedMembers)
            .Select(m => $"'{m.Path}' ({m.Type.ToDisplayString(MemberTypeFormat)})")
            .ToList();
        var text = string.Join(", ", named);
        return result.ReferenceMemberCount > named.Count
            ? $"{text} and {result.ReferenceMemberCount - named.Count} more"
            : text;
    }

    /// <summary>
    /// Reports GUAC0009 for an instance constructor of a [ComputingContext] class that does not reach
    /// <c>: this()</c>, directly or through <c>: this(...)</c> chains, when the generator emits the
    /// parameterless constructor (the context is valid and declares no parameterless constructor
    /// itself, which is GUAC0008).
    /// </summary>
    /// <remarks>
    /// Each constructor is checked from its own body, without a symbol-end action: Roslyn does not
    /// complete the symbol-end analysis of a type with partial constructors (C# 14). The chain is
    /// followed through the targets' declarations (the implementation part of a partial constructor);
    /// a <c>: this(...)</c> with arguments in another file than the analysed body cannot be bound
    /// without that file's semantic model and gets the benefit of the doubt.
    /// </remarks>
    private static void AnalyzeConstructor(OperationAnalysisContext context, WellKnownTypes types)
    {
        if (context.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false, Parameters.Length: > 0 } constructor
            || context.Operation.Syntax is not ConstructorDeclarationSyntax declaration
            || constructor.ContainingType is not { TypeKind: TypeKind.Class } classSymbol
            || !IsComputingContext(classSymbol, types))
        {
            return;
        }

        var cancellationToken = context.CancellationToken;
        for (var type = classSymbol; type is not null; type = type.ContainingType)
        {
            if (!ContextAnalyzer.IsPartial(type, cancellationToken))
                return;
        }

        if (types.ContextBase is null || !ContextAnalyzer.DerivesFrom(classSymbol, types.ContextBase))
            return;

        // A user-declared parameterless constructor is GUAC0008; chains then reach the user's
        // constructor, not generated code.
        if (classSymbol.InstanceConstructors.Any(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared && !IsGenerated(c, cancellationToken)))
            return;

        if (!Reaches(declaration, constructor, context.Operation.SemanticModel, cancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConstructorMustChainToGeneratedConstructor,
                declaration.Identifier.GetLocation(),
                classSymbol.Name));
        }
    }

    /// <summary>
    /// Only <c>: this()</c> with zero arguments reaches the generated constructor (in the final
    /// compilation it is the best overload for an empty argument list); <c>: base(...)</c> or no
    /// initializer never does. Cycles (CS0516) count as not reaching. Targets that cannot be followed
    /// (a primary constructor, an unresolved call, a call bound in another file) get the benefit of
    /// the doubt.
    /// </summary>
    private static bool Reaches(ConstructorDeclarationSyntax declaration, IMethodSymbol constructor, SemanticModel? semanticModel, CancellationToken cancellationToken)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { Normalize(constructor) };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var initializer = declaration.Initializer;
            if (initializer is null || !initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword))
                return false;

            if (initializer.ArgumentList.Arguments.Count == 0)
                return true;

            if (semanticModel is null || initializer.SyntaxTree != semanticModel.SyntaxTree)
                return true;

            if (semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol is not IMethodSymbol target || target.IsImplicitlyDeclared)
                return true;

            if (!visited.Add(Normalize(target)))
                return false;

            // The body (and initializer) of a partial constructor is in its implementation part.
            var implementation = target.PartialImplementationPart ?? target;
            if (implementation.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax next)
                return true; // e.g. a primary constructor: not analysed here

            declaration = next;
        }
    }

    /// <summary>The definition part of a partial constructor, otherwise the constructor itself.</summary>
    private static IMethodSymbol Normalize(IMethodSymbol constructor) => constructor.PartialDefinitionPart ?? constructor;

    /// <summary>
    /// Whether the constructor is declared only in generated code (the generator's parameterless
    /// constructor, in a file starting with an <c>&lt;auto-generated</c> comment).
    /// </summary>
    private static bool IsGenerated(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            var root = reference.SyntaxTree.GetRoot(cancellationToken);
            if (!root.GetLeadingTrivia().Any(t => t.ToString().Contains("<auto-generated")))
                return false;
        }

        return constructor.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool IsComputingContext(INamedTypeSymbol type, WellKnownTypes types)
        => type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, types.Attribute));
}
