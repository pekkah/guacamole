using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Incremental source generator for ComputingContext marked classes.
/// Generates mutation methods, change tracking, computation nodes, and SaveChanges logic.
/// </summary>
[Generator]
public class ComputingContextGenerator : IIncrementalGenerator
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

        // Generate code using all generators
        var changeTypes = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.ChangeTypeGenerator.Generate));
        var changeTrackingFields = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.ChangeTypeGenerator.GenerateChangeTrackingFields));

        var nodeClasses = string.Join("\n", analysisResult.InputProperties.Select(p => CodeGenerators.ComputationNodeGenerator.Generate(p, className)));
        var nodeFields = string.Join("\n", analysisResult.InputProperties.Select(p => CodeGenerators.ComputationNodeGenerator.GenerateNodeField(p, className)));
        var nodeInitializations = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.ComputationNodeGenerator.GenerateNodeInitialization));

        // Generate partial method for node initialization
        var initializeNodesMethod = analysisResult.InputProperties.Count > 0
            ? $$"""
                    // ========================================
                    // Node Initialization (called from user's constructor)
                    // ========================================
                    partial void InitializeComputationNodes()
                    {
            {{nodeInitializations}}
                    }
            """
            : "";

        var mutationMethods = string.Join("\n", analysisResult.InputProperties.Select(p => CodeGenerators.MutationMethodGenerator.Generate(p, className)));
        var saveChangesMethods = CodeGenerators.SaveChangesGenerator.Generate(analysisResult.InputProperties);

        var propertyImplementations = string.Join("\n", analysisResult.InputProperties.Select(CodeGenerators.PropertyGenerator.Generate));

        // Generate cache invalidation helper method
        var cacheInvalidationHelper = """

                    // ========================================
                    // Cache Invalidation Helper
                    // ========================================

                    /// <summary>
                    /// Invalidates cache for the specified node and all transitive dependents.
                    /// Called automatically by mutation methods to maintain cache consistency.
                    /// </summary>
                    private void InvalidateNodeCache(string nodeId)
                    {
                        // Remove the node itself from cache
                        Graph.ValueCache.Remove(nodeId);

                        // Find all nodes that depend on this node (transitively)
                        var dependentsToInvalidate = FindAllDependents(nodeId);
                        foreach (var dependent in dependentsToInvalidate)
                        {
                            Graph.ValueCache.Remove(dependent);
                        }
                    }

                    /// <summary>
                    /// Finds all nodes that transitively depend on the given node.
                    /// </summary>
                    private System.Collections.Generic.HashSet<string> FindAllDependents(string nodeId)
                    {
                        var dependents = new System.Collections.Generic.HashSet<string>();
                        var toCheck = new System.Collections.Generic.Queue<string>();
                        toCheck.Enqueue(nodeId);

                        while (toCheck.Count > 0)
                        {
                            var current = toCheck.Dequeue();

                            // Find all nodes that directly depend on current
                            foreach (var kvp in Graph.Dependencies)
                            {
                                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                                {
                                    dependents.Add(kvp.Key);
                                    toCheck.Enqueue(kvp.Key);
                                }
                            }
                        }

                        return dependents;
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

        // Adjust indentation for generated code based on nesting level
        var memberIndent = new string(' ', 4 * (containingTypes.Count + 2));

        // Generate complete partial class
        var source = $$"""
            // <auto-generated/>
            #nullable enable

            namespace {{namespaceName}}
            {
            {{containingTypesOpen}}{{indent}}partial class {{className}}
            {{indent}}{
                    // ========================================
                    // Change Record Types (strongly-typed, zero boxing)
                    // ========================================
            {{changeTypes}}

                    // ========================================
                    // Change Tracking Fields
                    // ========================================
            {{changeTrackingFields}}

                    // ========================================
                    // Computation Node Classes
                    // ========================================
            {{nodeClasses}}

                    // ========================================
                    // Computation Node Fields
                    // ========================================
            {{nodeFields}}

            {{initializeNodesMethod}}

                    // ========================================
                    // Property Implementations
                    // ========================================
            {{propertyImplementations}}

                    // ========================================
                    // Mutation Methods (Add/Remove/Replace/Set)
                    // ========================================
            {{mutationMethods}}

                    // ========================================
                    // SaveChanges Methods
                    // ========================================
            {{saveChangesMethods}}

            {{cacheInvalidationHelper}}
            {{indent}}}
            {{containingTypesClose}}}
            """;

        context.AddSource($"{className}.g.cs", source);
    }
}
