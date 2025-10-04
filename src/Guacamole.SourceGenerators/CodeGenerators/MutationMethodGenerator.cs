using System.Linq;
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

    /// <summary>
    /// Determines if a type requires null checking.
    /// Value types (except Nullable<T>) don't need null checks.
    /// </summary>
    private static bool RequiresNullCheck(string itemType)
    {
        // Check if it's a nullable value type (ends with ?)
        if (itemType.EndsWith("?"))
        {
            // It's nullable, so it needs null check
            return true;
        }

        // Common value types that don't need null checks
        var nonNullableValueTypes = new[]
        {
            "int", "long", "short", "byte", "sbyte",
            "uint", "ulong", "ushort",
            "float", "double", "decimal",
            "bool", "char",
            "System.Int32", "System.Int64", "System.Int16", "System.Byte", "System.SByte",
            "System.UInt32", "System.UInt64", "System.UInt16",
            "System.Single", "System.Double", "System.Decimal",
            "System.Boolean", "System.Char",
            "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.Guid"
        };

        // If it's a known non-nullable value type, no null check needed
        if (nonNullableValueTypes.Contains(itemType))
        {
            return false;
        }

        // For reference types and unknown types, require null check
        // This is conservative - we assume it needs checking unless we know otherwise
        return true;
    }

    /// <summary>
    /// Generates the null check code if required for the type.
    /// </summary>
    private static string GenerateNullCheck(string itemType, string parameterName)
    {
        if (RequiresNullCheck(itemType))
        {
            return $"System.ArgumentNullException.ThrowIfNull({parameterName});";
        }
        return string.Empty;
    }

    private static string GenerateCollectionMethods(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";
        var nullCheckItem = GenerateNullCheck(itemType, "item");
        var nullCheckOldItem = GenerateNullCheck(itemType, "oldItem");
        var nullCheckNewItem = GenerateNullCheck(itemType, "newItem");

        // Fix naming: use "Batch" suffix instead of double-s for plural properties
        var batchSuffix = propertyName.EndsWith("s") ? "Batch" : "s";

        // Using raw string literals for clean code generation
        var code = new StringBuilder();

        code.AppendLine($$"""

                /// <summary>
                /// Adds an item to {{propertyName}}. Throws if item already exists (fail-fast).
                /// </summary>
                public void Add{{propertyName}}({{itemType}} item)
                {
                    ThrowIfDisposed();
                    {{nullCheckItem}}

                    Lock.EnterWriteLock();
                    try
                    {
                        // Check for duplicate using equality comparison
                        if (_current{{propertyName}}.Contains(item))
                        {
                            throw Guacamole.Exceptions.ContextOperationException.DuplicateItem("{{propertyName}}", item);
                        }

                        // Track change in property-specific list (zero boxing)
                        // Cache invalidation is deferred to SaveChanges() for efficiency
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
                    {{nullCheckItem}}

                    Lock.EnterWriteLock();
                    try
                    {
                        if (!_current{{propertyName}}.Contains(item))
                        {
                            return false;
                        }

                        // Track change - cache invalidation deferred to SaveChanges()
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
                    {{nullCheckOldItem}}
                    {{nullCheckNewItem}}

                    Lock.EnterWriteLock();
                    try
                    {
                        // Early exit if replacing with same item (no-op)
                        if (System.Collections.Generic.EqualityComparer<{{itemType}}>.Default.Equals(oldItem, newItem))
                        {
                            return;
                        }

                        if (!_current{{propertyName}}.Contains(oldItem))
                        {
                            throw new System.InvalidOperationException($"Cannot replace item that doesn't exist in {{propertyName}}");
                        }

                        // Check if new item already exists
                        if (_current{{propertyName}}.Contains(newItem))
                        {
                            throw new System.InvalidOperationException($"Cannot replace with item that already exists in {{propertyName}}");
                        }

                        // Track change - cache invalidation deferred to SaveChanges()
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
                public void Add{{propertyName}}{{batchSuffix}}(System.Collections.Generic.IEnumerable<{{itemType}}> items)
                {
                    ThrowIfDisposed();
                    System.ArgumentNullException.ThrowIfNull(items);

                    Lock.EnterWriteLock();
                    try
                    {
                        // Track items added in this batch to prevent duplicates within the batch
                        var addedInBatch = new System.Collections.Generic.HashSet<{{itemType}}>();
                        int index = 0;

                        foreach (var item in items)
                        {
                            // Check against both current state and items added in this batch
                            if (_current{{propertyName}}.Contains(item) || addedInBatch.Contains(item))
                            {
                                throw Guacamole.Exceptions.ContextOperationException.BatchOperationFailed(
                                    "Add", "{{propertyName}}", index, item,
                                    new System.InvalidOperationException("Duplicate item"));
                            }

                            _{{propertyName}}Changes.Add(new {{changeTypeName}}.Add(item, System.DateTimeOffset.UtcNow));
                            addedInBatch.Add(item);
                            index++;
                        }

                        // Cache invalidation deferred to SaveChanges() for efficiency
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

                /// <summary>
                /// Removes multiple items from {{propertyName}} in a batch.
                /// </summary>
                public void Remove{{propertyName}}{{batchSuffix}}(System.Collections.Generic.IEnumerable<{{itemType}}> items)
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

                        // Cache invalidation deferred to SaveChanges() for efficiency
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

            """);

        return code.ToString();
    }

    private static string GenerateSingleValueMethod(PropertyInfo property, string className)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";
        var nullCheckValue = GenerateNullCheck(itemType, "value");

        return $$"""

                /// <summary>
                /// Sets the value of {{propertyName}}.
                /// </summary>
                public void Set{{propertyName}}({{itemType}} value)
                {
                    ThrowIfDisposed();
                    {{nullCheckValue}}

                    Lock.EnterWriteLock();
                    try
                    {
                        // Track change - cache invalidation deferred to SaveChanges()
                        // This allows equality comparison to prevent unnecessary invalidation
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
