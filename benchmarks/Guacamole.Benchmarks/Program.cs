using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Guacamole.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithArtifactsPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "reports"));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
