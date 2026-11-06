using Guacamole.SourceGenerators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Incremental source generator for classes marked with [ComputingContext].
/// Generates source-backed properties with push-based reactive updates.
/// </summary>
/// <remarks>
/// Pipeline: ForAttributeWithMetadataName performs all semantic analysis and produces an equatable
/// <see cref="ContextAnalysisResult"/>. Source generation and diagnostic reporting are split into two
/// branches so that, for example, a diagnostic location shift does not force re-emitting source.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ComputingContextGenerator : IIncrementalGenerator
{
    /// <summary>Tracking name of the step that analyses [ComputingContext] classes.</summary>
    public const string AnalysisStepName = "ComputingContextAnalysis";

    /// <summary>Tracking name of the step that selects code generation models.</summary>
    public const string ModelStepName = "ComputingContextModels";

    /// <summary>Tracking name of the step that selects diagnostics.</summary>
    public const string DiagnosticsStepName = "ComputingContextDiagnostics";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var analysisResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ContextAnalyzer.ComputingContextAttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ContextAnalyzer.Analyze(ctx, ct))
            .WithTrackingName(AnalysisStepName);

        var models = analysisResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(ModelStepName);

        var diagnostics = analysisResults
            .Where(static result => !result.Diagnostics.IsEmpty)
            .Select(static (result, _) => result.Diagnostics)
            .WithTrackingName(DiagnosticsStepName);

        context.RegisterSourceOutput(models, static (spc, model) =>
            spc.AddSource(model.HintName, SourceText.From(ContextEmitter.Emit(model), Encoding.UTF8)));

        context.RegisterSourceOutput(diagnostics, static (spc, items) =>
        {
            foreach (var diagnostic in items)
            {
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        });
    }
}
