using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Incremental source generator for ComputingContext marked classes.
/// Generates source-backed properties with push-based reactive updates.
/// </summary>
[Generator]
public class ComputingContextGeneratorNew : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attribute for debugging
        context.RegisterPostInitializationOutput(ctx =>
        {
            // Attributes are already defined in Guacamole assembly
            // No need to generate them
        });

        // Find all classes marked with [ComputingContext] attribute
        var computingContextClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsComputingContextCandidate(node),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Combine with compilation
        var compilationAndClasses = context.CompilationProvider.Combine(computingContextClasses.Collect());

        // Generate code for each context
        context.RegisterSourceOutput(compilationAndClasses, static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    private static bool IsComputingContextCandidate(SyntaxNode node)
    {
        // Look for class declarations with attributes
        return node is ClassDeclarationSyntax classDecl && classDecl.AttributeLists.Count > 0;
    }

    private static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Check if class has ComputingContext attribute
        foreach (var attributeList in classDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol;
                if (symbol is IMethodSymbol attrSymbol)
                {
                    var attrType = attrSymbol.ContainingType.ToDisplayString();
                    if (attrType == "Guacamole.ComputingContextAttribute")
                    {
                        return classDecl;
                    }
                }
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
            return;

        foreach (var classDecl in classes.Distinct())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var symbol = semanticModel.GetDeclaredSymbol(classDecl);

            if (symbol is not INamedTypeSymbol classSymbol)
                continue;

            GenerateContextExtensions(context, classSymbol, classDecl);
        }
    }

    private static void GenerateContextExtensions(
        SourceProductionContext context,
        INamedTypeSymbol classSymbol,
        ClassDeclarationSyntax classDecl)
    {
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;

        // Check if this is a nested class and build the containing type hierarchy
        var containingTypes = new System.Collections.Generic.Stack<string>();
        var currentContaining = classSymbol.ContainingType;
        while (currentContaining != null)
        {
            containingTypes.Push(currentContaining.Name);
            currentContaining = currentContaining.ContainingType;
        }

        // Analyze properties
        var analysisResult = PropertyAnalyzer.Analyze(classSymbol, context);

        // Report any diagnostics
        foreach (var diagnostic in analysisResult.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }

        // Generate source-backed properties
        var sourceFields = string.Join("\n        ", analysisResult.InputProperties.Select(p => CodeGenerators.SourcePropertyGenerator.GenerateSourceField(p, className)));
        var propertyImplementations = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.SourcePropertyGenerator.GeneratePropertyImplementation));
        var sourceAccessors = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.SourcePropertyGenerator.GenerateSourceAccessor));
        var sourceInitializations = string.Join("\n            ", analysisResult.InputProperties.Select(p => CodeGenerators.SourcePropertyGenerator.GenerateSourceInitialization(p, className)));

        // Generate backward compatibility methods with batching support
        var batchingInfrastructure = CodeGenerators.SourcePropertyGenerator.GenerateBatchingInfrastructure();
        var mutationMethods = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.SourcePropertyGenerator.GenerateBackwardCompatibilityMethods));
        var saveChangesMethod = CodeGenerators.SourcePropertyGenerator.GenerateSaveChangesMethod();

        // Generate constructor
        var constructor = $$"""

                /// <summary>
                /// Initializes the computing context and its sources.
                /// </summary>
                public {{className}}() : base()
                {
                    {{sourceInitializations}}
                }

        """;

        // Build the opening for nested classes if needed
        var containingTypesOpen = string.Empty;
        var containingTypesClose = string.Empty;
        var indent = "    ";

        foreach (var containingType in containingTypes)
        {
            containingTypesOpen += $"{indent}partial class {containingType}\n{indent}{{\n";
            indent += "    ";
        }

        // Build closing braces for nested classes
        var closeIndent = "    ";
        for (int i = 0; i < containingTypes.Count; i++)
        {
            closeIndent = new string(' ', 4 * (containingTypes.Count - i));
            containingTypesClose += $"{closeIndent}}}\n";
        }

        // Generate complete partial class
        var source = $$"""
            // <auto-generated/>
            // Generated by Guacamole Source Generator
            // This file contains push-based reactive source properties
            #nullable enable

            namespace {{namespaceName}}
            {
            {{containingTypesOpen}}{{indent}}partial class {{className}}
            {{indent}}{
                    // ========================================
                    // Source Fields (push-based reactive sources)
                    // ========================================
                    {{sourceFields}}

                    // ========================================
                    // Batching Infrastructure (for SaveChanges compatibility)
                    // ========================================
            {{batchingInfrastructure}}

            {{constructor}}

                    // ========================================
                    // Property Implementations (backed by sources)
                    // ========================================
            {{propertyImplementations}}

                    // ========================================
                    // Source Accessors (for pushing data)
                    // ========================================
            {{sourceAccessors}}

                    // ========================================
                    // Backward Compatibility Methods (mutation-based API)
                    // ========================================
            {{mutationMethods}}
            {{saveChangesMethod}}
            {{indent}}}
            {{containingTypesClose}}}
            """;

        context.AddSource($"{className}.g.cs", source);
    }
}
