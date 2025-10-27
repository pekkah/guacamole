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

    /// <summary>
    /// Generates backward compatibility mutation methods.
    /// These wrap the new push-based source API for existing tests.
    /// </summary>
    public static string GenerateBackwardCompatibilityMethods(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionMutationMethods(property);
        }
        else
        {
            return GenerateSingleValueMutationMethod(property);
        }
    }

    /// <summary>
    /// Generates batching infrastructure for SaveChanges compatibility.
    /// </summary>
    public static string GenerateBatchingInfrastructure()
    {
        return """
                private readonly System.Collections.Generic.List<System.Action> _pendingOperations = new();
                private readonly object _batchLock = new object();

        """;
    }

    /// <summary>
    /// Generates SaveChanges method that executes pending operations and triggers outputs.
    /// </summary>
    public static string GenerateSaveChangesMethod()
    {
        return """

                /// <summary>
                /// Executes all pending mutations and triggers registered output handlers.
                /// Provided for backward compatibility with mutation-based API.
                /// Note: Due to the push-based architecture, individual operations may trigger
                /// outputs immediately. This matches Roslyn's incremental compilation behavior.
                /// </summary>
                public void SaveChanges()
                {
                    // Execute all pending operations
                    lock (_batchLock)
                    {
                        if (_pendingOperations.Count == 0)
                        {
                            // No pending operations, just trigger outputs with current state
                            TriggerOutputs();
                            return;
                        }

                        foreach (var operation in _pendingOperations)
                        {
                            operation();
                        }
                        _pendingOperations.Clear();
                    }

                    // Note: Sources trigger outputs automatically on changes via Changed events.
                    // We don't call TriggerOutputs() again here to avoid double-triggering.
                }

        """;
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
                    () => Graph); // Pass graph provider, not direct reference
                Graph = Graph.AddNode(_{{propertyName}}Source.GetNode()); // Register node
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
                    () => Graph); // Pass graph provider, not direct reference
                Graph = Graph.AddNode(_{{propertyName}}Source.GetNode()); // Register node
                SubscribeToSource(_{{propertyName}}Source);
        """;
    }

    private static string GenerateSingleValueMutationMethod(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Sets the value of {{propertyName}}.
                /// Backward compatibility wrapper for mutation-based API.
                /// Changes are batched until SaveChanges() is called.
                /// </summary>
                public void Set{{propertyName}}({{itemType}} value)
                {
                    if (_{{propertyName}}Source == null)
                        throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                    lock (_batchLock)
                    {
                        _pendingOperations.Add(() => _{{propertyName}}Source.Set(value));
                    }
                }

        """;
    }

    private static string GenerateCollectionMutationMethods(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;

        return $$"""

                /// <summary>
                /// Adds an item to {{propertyName}}.
                /// Backward compatibility wrapper for mutation-based API.
                /// Changes are batched until SaveChanges() is called.
                /// </summary>
                public void Add{{propertyName}}({{itemType}} item)
                {
                    if (_{{propertyName}}Source == null)
                        throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                    lock (_batchLock)
                    {
                        _pendingOperations.Add(() => _{{propertyName}}Source.Push(item));
                    }
                }

                /// <summary>
                /// Removes an item from {{propertyName}}.
                /// Backward compatibility wrapper for mutation-based API.
                /// Changes are batched until SaveChanges() is called.
                /// </summary>
                public void Remove{{propertyName}}({{itemType}} item)
                {
                    if (_{{propertyName}}Source == null)
                        throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                    lock (_batchLock)
                    {
                        _pendingOperations.Add(() => _{{propertyName}}Source.Remove(item));
                    }
                }

                /// <summary>
                /// Replaces an item in {{propertyName}}.
                /// Backward compatibility wrapper for mutation-based API.
                /// Changes are batched until SaveChanges() is called.
                /// </summary>
                public void Replace{{propertyName}}({{itemType}} oldItem, {{itemType}} newItem)
                {
                    if (_{{propertyName}}Source == null)
                        throw new System.InvalidOperationException($"Property {{propertyName}} has not been initialized. This is a framework bug.");

                    lock (_batchLock)
                    {
                        _pendingOperations.Add(() => _{{propertyName}}Source.Replace(oldItem, newItem));
                    }
                }

        """;
    }
}
