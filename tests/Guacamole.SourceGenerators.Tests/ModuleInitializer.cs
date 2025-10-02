using System.IO;
using System.Runtime.CompilerServices;
using VerifyTests;
using VerifyXunit;

namespace Guacamole.SourceGenerators.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifySourceGenerators.Initialize();

        // Configure Verify to use Snapshots directory with subdirectories per test class
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
        {
            var className = type.Name;
            var directory = Path.Combine(projectDirectory, "Snapshots", className);
            // Use minimal typeName "_" to get clean "_.MethodName.verified.txt" filenames
            return new PathInfo(directory, "_", method.Name);
        });
    }
}
