using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using VerifyTests;

namespace Guacamole.SourceGenerators.Tests;

public static class TestHelper
{
    public static Task Verify<TGenerator>(string source, [CallerFilePath] string sourceFile = "")
        where TGenerator : IIncrementalGenerator, new()
    {
        var driver = BuildDriver<TGenerator>(source);
        var runResult = driver.GetRunResult();

        // Use Verify.SourceGenerators to verify the run result
        // This will snapshot both generated sources and diagnostics
        return Verifier.Verify(runResult, sourceFile: sourceFile);
    }

    private static GeneratorDriver BuildDriver<TGenerator>(string source)
        where TGenerator : IIncrementalGenerator, new()
    {
        // Create syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get required assembly locations
        var systemRuntime = typeof(object).Assembly.Location;
        var systemRuntimePath = System.IO.Path.GetDirectoryName(systemRuntime)!;

        // Create references including Guacamole assemblies
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.IsExternalInit).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Collections.Immutable.dll")),
            MetadataReference.CreateFromFile(typeof(ComputingContextAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ComputingContextBase).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Guacamole.Core.Providers.IncrementalValueProvider<>).Assembly.Location),
        };

        // Create compilation
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Create and run generator
        var generator = new TGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation);
    }

    public static GeneratorRunResult RunGenerator<TGenerator>(string source) where TGenerator : IIncrementalGenerator, new()
    {
        // Create syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get required assembly locations
        var systemRuntime = typeof(object).Assembly.Location;
        var systemRuntimePath = System.IO.Path.GetDirectoryName(systemRuntime)!;

        // Create references including Guacamole assemblies
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.IsExternalInit).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntimePath, "System.Collections.Immutable.dll")),
            MetadataReference.CreateFromFile(typeof(ComputingContextAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ComputingContextBase).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Guacamole.Core.Providers.IncrementalValueProvider<>).Assembly.Location),
        };

        // Create compilation
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Create and run generator
        var generator = new TGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return new GeneratorRunResult(runResult, outputCompilation, diagnostics);
    }
}

public class GeneratorRunResult
{
    public GeneratorDriverRunResult RunResult { get; }
    public Compilation OutputCompilation { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public GeneratorRunResult(GeneratorDriverRunResult runResult, Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics)
    {
        RunResult = runResult;
        OutputCompilation = outputCompilation;
        Diagnostics = diagnostics;
    }

    public string GetGeneratedSource()
    {
        if (RunResult.Results.Length == 0)
            return string.Empty;

        var result = RunResult.Results[0];
        return result.GeneratedSources.Length > 0 ? result.GeneratedSources[0].SourceText.ToString() : string.Empty;
    }

    public bool HasDiagnosticErrors()
    {
        return Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    public IEnumerable<string> GetErrors()
    {
        return Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());
    }

    public IEnumerable<DiagnosticInfo> GetDiagnostics()
    {
        return Diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning) // Only include warnings and errors
            .Select(d => new DiagnosticInfo(
                d.Id,
                d.Severity.ToString(),
                d.GetMessage(),
                d.Location.GetLineSpan().StartLinePosition.Line,
                d.Location.GetLineSpan().StartLinePosition.Character))
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.Id)
            .ToList();
    }
}

public record DiagnosticInfo(
    string Id,
    string Severity,
    string Message,
    int Line,
    int Column);
