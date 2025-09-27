using Microsoft.CodeAnalysis;

namespace Guacamole.SourceGenerators.Generators;

/// <summary>
/// Incremental source generator for automatic query registration and boilerplate reduction.
/// Placeholder for future development.
/// </summary>
[Generator]
public sealed class QueryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // TODO: Initialize incremental query generator
        // Example incremental pipeline:
        // var provider = context.SyntaxProvider
        //     .CreateSyntaxProvider(
        //         predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
        //         transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
        //     .Where(static m => m is not null);
        //
        // context.RegisterSourceOutput(provider, static (spc, source) => Execute(spc, source));
    }
}
