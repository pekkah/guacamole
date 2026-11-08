using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Xunit;

namespace Guacamole.Benchmarks.Tests;

public class BenchmarkValidationTests
{
    /// <summary>
    /// Smoke test (spec 009 T009): runs every benchmark case once through <see cref="Program.Run"/>
    /// with BenchmarkDotNet's dry job. The in-process toolchain runs the cases in the test process
    /// (spawning a separate benchmark process is slow and depends on the SDK setup), so this
    /// catches benchmarks that throw, fail validation or are no longer discovered, in seconds.
    /// </summary>
    [Fact]
    public void AllBenchmarks_ExecuteInDryRun()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), $"guacamole-benchmarks-{Guid.NewGuid():N}");
        var config = ManualConfig.CreateEmpty()
            .AddJob(Job.Dry.WithToolchain(InProcessNoEmitToolchain.Instance))
            .AddLogger(NullLogger.Instance)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            // `dotnet test` builds Debug; the dry run checks that benchmarks execute, not their timings
            .WithOptions(ConfigOptions.DisableOptimizationsValidator | ConfigOptions.DisableLogFile)
            .WithArtifactsPath(artifacts);

        try
        {
            var summaries = Program.Run(["--filter", "*"], config);

            var expectedTypes = typeof(Program).Assembly.GetTypes()
                .Where(static t => t.GetMethods().Any(static m => m.IsDefined(typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute), false)))
                .Select(static t => t.Name)
                .Order(StringComparer.Ordinal);
            var executedTypes = summaries
                .SelectMany(static s => s.BenchmarksCases)
                .Select(static c => c.Descriptor.Type.Name)
                .Distinct()
                .Order(StringComparer.Ordinal);
            Assert.Equal(expectedTypes, executedTypes);

            Assert.All(summaries, summary =>
            {
                Assert.False(summary.HasCriticalValidationErrors, string.Join("; ", summary.ValidationErrors.Select(static e => e.Message)));
                Assert.Equal(summary.BenchmarksCases.Length, summary.Reports.Length);
                Assert.All(summary.Reports, report => Assert.True(report.Success, $"{report.BenchmarkCase.DisplayInfo} failed"));
                Assert.True(Program.Succeeded(summary));
            });
        }
        finally
        {
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
        }
    }
}
