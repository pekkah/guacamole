using System.Collections.Generic;
using System.Linq;

namespace Guacamole.SourceGenerators.CodeGenerators;

/// <summary>
/// Generates SaveChanges and SaveChangesAsync methods with optimized commit logic.
/// Uses C# 11 raw string literals for code generation.
/// Implements fail-fast error handling and incremental invalidation.
/// </summary>
internal static class SaveChangesGenerator
{
    /// <summary>
    /// Generates SaveChanges methods for all properties.
    /// </summary>
    public static string Generate(List<PropertyInfo> properties)
    {
        // If no properties, don't generate SaveChanges methods
        if (properties.Count == 0)
            return "";

        var applyChangesStatements = string.Join("\n", properties.Select(GenerateApplyStatement));
        var clearChangesStatements = string.Join("\n", properties.Select(p => $"                _{p.Name}Changes.Clear();"));
        var hasPendingChangesCheck = string.Join(" || ", properties.Select(p => $"_{p.Name}Changes.Count > 0"));

        return $$"""

                /// <summary>
                /// Indicates if there are pending changes waiting to be committed.
                /// </summary>
                public bool HasPendingChanges => {{hasPendingChangesCheck}};

                /// <summary>
                /// Commits all tracked changes and triggers incremental recomputation.
                /// Fails fast on first error with detailed diagnostics.
                /// After changes are committed, triggers all registered output handlers.
                /// </summary>
                public void SaveChanges()
                {
                    ThrowIfDisposed();

                    Lock.EnterWriteLock();
                    try
                    {
                        // Apply changes for each property (fail-fast)
            {{applyChangesStatements}}

                        // Clear all change lists after successful commit
            {{clearChangesStatements}}
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }

                    // Trigger output handlers after changes are committed
                    // This recomputes registered providers and calls their handlers
                    TriggerOutputs();
                }

                /// <summary>
                /// Async version of SaveChanges for compatibility with async workflows.
                /// </summary>
                public System.Threading.Tasks.Task SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
                {
                    SaveChanges();
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                /// <summary>
                /// Discards all pending changes without committing.
                /// </summary>
                public void DiscardChanges()
                {
                    ThrowIfDisposed();

                    Lock.EnterWriteLock();
                    try
                    {
            {{clearChangesStatements}}
                    }
                    finally
                    {
                        Lock.ExitWriteLock();
                    }
                }

            """;
    }

    private static string GenerateApplyStatement(PropertyInfo property)
    {
        if (property.IsCollection)
        {
            return GenerateCollectionApply(property);
        }
        else
        {
            return GenerateSingleValueApply(property);
        }
    }

    private static string GenerateCollectionApply(PropertyInfo property)
    {
        var propertyName = property.Name;
        var changeTypeName = $"{propertyName}Change";

        return $$"""
                        // Apply {{propertyName}} changes with fail-fast error handling
                        if (_{{propertyName}}Changes.Count > 0)
                        {
                            bool collectionChanged = false;
                            foreach (var change in _{{propertyName}}Changes)
                            {
                                switch (change)
                                {
                                    case {{changeTypeName}}.Add add:
                                        _current{{propertyName}}.Add(add.Item);
                                        collectionChanged = true;
                                        break;
                                    case {{changeTypeName}}.Remove remove:
                                        collectionChanged |= _current{{propertyName}}.Remove(remove.Item);
                                        break;
                                    case {{changeTypeName}}.Replace replace:
                                        var removed = _current{{propertyName}}.Remove(replace.OldItem);
                                        if (removed)
                                        {
                                            _current{{propertyName}}.Add(replace.Item);
                                            collectionChanged = true;
                                        }
                                        break;
                                }
                            }

                            // Only invalidate if collection actually changed
                            if (collectionChanged)
                            {
                                InvalidateNodeCache({{propertyName}}NodeLazy.Id);
                            }
                        }
            """;
    }

    private static string GenerateSingleValueApply(PropertyInfo property)
    {
        var propertyName = property.Name;
        var itemType = property.ItemType;
        var changeTypeName = $"{propertyName}Change";

        return $$"""
                        // Apply {{propertyName}} changes with equality check
                        if (_{{propertyName}}Changes.Count > 0)
                        {
                            var lastChange = _{{propertyName}}Changes[_{{propertyName}}Changes.Count - 1];
                            if (lastChange is {{changeTypeName}}.Set set)
                            {
                                // Only update and invalidate if value actually changed
                                // EqualityComparer handles null values correctly
                                if (!System.Collections.Generic.EqualityComparer<{{itemType}}>.Default.Equals(_current{{propertyName}}, set.Value))
                                {
                                    _current{{propertyName}} = set.Value;
                                    InvalidateNodeCache({{propertyName}}NodeLazy.Id);
                                }
                            }
                        }
            """;
    }
}
