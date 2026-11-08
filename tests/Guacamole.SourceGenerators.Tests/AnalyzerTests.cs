using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Guacamole.SourceGenerators.Tests;

/// <summary>
/// Tests for <see cref="ComputingContextAnalyzer"/>: its diagnostics can be suppressed like any
/// analyzer warning (#107).
/// </summary>
public class AnalyzerTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics.CodeAnalysis;
        using Guacamole;

        namespace Test;

        public class Plain
        {
            public string Name { get; set; } = "";
        }

        """;

    [Fact]
    public void PragmaAroundProperty_SuppressesGUAC0004()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0004 // compared by identity on purpose
                public IncrementalValueProvider<Plain> Suppressed { get; set; }
            #pragma warning restore GUAC0004
                public IncrementalValueProvider<Plain> Reported { get; set; }
            }
            """;

        var result = TestHelper.Run(source);
        var diagnostics = TestHelper.RunAnalyzer(source);

        Assert.Empty(result.GeneratorDiagnostics);
        var diagnostic = Assert.Single(diagnostics);
        AssertAt(diagnostic, "GUAC0004", source, "Reported");
    }

    [Fact]
    public void PragmaAroundInputProperty_SuppressesGUAC0004()
    {
        // The issue's example: a provider property in a [ComputingContext] class.
        var source = Header + """
            public sealed class GlobalImplContext;

            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0004
                public partial IncrementalValueProvider<GlobalImplContext> GlobalImplContextInput { get; }

                public IncrementalValueProvider<GlobalImplContext> GlobalImplContextProvider { get; set; }
            #pragma warning restore GUAC0004
            }
            """;

        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Fact]
    public void SuppressMessageOnProperty_SuppressesGUAC0004()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                [SuppressMessage("Guacamole", "GUAC0004", Justification = "Compared by identity on purpose")]
                public IncrementalValueProvider<Plain> Suppressed { get; set; }

                public IncrementalValueProvider<Plain> Reported { get; set; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        AssertAt(diagnostic, "GUAC0004", source, "Reported");
    }

    [Fact]
    public void SuppressMessageOnClass_SuppressesGUAC0004ForAllProperties()
    {
        var source = Header + """
            [ComputingContext]
            [SuppressMessage("Guacamole.Performance", "GUAC0004:Type does not implement proper equality for incremental computation")]
            public partial class TestContext : ComputingContextBase
            {
                public partial IncrementalValueProvider<Plain> Input { get; }

                public IncrementalValueProvider<Plain> Derived { get; set; }
            }
            """;

        Assert.Empty(TestHelper.RunAnalyzer(source));
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("silent", null)]
    [InlineData("suggestion", DiagnosticSeverity.Info)]
    [InlineData("error", DiagnosticSeverity.Error)]
    public void EditorConfigSeverity_AppliesToGUAC0004(string configured, DiagnosticSeverity? expected)
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
                public IncrementalValueProvider<Plain> Derived { get; set; }
            }
            """;

        var diagnostics = TestHelper.RunAnalyzer(source, $"""
            root = true

            [*.cs]
            dotnet_diagnostic.GUAC0004.severity = {configured}
            """);

        if (expected is null)
        {
            Assert.DoesNotContain(diagnostics, d => d.Severity != DiagnosticSeverity.Hidden);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("GUAC0004", diagnostic.Id);
            Assert.Equal(expected, diagnostic.Severity);
        }
    }

    [Fact]
    public void PragmaAroundConstructor_SuppressesGUAC0009()
    {
        var source = Header + """
            [ComputingContext]
            public partial class TestContext : ComputingContextBase
            {
            #pragma warning disable GUAC0009
                public TestContext(int value) : base()
                {
                }
            #pragma warning restore GUAC0009

                public TestContext(string value) : base()
                {
                }

                public partial IncrementalValuesProvider<string> Items { get; }
            }
            """;

        var diagnostic = Assert.Single(TestHelper.RunAnalyzer(source));
        Assert.Equal("GUAC0009", diagnostic.Id);
        Assert.Contains("TestContext(string value)", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line].ToString());
    }

    private static void AssertAt(Diagnostic diagnostic, string id, string source, string propertyName)
    {
        Assert.Equal(id, diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(propertyName, source.Substring(span.Start, span.Length));
    }
}
