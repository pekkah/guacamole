// Contract: PropertyAnalyzer Classification Behavior
// This file documents the expected behavior contract for PropertyAnalyzer.Analyze()

namespace Guacamole.SourceGenerators.Contracts;

/// <summary>
/// Contract for property classification after the logic reversal.
/// </summary>
public class PropertyAnalyzerContract
{
    /// <summary>
    /// INPUT: Property declaration with NO setter (get-only)
    /// EXPECTED OUTPUT: Property classified as "InputProperty" (auto-managed)
    /// GENERATED CODE: Mutation methods, change tracking, computation node, SaveChanges logic
    /// </summary>
    [ComputingContext]
    public partial class AutoManagedExample : ComputingContextBase
    {
        // Get-only property → Framework generates ALL infrastructure
        public partial IncrementalValueProvider<string> Name { get; }

        // Expected generated methods:
        // - public void SetName(string value)
        // - public abstract record NameChange(...)
        // - private sealed class NameNode : ComputationNodeBase<string>
        // - SaveChanges() includes Name change application
    }

    /// <summary>
    /// INPUT: Property declaration WITH setter
    /// EXPECTED OUTPUT: Property classified as "DerivedProperty" (user-managed)
    /// GENERATED CODE: Property implementation only (no mutations)
    /// </summary>
    [ComputingContext]
    public partial class UserManagedExample : ComputingContextBase
    {
        // Property with setter → User manages initialization and changes
        public partial IncrementalValueProvider<int> Count { get; set; }

        // Expected generated code:
        // - Property getter/setter implementation
        // NOT generated:
        // - No SetCount() method
        // - No CountChange records
        // - No CountNode (user creates nodes manually if needed)
        // - SaveChanges() does NOT process Count changes

        public UserManagedExample(IServiceProvider services) : base()
        {
            // User must initialize Count here
            Count = CreateInput<int>();
        }
    }

    /// <summary>
    /// INPUT: Collection property with NO setter (get-only)
    /// EXPECTED OUTPUT: Property classified as "InputProperty" (auto-managed)
    /// GENERATED CODE: Collection mutation methods (Add, Remove, Replace, batch variants)
    /// </summary>
    [ComputingContext]
    public partial class AutoManagedCollectionExample : ComputingContextBase
    {
        // Get-only collection → Framework generates collection mutations
        public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

        // Expected generated methods:
        // - public void AddSourceFile(SourceFile item)
        // - public void AddSourceFiles(IEnumerable<SourceFile> items)
        // - public bool RemoveSourceFile(SourceFile item)
        // - public void RemoveSourceFiles(IEnumerable<SourceFile> items)
        // - public void ReplaceSourceFile(SourceFile oldItem, SourceFile newItem)
        // - public abstract record SourceFileChange.Add/Remove/Replace(...)
        // - private sealed class SourceFilesNode : CollectionComputationNodeBase<SourceFile>
    }

    /// <summary>
    /// INPUT: Mixed property types in same context
    /// EXPECTED OUTPUT: Each property classified independently
    /// </summary>
    [ComputingContext]
    public partial class MixedExample : ComputingContextBase
    {
        // Auto-managed (get-only) → Gets mutations
        public partial IncrementalValueProvider<string> AutoName { get; }

        // User-managed (has setter) → No mutations
        public partial IncrementalValueProvider<int> ManualCount { get; set; }

        public MixedExample(IServiceProvider services) : base()
        {
            // User initializes manual property
            ManualCount = CreateInput<int>();

            // AutoName will be initialized by framework when SetAutoName() is called
        }
    }

    /// <summary>
    /// EDGE CASE: Property with both getter AND setter
    /// EXPECTED OUTPUT: Classified as "DerivedProperty" (user-managed)
    /// RATIONALE: Setter presence takes precedence, signals user intent to manage
    /// </summary>
    [ComputingContext]
    public partial class BothAccessorsExample : ComputingContextBase
    {
        // Has both get and set → Treated as user-managed (setter wins)
        public partial IncrementalValueProvider<string> ManualProperty { get; set; }

        // NOT generated: SetManualProperty() mutation method
        // User must initialize and manage
    }
}

/// <summary>
/// Test validation contract: These behaviors must be verified by tests
/// </summary>
public class TestContract
{
    /// <summary>
    /// Test: Get-only property generates mutation method
    /// </summary>
    public void GetOnlyProperty_GeneratesMutationMethod()
    {
        // Given: Context with get-only property
        var source = @"
            [ComputingContext]
            public partial class TestContext : ComputingContextBase {
                public partial IncrementalValueProvider<string> Name { get; }
            }
        ";

        // When: Source generator runs
        var result = RunGenerator(source);

        // Then: SetName() method exists in generated code
        Assert.Contains("public void SetName(string value)", result.GeneratedCode);
        Assert.Contains("NameChange", result.GeneratedCode);
    }

    /// <summary>
    /// Test: Property with setter does NOT generate mutation method
    /// </summary>
    public void PropertyWithSetter_DoesNotGenerateMutationMethod()
    {
        // Given: Context with property that has setter
        var source = @"
            [ComputingContext]
            public partial class TestContext : ComputingContextBase {
                public partial IncrementalValueProvider<int> Count { get; set; }
            }
        ";

        // When: Source generator runs
        var result = RunGenerator(source);

        // Then: SetCount() method does NOT exist
        Assert.DoesNotContain("public void SetCount(", result.GeneratedCode);
        Assert.DoesNotContain("CountChange", result.GeneratedCode);
    }

    /// <summary>
    /// Test: Existing tests with old behavior fail (proving behavior changed)
    /// </summary>
    public void OldBehavior_TestsFail()
    {
        // This test should fail initially, proving the behavior changed
        // After updating snapshots, it will pass with new behavior
    }
}
