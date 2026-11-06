using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Guacamole.Tests.Regressions;

/// <summary>
/// Regression tests for issue #23: "Duplicate diagnostics when using Merge() on SelectMany() providers".
/// </summary>
/// <remarks>
/// The reported symptom was <c>[MATCH001, MATCH001]</c> even after each stage stopped accumulating
/// earlier diagnostics. Root cause: node IDs used to be derived from the operation name and the
/// (sorted) parent IDs only, so two different operator calls on the same parent (for example two
/// <c>Select</c>s with different lambdas) shared one cache slot and returned each other's values.
/// Every operator call now gets its own node (see NodeIdentityTests), so each stage's diagnostics
/// stay separate.
/// </remarks>
public partial class Issue23MergeDuplicateDiagnosticsTests
{
    public sealed record Diagnostic(string Code, string FilePath);

    public sealed record ParsedUnit(string FilePath);

    public sealed record TypeCheckedModule(string FilePath, ImmutableArray<Diagnostic> Diagnostics);

    public sealed record ExhaustivenessCheckedModule(string FilePath, ImmutableArray<Diagnostic> Diagnostics);

    public sealed record SymbolTable(string FilePath, ImmutableArray<Diagnostic> Diagnostics);

    [ComputingContext]
    private partial class CompilerContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> SourceFilePaths { get; }
    }

    [Fact]
    public void Merge_OfPerStageDiagnostics_ContainsEachDiagnosticOnce()
    {
        using var context = new CompilerContext();

        var parsedUnits = context.SourceFilePaths.Select(path => new ParsedUnit(path));

        // Two different Select stages off the same parent: these used to share a cache slot.
        var symbols = parsedUnits.Select(unit => new SymbolTable(unit.FilePath, ImmutableArray<Diagnostic>.Empty));
        var typeChecked = parsedUnits.Select(unit => new TypeCheckedModule(unit.FilePath, ImmutableArray<Diagnostic>.Empty));

        // Each stage reports only its own (new) diagnostics.
        var exhaustivenessChecked = typeChecked.Select(module =>
            new ExhaustivenessCheckedModule(module.FilePath, ImmutableArray.Create(new Diagnostic("MATCH001", module.FilePath))));

        var symbolDiagnostics = symbols.SelectMany(m => m.Diagnostics);
        var typeCheckDiagnostics = typeChecked.SelectMany(m => m.Diagnostics);
        var exhaustivenessDiagnostics = exhaustivenessChecked.SelectMany(m => m.Diagnostics);

        var allDiagnostics = symbolDiagnostics
            .Merge(typeCheckDiagnostics)
            .Merge(exhaustivenessDiagnostics);

        context.SourceFilePathsSource.Push("/test/main.fugu");

        Assert.Empty(Values(symbolDiagnostics));
        Assert.Empty(Values(typeCheckDiagnostics));
        Assert.Equal([new Diagnostic("MATCH001", "/test/main.fugu")], Values(exhaustivenessDiagnostics));
        Assert.Equal([new Diagnostic("MATCH001", "/test/main.fugu")], Values(allDiagnostics));
    }

    [Fact]
    public void Merge_OfSelectManyStreamsFromSameParent_KeepsBothStreamsDistinct()
    {
        using var context = new CompilerContext();

        var modules = context.SourceFilePaths.Select(path => new TypeCheckedModule(
            path,
            ImmutableArray.Create(new Diagnostic("TYPE001", path), new Diagnostic("MATCH001", path))));

        // Two different SelectMany calls on the same parent.
        var typeErrors = modules.SelectMany(m => m.Diagnostics.Where(d => d.Code.StartsWith("TYPE")));
        var matchErrors = modules.SelectMany(m => m.Diagnostics.Where(d => d.Code.StartsWith("MATCH")));

        var all = typeErrors.Merge(matchErrors);

        context.SourceFilePathsSource.Push("/test/main.fugu");

        Assert.Equal(["TYPE001", "MATCH001"], Values(all).Select(d => d.Code));
    }

    private static Diagnostic[] Values(IncrementalValuesProvider<Diagnostic> provider)
    {
        var result = provider.GetValues();
        Assert.True(result.IsSuccess, result.ToString());
        return result.Value!.ToArray();
    }
}
