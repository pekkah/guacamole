using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Guacamole.Benchmarks;

/// <summary>
/// BenchmarkDotNet entry point. Accepts the usual BenchmarkDotNet command line
/// (<c>--filter</c>, <c>--job</c>, <c>--list flat</c>, ...); reports go to <c>../reports</c>
/// relative to the current directory (<c>benchmarks/reports</c> when run from the project directory).
/// </summary>
public class Program
{
    /// <summary>Runs the selected benchmarks; returns 1 if any of them failed, 0 otherwise.</summary>
    public static int Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithArtifactsPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "reports"));

        return Run(args, config).All(Succeeded) ? 0 : 1;
    }

    /// <summary>Runs the benchmarks selected by <paramref name="args"/> with <paramref name="config"/>.</summary>
    public static Summary[] Run(string[] args, IConfig config) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToArray();

    /// <summary>True if the summary has no validation errors and every benchmark case ran successfully.</summary>
    public static bool Succeeded(Summary summary) =>
        !summary.HasCriticalValidationErrors && summary.Reports.All(static report => report.Success);
}
