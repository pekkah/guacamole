using System.Text;

namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates Add/Remove/Replace mutation methods for input source properties.
/// Uses C# 11 raw string literals for code generation.
/// </summary>
internal static class MutationMethodGenerator
{
    /// <summary>
    /// Generates mutation methods for a single property.
    /// </summary>
    public static string Generate(PropertyInfo property, string className)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionMethods(property, className);
        }
        else
        {
            return GenerateSingleValueMethod(property, className);
        }
    }

    private static string GenerateCollectionMethods(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        // Using raw string literals for clean code generation
        return $$"""

                /// <summary>
                /// Adds an item to {{propertyName}}. Throws if item already exists (fail-fast).
                /// </summary>
                public void Add{{propertyName}}({{itemType}} item)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(item);

                    Lock.EnterWriteLock();
                    try
                    {
                        // Check for duplicate using equality comparison
                        if (_current{{propertyName}}.Contains(item))
                        {
                            throw Guacamole.Exceptions.ContextOperationException.DuplicateItem("{{propertyName}}", item);
                        }

                        // Track change in property-specific list (zero boxing)
                        _{{propertyName}}Changes.Add(new {{changeTypeName}}.Add(item, System.DateTimeOffset.UtcNow));
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

                /// <summary>
                /// Removes an item from {{propertyName}}. Returns false if item doesn't exist.
                /// </summary>
                public bool Remove{{propertyName}}({{itemType}} item)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(item);

                    Lock.EnterWriteLock();
                    try
                    {
                        if (!_current{{propertyName}}.Contains(item))
                        {
                            return false;
                        }

                        _{{propertyName}}Changes.Add(new {{changeTypeName}}.Remove(item, System.DateTimeOffset.UtcNow));
                        return true;
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

                /// <summary>
                /// Replaces an item in {{propertyName}}.
                /// </summary>
                public void Replace{{propertyName}}({{itemType}} oldItem, {{itemType}} newItem)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(oldItem);
                    System.ArgumentNullException.ThrowIfNull(newItem);

                    Lock.EnterWriteLock();
                    try
                    {
                        if (!_current{{propertyName}}.Contains(oldItem))
                        {
                            throw new System.InvalidOperationException($"Cannot replace item that doesn't exist in {{propertyName}}");
                        }

                        _{{propertyName}}Changes.Add(new {{changeTypeName}}.Replace(oldItem, newItem, System.DateTimeOffset.UtcNow));
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

                /// <summary>
                /// Adds multiple items to {{propertyName}} in a batch.
                /// </summary>
                public void Add{{propertyName}}s(System.Collections.Generic.IEnumerable<{{itemType}}> items)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(items);

                    Lock.EnterWriteLock();
                    try
                    {
                        int index = 0;
                        foreach (var item in items)
                        {
                            if (_current{{propertyName}}.Contains(item))
                            {
                                throw Guacamole.Exceptions.ContextOperationException.BatchOperationFailed(
                                    "Add", "{{propertyName}}", index, item,
                                    new System.InvalidOperationException("Duplicate item"));
                            }

                            _{{propertyName}}Changes.Add(new {{changeTypeName}}.Add(item, System.DateTimeOffset.UtcNow));
                            index++;
                        }
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

                /// <summary>
                /// Removes multiple items from {{propertyName}} in a batch.
                /// </summary>
                public void Remove{{propertyName}}s(System.Collections.Generic.IEnumerable<{{itemType}}> items)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(items);

                    Lock.EnterWriteLock();
                    try
                    {
                        foreach (var item in items)
                        {
                            if (_current{{propertyName}}.Contains(item))
                            {
                                _{{propertyName}}Changes.Add(new {{changeTypeName}}.Remove(item, System.DateTimeOffset.UtcNow));
                            }
                        }
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

            """;
    }

    private static string GenerateSingleValueMethod(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        return $$"""

                /// <summary>
                /// Sets the value of {{propertyName}}.
                /// </summary>
                public void Set{{propertyName}}({{itemType}} value)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(value);

                    Lock.EnterWriteLock();
                    try
                    {
                        _{{propertyName}}Changes.Add(new {{changeTypeName}}.Set(value, System.DateTimeOffset.UtcNow));
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

            """;
    }
}
