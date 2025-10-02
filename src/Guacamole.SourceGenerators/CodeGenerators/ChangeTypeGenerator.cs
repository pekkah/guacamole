namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates strongly-typed change record types for each property.
/// Uses C# 11 raw string literals for code generation.
/// Zero boxing: all change records use concrete types.
/// </summary>
internal static class ChangeTypeGenerator
{
    /// <summary>
    /// Generates change record types for a property.
    /// </summary>
    public static string Generate(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionChangeRecords(property);
        }
        else
        {
            return GenerateSingleValueChangeRecord(property);
        }
    }

    private static string GenerateCollectionChangeRecords(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        // Generate strongly-typed change records using sealed records for performance
        return $$"""

                /// <summary>
                /// Strongly-typed change records for {{propertyName}} property.
                /// Sealed records with zero boxing overhead.
                /// </summary>
                public abstract record {{changeTypeName}}({{itemType}} Item, System.DateTimeOffset Timestamp)
                {
                    /// <summary>
                    /// Add operation - adds a new item to the collection.
                    /// </summary>
                    public sealed record Add({{itemType}} Item, System.DateTimeOffset Timestamp)
                        : {{changeTypeName}}(Item, Timestamp);

                    /// <summary>
                    /// Remove operation - removes an existing item from the collection.
                    /// </summary>
                    public sealed record Remove({{itemType}} Item, System.DateTimeOffset Timestamp)
                        : {{changeTypeName}}(Item, Timestamp);

                    /// <summary>
                    /// Replace operation - replaces an existing item with a new one.
                    /// </summary>
                    public sealed record Replace({{itemType}} OldItem, {{itemType}} NewItem, System.DateTimeOffset Timestamp)
                        : {{changeTypeName}}(NewItem, Timestamp)
                    {
                        public {{itemType}} OldItem { get; } = OldItem;
                    }
                }

            """;
    }

    private static string GenerateSingleValueChangeRecord(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        return $$"""

                /// <summary>
                /// Strongly-typed change record for {{propertyName}} property.
                /// Sealed record with zero boxing overhead.
                /// </summary>
                public abstract record {{changeTypeName}}(System.DateTimeOffset Timestamp)
                {
                    /// <summary>
                    /// Set operation - sets the value of the property.
                    /// </summary>
                    public sealed record Set({{itemType}} Value, System.DateTimeOffset Timestamp)
                        : {{changeTypeName}}(Timestamp)
                    {
                        public {{itemType}} Value { get; } = Value;
                    }
                }

            """;
    }

    /// <summary>
    /// Generates the change list field and current state field for a property.
    /// </summary>
    public static string GenerateChangeTrackingFields(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        if (property.IsCollection)
        {
            return $$"""

                    private readonly System.Collections.Generic.List<{{changeTypeName}}> _{{propertyName}}Changes = new();
                    private readonly System.Collections.Generic.HashSet<{{itemType}}> _current{{propertyName}} = new();

                """;
        }
        else
        {
            return $$"""

                    private readonly System.Collections.Generic.List<{{changeTypeName}}> _{{propertyName}}Changes = new();
                    private {{itemType}}? _current{{propertyName}};

                """;
        }
    }
}
