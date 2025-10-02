namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates provider property implementations that connect to computation nodes.
/// Uses C# 11 raw string literals for code generation.
/// </summary>
internal static class PropertyGenerator
{
    /// <summary>
    /// Generates property implementation that connects to its computation node.
    /// </summary>
    public static string Generate(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionProperty(property);
        }
        else
        {
            return GenerateSingleValueProperty(property);
        }
    }

    private static string GenerateCollectionProperty(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        // For input properties with setters, generate implementing declaration for partial property
        if (property.HasSetter)
        {
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial property {{propertyName}}.
                    /// Provides backing storage and connects to computation node.
                    /// </summary>
                    private Guacamole.Core.Providers.IncrementalValuesProvider<{{itemType}}> _{{propertyName}}Provider;

                    public partial Guacamole.Core.Providers.IncrementalValuesProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Provider.Node == null)
                            {
                                _{{propertyName}}Provider = new Guacamole.Core.Providers.IncrementalValuesProvider<{{itemType}}>(
                                    _{{propertyName}}Node,
                                    Graph,
                                    new Guacamole.Core.Diagnostics.ProviderMetadata(TrackingName: "{{propertyName}}"));
                            }
                            return _{{propertyName}}Provider;
                        }
                        set
                        {
                            // Setter required for partial property signature match
                            // Users interact through Add/Remove/Replace methods
                            _{{propertyName}}Provider = value;
                        }
                    }

                """;
        }
        else
        {
            // Derived properties (read-only) - implementing declaration
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial derived property {{propertyName}}.
                    /// Must be initialized by user code (e.g., in ConfigurePipeline).
                    /// </summary>
                    private Guacamole.Core.Providers.IncrementalValuesProvider<{{itemType}}> _{{propertyName}}Field;

                    public partial Guacamole.Core.Providers.IncrementalValuesProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Field.Node == null)
                            {
                                throw new System.InvalidOperationException(
                                    $"Derived property {{propertyName}} has not been initialized. " +
                                    "Initialize it in the constructor or ConfigurePipeline method.");
                            }
                            return _{{propertyName}}Field;
                        }
                    }

                """;
        }
    }

    private static string GenerateSingleValueProperty(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        if (property.HasSetter)
        {
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial property {{propertyName}}.
                    /// Provides backing storage and connects to computation node.
                    /// </summary>
                    private Guacamole.Core.Providers.IncrementalValueProvider<{{itemType}}> _{{propertyName}}Provider;

                    public partial Guacamole.Core.Providers.IncrementalValueProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Provider.Node == null)
                            {
                                _{{propertyName}}Provider = new Guacamole.Core.Providers.IncrementalValueProvider<{{itemType}}>(
                                    _{{propertyName}}Node,
                                    Graph,
                                    new Guacamole.Core.Diagnostics.ProviderMetadata(TrackingName: "{{propertyName}}"));
                            }
                            return _{{propertyName}}Provider;
                        }
                        set
                        {
                            _{{propertyName}}Provider = value;
                        }
                    }

                """;
        }
        else
        {
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial derived property {{propertyName}}.
                    /// Must be initialized by user code (e.g., in ConfigurePipeline).
                    /// </summary>
                    private Guacamole.Core.Providers.IncrementalValueProvider<{{itemType}}> _{{propertyName}}Field;

                    public partial Guacamole.Core.Providers.IncrementalValueProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Field.Node == null)
                            {
                                throw new System.InvalidOperationException(
                                    $"Derived property {{propertyName}} has not been initialized. " +
                                    "Initialize it in the constructor or ConfigurePipeline method.");
                            }
                            return _{{propertyName}}Field;
                        }
                    }

                """;
        }
    }
}
