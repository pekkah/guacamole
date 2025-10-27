namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates source-backed property implementations.
/// Replaces the old mutation-based approach with push-based reactive sources.
/// </summary>
internal static class SourcePropertyGenerator
{
    /// <summary>
    /// Generates source field and property implementation.
    /// </summary>
    public static string GenerateSourceField(PropertyInfo property, string className)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionSourceField(property, className);
        }
        else
        {
            return GenerateSingleValueSourceField(property, className);
        }
    }

    /// <summary>
    /// Generates the property implementation that returns the source's provider.
    /// </summary>
    public static string GeneratePropertyImplementation(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionPropertyImplementation(property);
        }
        else
        {
            return GenerateSingleValuePropertyImplementation(property);
        }
    }

    /// <summary>
    /// Generates the source accessor property (e.g., SourceFilesSource).
    /// </summary>
    public static string GenerateSourceAccessor(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionSourceAccessor(property);
        }
        else
        {
            return GenerateSingleValueSourceAccessor(property);
        }
    }

    /// <summary>
    /// Generates source initialization code for the constructor.
    /// </summary>
    public static string GenerateSourceInitialization(PropertyInfo property, string className)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionSourceInitialization(property, className);
        }
        else
        {
            return GenerateSingleValueSourceInitialization(property, className);
        }
    }

    private static string GenerateCollectionSourceField(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""
                private Guacamole.Sources.IncrementalSource<{{itemType}}>? _{{propertyName}}Source;
        """;
    }

    private static string GenerateCollectionPropertyImplementation(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Provider for {{propertyName}}.
                /// Backed by a push-based incremental source.
                /// </summary>
                public partial Guacamole.IncrementalValuesProvider<{{itemType}}> {{propertyName}}
                {
                    get
                    {
                        if (_{{propertyName}}Source == null)
                            throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                        return _{{propertyName}}Source.AsProvider();
                    }
                }

        """;
    }

    private static string GenerateCollectionSourceAccessor(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Source accessor for {{propertyName}}.
                /// Use this to push data into the incremental compilation pipeline.
                /// </summary>
                public Guacamole.Sources.IIncrementalSource<{{itemType}}> {{propertyName}}Source
                {
                    get
                    {
                        if (_{{propertyName}}Source == null)
                            throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                        return _{{propertyName}}Source;
                    }
                }

        """;
    }

    private static string GenerateCollectionSourceInitialization(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""
                _{{propertyName}}Source = new Guacamole.Sources.IncrementalSource<{{itemType}}>(
                    "{{className}}.{{propertyName}}",
                    Graph);
                SubscribeToSource(_{{propertyName}}Source);
        """;
    }

    private static string GenerateSingleValueSourceField(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""
                private Guacamole.Sources.IncrementalValueSource<{{itemType}}>? _{{propertyName}}Source;
        """;
    }

    private static string GenerateSingleValuePropertyImplementation(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Provider for {{propertyName}}.
                /// Backed by a push-based incremental value source.
                /// </summary>
                public partial Guacamole.IncrementalValueProvider<{{itemType}}> {{propertyName}}
                {
                    get
                    {
                        if (_{{propertyName}}Source == null)
                            throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                        return _{{propertyName}}Source.AsProvider();
                    }
                }

        """;
    }

    private static string GenerateSingleValueSourceAccessor(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Source accessor for {{propertyName}}.
                /// Use this to set the value in the incremental compilation pipeline.
                /// </summary>
                public Guacamole.Sources.IIncrementalValueSource<{{itemType}}> {{propertyName}}Source
                {
                    get
                    {
                        if (_{{propertyName}}Source == null)
                            throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                        return _{{propertyName}}Source;
                    }
                }

        """;
    }

    private static string GenerateSingleValueSourceInitialization(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""
                _{{propertyName}}Source = new Guacamole.Sources.IncrementalValueSource<{{itemType}}>(
                    "{{className}}.{{propertyName}}",
                    Graph);
                SubscribeToSource(_{{propertyName}}Source);
        """;
    }
}
