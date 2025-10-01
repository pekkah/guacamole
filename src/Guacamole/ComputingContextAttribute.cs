using System;

namespace Guacamole;

/// <summary>
/// Marks a class as a computing context for source generator processing.
/// Classes with this attribute will have mutation methods generated automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComputingContextAttribute : Attribute
{
    /// <summary>
    /// Optional name for the generated context.
    /// If not specified, uses the class name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to generate async versions of mutation methods.
    /// Default is true for compatibility with async workflows.
    /// </summary>
    public bool GenerateAsyncMethods { get; set; } = true;

    /// <summary>
    /// Whether to generate batch operation methods (AddRange, RemoveRange).
    /// Default is true for efficient bulk operations.
    /// </summary>
    public bool GenerateBatchMethods { get; set; } = true;
}
