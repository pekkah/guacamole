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

        // REVERSED LOGIC: InputProperties now contains get-only properties (HasSetter=false)
        // Generate auto-managed implementation for get-only properties
        if (!property.HasSetter)  // Changed from HasSetter to !HasSetter
        {
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial property {{propertyName}}.
                    /// Auto-managed by framework (get-only = automatic mutation methods).
                    /// </summary>
                    private Guacamole.IncrementalValuesProvider<{{itemType}}> _{{propertyName}}Provider;
                    private readonly object _{{propertyName}}ProviderLock = new object();

                    public partial Guacamole.IncrementalValuesProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Provider.Node == null)
                            {
                                lock (_{{propertyName}}ProviderLock)
                                {
                                    if (_{{propertyName}}Provider.Node == null)
                                    {
                                        _{{propertyName}}Provider = new Guacamole.IncrementalValuesProvider<{{itemType}}>(
                                            {{propertyName}}NodeLazy,
                                            Graph,
                                            new Guacamole.Core.Diagnostics.ProviderMetadata(TrackingName: "{{propertyName}}"));
                                    }
                                }
                            }
                            return _{{propertyName}}Provider;
                        }
                    }

                """;
        }
        else
        {
            // User-managed properties (with setter) - should not be in InputProperties list
            // This code path should not be reached after classification logic reversal
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial user-managed property {{propertyName}}.
                    /// Must be initialized by user code (has setter = manual management).
                    /// </summary>
                    private Guacamole.IncrementalValuesProvider<{{itemType}}> _{{propertyName}}Field;

                    public partial Guacamole.IncrementalValuesProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Field.Node == null)
                            {
                                throw new System.InvalidOperationException(
                                    $"User-managed property {{propertyName}} has not been initialized. " +
                                    "Initialize it in the constructor.");
                            }
                            return _{{propertyName}}Field;
                        }
                        set
                        {
                            _{{propertyName}}Field = value;
                        }
                    }

                """;
        }
    }

    private static string GenerateSingleValueProperty(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        // REVERSED LOGIC: InputProperties now contains get-only properties (HasSetter=false)
        if (!property.HasSetter)  // Changed from HasSetter to !HasSetter
        {
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial property {{propertyName}}.
                    /// Auto-managed by framework (get-only = automatic mutation methods).
                    /// </summary>
                    private Guacamole.IncrementalValueProvider<{{itemType}}> _{{propertyName}}Provider;
                    private readonly object _{{propertyName}}ProviderLock = new object();

                    public partial Guacamole.IncrementalValueProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Provider.Node == null)
                            {
                                lock (_{{propertyName}}ProviderLock)
                                {
                                    if (_{{propertyName}}Provider.Node == null)
                                    {
                                        _{{propertyName}}Provider = new Guacamole.IncrementalValueProvider<{{itemType}}>(
                                            {{propertyName}}NodeLazy,
                                            Graph,
                                            new Guacamole.Core.Diagnostics.ProviderMetadata(TrackingName: "{{propertyName}}"));
                                    }
                                }
                            }
                            return _{{propertyName}}Provider;
                        }
                    }

                """;
        }
        else
        {
            // User-managed properties (with setter) - should not be in InputProperties list
            return $$"""

                    /// <summary>
                    /// Implementing declaration for partial user-managed property {{propertyName}}.
                    /// Must be initialized by user code (has setter = manual management).
                    /// </summary>
                    private Guacamole.IncrementalValueProvider<{{itemType}}> _{{propertyName}}Field;

                    public partial Guacamole.IncrementalValueProvider<{{itemType}}> {{propertyName}}
                    {
                        get
                        {
                            if (_{{propertyName}}Field.Node == null)
                            {
                                throw new System.InvalidOperationException(
                                    $"User-managed property {{propertyName}} has not been initialized. " +
                                    "Initialize it in the constructor.");
                            }
                            return _{{propertyName}}Field;
                        }
                        set
                        {
                            _{{propertyName}}Field = value;
                        }
                    }

                """;
        }
    }
}
