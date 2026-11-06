using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

public static class TestHelper
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    /// <summary>
    /// Runs the generator, asserts that the generated code compiles without errors or warnings,
    /// and snapshots generated sources and generator diagnostics.
    /// </summary>
    public static Task Verify(string[] sources, [CallerFilePath] string sourceFile = "")
    {
        var result = Run(sources);
        AssertCompilesCleanly(result);
        return Verifier.Verify(result.Driver, sourceFile: sourceFile);
    }

    public static Task Verify(string source, [CallerFilePath] string sourceFile = "")
        => Verify([source], sourceFile);

    public static GeneratorTestResult Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        return new GeneratorTestResult(driver, outputCompilation, generatorDiagnostics);
    }

    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"Test{i}.cs"));
        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static GeneratorDriver CreateDriver(bool trackSteps = false)
        => CSharpGeneratorDriver.Create(
            [new ComputingContextGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: trackSteps));

    public static CSharpParseOptions DefaultParseOptions => ParseOptions;

    /// <summary>
    /// Asserts that the compilation including generated sources has no errors or warnings.
    /// </summary>
    public static void AssertCompilesCleanly(GeneratorTestResult result)
    {
        var problems = result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();

        Assert.True(problems.Count == 0, "Generated code does not compile cleanly:\n" + string.Join("\n", problems));
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        // All framework assemblies of the running runtime plus the Guacamole runtime assembly.
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), frameworkDirectory, StringComparison.Ordinal));

        return trustedAssemblies
            .Append(typeof(ComputingContextBase).Assembly.Location)
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}

public sealed class GeneratorTestResult(GeneratorDriver driver, Compilation outputCompilation, ImmutableArray<Diagnostic> generatorDiagnostics)
{
    public GeneratorDriver Driver { get; } = driver;

    public Compilation OutputCompilation { get; } = outputCompilation;

    /// <summary>Diagnostics reported by the generator.</summary>
    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; } = generatorDiagnostics;

    public ImmutableArray<GeneratedSourceResult> GeneratedSources
        => Driver.GetRunResult().Results.Single().GeneratedSources;

    public IReadOnlyList<Diagnostic> CompilationErrors
        => OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
}
