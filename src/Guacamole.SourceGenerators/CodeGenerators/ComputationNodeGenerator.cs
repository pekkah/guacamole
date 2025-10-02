namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates property-specific computation nodes that integrate with the computation graph.
/// Uses C# 11 raw string literals for code generation.
/// </summary>
internal static class ComputationNodeGenerator
{
    /// <summary>
    /// Generates a computation node class for a property.
    /// </summary>
    public static string Generate(PropertyInfo property, string contextClassName)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionNode(property, contextClassName);
        }
        else
        {
            return GenerateSingleValueNode(property, contextClassName);
        }
    }

    private static string GenerateCollectionNode(PropertyInfo property, string contextClassName)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var nodeName = $"{propertyName}Node";

        return $$"""

                /// <summary>
                /// Computation node for {{propertyName}} collection property.
                /// Integrates with computation graph for dependency tracking.
                /// </summary>
                private sealed class {{nodeName}} : Guacamole.Core.Nodes.CollectionComputationNodeBase<{{itemType}}>
                {
                    private readonly {{contextClassName}} _context;

                    public {{nodeName}}({{contextClassName}} context)
                        : base($"{{contextClassName}}.{{propertyName}}")
                    {
                        _context = context;
                    }

                    public override Guacamole.Core.Common.Result<System.Collections.Generic.IEnumerable<{{itemType}}>, Guacamole.Core.Pipeline.ComputationError> Compute(
                        Guacamole.Core.Pipeline.ComputationContext context)
                    {
                        // Check cache first (incremental computation optimization)
                        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is System.Collections.Generic.IEnumerable<{{itemType}}> cached)
                        {
                            return Guacamole.Core.Common.Result<System.Collections.Generic.IEnumerable<{{itemType}}>, Guacamole.Core.Pipeline.ComputationError>
                                .Success(cached);
                        }

                        try
                        {
                            // Return current state from context
                            return Guacamole.Core.Common.Result<System.Collections.Generic.IEnumerable<{{itemType}}>, Guacamole.Core.Pipeline.ComputationError>
                                .Success(_context._current{{propertyName}});
                        }
                        catch (System.Exception ex)
                        {
                            return Guacamole.Core.Common.Result<System.Collections.Generic.IEnumerable<{{itemType}}>, Guacamole.Core.Pipeline.ComputationError>
                                .Error(Guacamole.Core.Pipeline.ComputationError.FromException(ex));
                        }
                    }
                }

            """;
    }

    private static string GenerateSingleValueNode(PropertyInfo property, string contextClassName)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var nodeName = $"{propertyName}Node";

        return $$"""

                /// <summary>
                /// Computation node for {{propertyName}} single-value property.
                /// Integrates with computation graph for dependency tracking.
                /// </summary>
                private sealed class {{nodeName}} : Guacamole.Core.Nodes.ComputationNodeBase<{{itemType}}>
                {
                    private readonly {{contextClassName}} _context;

                    public {{nodeName}}({{contextClassName}} context)
                        : base($"{{contextClassName}}.{{propertyName}}")
                    {
                        _context = context;
                    }

                    public override Guacamole.Core.Common.Result<{{itemType}}, Guacamole.Core.Pipeline.ComputationError> Compute(
                        Guacamole.Core.Pipeline.ComputationContext context)
                    {
                        // Check cache first (incremental computation optimization)
                        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is {{itemType}} cached)
                        {
                            return Guacamole.Core.Common.Result<{{itemType}}, Guacamole.Core.Pipeline.ComputationError>
                                .Success(cached);
                        }

                        try
                        {
                            if (_context._current{{propertyName}} == null)
                            {
                                return Guacamole.Core.Common.Result<{{itemType}}, Guacamole.Core.Pipeline.ComputationError>
                                    .Error(new Guacamole.Core.Pipeline.ComputationError("Property {{propertyName}} has not been set"));
                            }

                            return Guacamole.Core.Common.Result<{{itemType}}, Guacamole.Core.Pipeline.ComputationError>
                                .Success(_context._current{{propertyName}});
                        }
                        catch (System.Exception ex)
                        {
                            return Guacamole.Core.Common.Result<{{itemType}}, Guacamole.Core.Pipeline.ComputationError>
                                .Error(Guacamole.Core.Pipeline.ComputationError.FromException(ex));
                        }
                    }
                }

            """;
    }

    /// <summary>
    /// Generates node field and initialization code for a property.
    /// </summary>
    public static string GenerateNodeField(PropertyInfo property, string contextClassName)
    {
        var propertyName = property.Name;
        var nodeName = $"{propertyName}Node";

        return $$"""
                private {{nodeName}} _{{propertyName}}Node = null!;
            """;
    }

    /// <summary>
    /// Generates node initialization in constructor.
    /// </summary>
    public static string GenerateNodeInitialization(PropertyInfo property)
    {
        var propertyName = property.Name;
        var nodeName = $"{propertyName}Node";

        return $$"""
                    _{{propertyName}}Node = new {{nodeName}}(this);
                    Graph = Graph.AddNode(_{{propertyName}}Node);
            """;
    }
}
