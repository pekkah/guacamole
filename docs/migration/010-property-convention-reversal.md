# Migration Guide: Property Convention Reversal (v0.1.0)

## Overview

This is a **breaking change** that reverses the property generation convention in Guacamole source generators.

### What Changed

**OLD Convention (before v0.1.0):**
- Properties **with setters** (`{ get; set; }`) → Auto-managed (mutation methods generated)
- Properties **without setters** (`{ get; }`) → User-managed (no generation)

**NEW Convention (v0.1.0 and later):**
- Properties **without setters** (`{ get; }`) → Auto-managed (mutation methods generated)
- Properties **with setters** (`{ get; set; }`) → User-managed (no generation)

### Why This Change?

The new convention is more intuitive and aligns with C# 13's partial property semantics:
- Get-only properties signal "I need an implementation" → Framework provides it
- Properties with setters signal "I'll manage this myself" → No auto-generation

## Migration Steps

### Step 1: Update Property Declarations

For all properties where you want Guacamole to generate mutation methods, **remove the setter**:

**Before:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // Auto-managed - mutation methods generated
    public partial IncrementalValueProvider<string> Name { get; set; }
    public partial IncrementalValuesProvider<SourceFile> Files { get; set; }
}
```

**After:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // Auto-managed - mutation methods generated
    public partial IncrementalValueProvider<string> Name { get; }
    public partial IncrementalValuesProvider<SourceFile> Files { get; }
}
```

### Step 2: Handle User-Managed Properties

If you have properties that you manage manually (with custom computation logic), **add a setter**:

**Before:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // User-managed - no generation
    public partial IncrementalValueProvider<int> ComputedValue { get; }

    // Custom computation logic
    public MyContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
        // Custom setup...
    }
}
```

**After:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // User-managed - no generation
    public partial IncrementalValueProvider<int> ComputedValue { get; set; }

    // Custom computation logic
    public MyContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
        // Custom setup...
    }
}
```

### Step 3: Update Method Calls (No Changes Required)

The generated mutation method signatures remain the same:

```csharp
// Single value mutations
context.SetName("value");

// Collection mutations
context.AddFiles(file);
context.RemoveFiles(file);
context.ReplaceFiles(oldFile, newFile);
context.AddFiless(fileCollection);  // Batch operations
context.RemoveFiless(fileCollection);
```

### Step 4: Rebuild and Test

```bash
# Clean and rebuild
dotnet clean
dotnet build

# Run tests
dotnet test
```

## Common Scenarios

### Scenario 1: Simple Auto-Managed Context

**Before:**
```csharp
[ComputingContext]
public partial class SimpleContext : ComputingContextBase
{
    public partial IncrementalValueProvider<string> Input { get; set; }
    public partial IncrementalValuesProvider<string> Items { get; set; }

    public SimpleContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
    }
}
```

**After:**
```csharp
[ComputingContext]
public partial class SimpleContext : ComputingContextBase
{
    public partial IncrementalValueProvider<string> Input { get; }
    public partial IncrementalValuesProvider<string> Items { get; }

    public SimpleContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
    }
}
```

### Scenario 2: Mixed Auto-Managed and User-Managed

**Before:**
```csharp
[ComputingContext]
public partial class MixedContext : ComputingContextBase
{
    // Auto-managed
    public partial IncrementalValueProvider<string> Name { get; set; }

    // User-managed
    public partial IncrementalValueProvider<int> CustomValue { get; }

    public MixedContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
        // CustomValue setup...
    }
}
```

**After:**
```csharp
[ComputingContext]
public partial class MixedContext : ComputingContextBase
{
    // Auto-managed
    public partial IncrementalValueProvider<string> Name { get; }

    // User-managed
    public partial IncrementalValueProvider<int> CustomValue { get; set; }

    public MixedContext(IServiceProvider services) : base()
    {
        InitializeComputationNodes();
        // CustomValue setup...
    }
}
```

## Compiler Errors and Fixes

### Error: CS9248 - Partial property must have an implementation part

**Error Message:**
```
error CS9248: Partial property 'MyContext.Name' must have an implementation part.
```

**Cause:** You have a property with a setter, but the generator no longer creates an implementation for it.

**Fix:** Remove the setter to make it auto-managed:
```csharp
// Change this:
public partial IncrementalValueProvider<string> Name { get; set; }

// To this:
public partial IncrementalValueProvider<string> Name { get; }
```

### Error: CS0649 - Field is never assigned to

**Error Message:**
```
warning CS0649: Field 'MyContext._NameProvider' is never assigned to, and will always have its default value
```

**Cause:** Generator created a backing field for a user-managed property (with setter), but you're not initializing it.

**Fix:** Either remove the setter (auto-managed) or initialize the field yourself:
```csharp
// Option 1: Make it auto-managed (remove setter)
public partial IncrementalValueProvider<string> Name { get; }

// Option 2: Initialize it yourself (keep setter)
public partial IncrementalValueProvider<string> Name { get; set; }

public MyContext(IServiceProvider services) : base()
{
    InitializeComputationNodes();
    Name = new IncrementalValueProvider<string>(...);
}
```

## Testing Your Migration

1. **Verify all contexts compile without errors**
2. **Run your full test suite**
3. **Check that mutation methods work as expected:**
   ```csharp
   context.SetName("test");
   context.SaveChanges();
   // Verify handlers are called
   ```
4. **Verify change tracking works:**
   ```csharp
   context.AddItems("item1");
   Assert.True(context.HasPendingChanges);
   context.SaveChanges();
   Assert.False(context.HasPendingChanges);
   ```

## Need Help?

If you encounter issues during migration:
1. Check the compiler errors - they usually point to the exact properties that need updating
2. Review the examples in this guide
3. Refer to the [Generated Code Reference](../generated-code-reference.md) for detailed API documentation
4. File an issue on GitHub with your specific scenario

## Summary Checklist

- [ ] Remove setters from auto-managed properties (`{ get; set; }` → `{ get; }`)
- [ ] Add setters to user-managed properties (`{ get; }` → `{ get; set; }`)
- [ ] Clean and rebuild solution
- [ ] Run all tests
- [ ] Verify mutation methods work correctly
- [ ] Check change tracking behavior
