using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
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
    private const int MaxNamedMembers = 5;

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

            compilationContext.RegisterSymbolStartAction(symbolContext => AnalyzeContext(symbolContext, types), SymbolKind.NamedType);
        });
    }

    private sealed record WellKnownTypes(
        INamedTypeSymbol Attribute,
        INamedTypeSymbol? ContextBase,
        INamedTypeSymbol? ValueProvider,
        INamedTypeSymbol? ValuesProvider,
        EqualityAnalysis Equality);

    private static void AnalyzeContext(SymbolStartAnalysisContext context, WellKnownTypes types)
    {
        var classSymbol = (INamedTypeSymbol)context.Symbol;
        if (classSymbol.TypeKind != TypeKind.Class
            || !classSymbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, types.Attribute)))
        {
            return;
        }

        // Constructor initializers, collected from the constructor bodies (not from generated code).
        var initializers = new ConcurrentDictionary<IMethodSymbol, ConstructorInitializer>(SymbolEqualityComparer.Default);
        context.RegisterOperationAction(operationContext =>
        {
            var body = (IConstructorBodyOperation)operationContext.Operation;
            if (operationContext.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } constructor
                && SymbolEqualityComparer.Default.Equals(constructor.ContainingType, classSymbol))
            {
                initializers[constructor] = GetInitializer(body);
            }
        }, OperationKind.ConstructorBody);

        context.RegisterSymbolEndAction(endContext =>
        {
            AnalyzeProperties(endContext, classSymbol, types);
            AnalyzeConstructors(endContext, classSymbol, types, initializers);
        });
    }

    private static void AnalyzeProperties(SymbolAnalysisContext context, INamedTypeSymbol classSymbol, WellKnownTypes types)
    {
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

            if (result.ReferenceMembers.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CollectionMemberComparedByReference,
                    location,
                    property.Name,
                    itemType!.ToDisplayString(),
                    DescribeMembers(result.ReferenceMembers)));
            }
        }
    }

    private static string DescribeMembers(IReadOnlyList<EqualityAnalysis.ReferenceMember> members)
    {
        var named = members
            .Take(MaxNamedMembers)
            .Select(m => $"'{m.Path}' ({m.Type.ToDisplayString(MemberTypeFormat)})");
        var text = string.Join(", ", named);
        return members.Count > MaxNamedMembers
            ? $"{text} and {members.Count - MaxNamedMembers} more"
            : text;
    }

    /// <summary>
    /// Reports GUAC0009 for every instance constructor that does not reach <c>: this()</c>, directly
    /// or through <c>: this(...)</c> chains, when the generator emits the parameterless constructor
    /// (the context is valid and declares no parameterless constructor itself, which is GUAC0008).
    /// </summary>
    private static void AnalyzeConstructors(
        SymbolAnalysisContext context,
        INamedTypeSymbol classSymbol,
        WellKnownTypes types,
        ConcurrentDictionary<IMethodSymbol, ConstructorInitializer> initializers)
    {
        if (initializers.IsEmpty)
            return;

        for (var type = classSymbol; type is not null; type = type.ContainingType)
        {
            if (!ContextAnalyzer.IsPartial(type, context.CancellationToken))
                return;
        }

        if (types.ContextBase is null || !ContextAnalyzer.DerivesFrom(classSymbol, types.ContextBase))
            return;

        // A user-declared parameterless constructor is GUAC0008; chains then reach the user's
        // constructor, not generated code. (The generated one is in generated code, so its body is
        // not among the collected ones.)
        if (initializers.Keys.Any(c => c.Parameters.Length == 0))
            return;

        var reaches = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        foreach (var constructor in initializers.Keys.OrderBy(c => c.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!Reaches(constructor))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConstructorMustChainToGeneratedConstructor,
                    GetConstructorIdentifierLocation(constructor, context.CancellationToken),
                    classSymbol.Name));
            }
        }

        // Only ': this()' with zero arguments reaches the generated constructor; ': base(...)' or no
        // initializer never does. Cycles (CS0516) count as not reaching. Targets whose bodies were
        // not collected (a primary constructor, an unresolved call) get the benefit of the doubt.
        bool Reaches(IMethodSymbol constructor)
        {
            if (reaches.TryGetValue(constructor, out var known))
                return known;

            reaches[constructor] = false; // provisional value guards against cycles
            var result = Evaluate(constructor);
            reaches[constructor] = result;
            return result;
        }

        bool Evaluate(IMethodSymbol constructor)
        {
            if (!initializers.TryGetValue(constructor, out var initializer))
                return true;

            return initializer.Kind switch
            {
                InitializerKind.ThisWithoutArguments => true,
                InitializerKind.ThisWithArguments => initializer.Target is null || Reaches(initializer.Target),
                _ => false,
            };
        }
    }

    private enum InitializerKind
    {
        /// <summary>No initializer, or <c>: base(...)</c>.</summary>
        None,

        /// <summary><c>: this()</c>.</summary>
        ThisWithoutArguments,

        /// <summary><c>: this(...)</c> with arguments.</summary>
        ThisWithArguments,
    }

    private readonly record struct ConstructorInitializer(InitializerKind Kind, IMethodSymbol? Target);

    private static ConstructorInitializer GetInitializer(IConstructorBodyOperation body)
    {
        if (body.Syntax is not ConstructorDeclarationSyntax { Initializer: { } syntax }
            || !syntax.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword))
        {
            return new ConstructorInitializer(InitializerKind.None, null);
        }

        if (syntax.ArgumentList.Arguments.Count == 0)
            return new ConstructorInitializer(InitializerKind.ThisWithoutArguments, null);

        var target = (body.Initializer as IExpressionStatementOperation)?.Operation is IInvocationOperation invocation
            ? invocation.TargetMethod
            : null;
        return new ConstructorInitializer(InitializerKind.ThisWithArguments, target);
    }

    private static Location GetConstructorIdentifierLocation(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        var reference = constructor.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference?.GetSyntax(cancellationToken) is ConstructorDeclarationSyntax declaration)
            return declaration.Identifier.GetLocation();

        return constructor.Locations.FirstOrDefault() ?? Location.None;
    }
}
