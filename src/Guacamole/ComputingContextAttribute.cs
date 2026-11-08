using System;

namespace Guacamole;

/// <summary>
/// Marks a partial class deriving from <see cref="ComputingContextBase"/> as a computing context for
/// the Guacamole source generator, which implements its partial <see cref="IncrementalValueProvider{T}"/>
/// and <see cref="IncrementalValuesProvider{T}"/> properties as source-backed inputs (see
/// <c>docs/generated-code-reference.md</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComputingContextAttribute : Attribute
{
}
