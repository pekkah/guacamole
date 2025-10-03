# Data Model: Reverse Property Generation Logic

**Feature**: 010-property-genration-current
**Date**: 2025-10-03

## Overview

This feature inverts the semantic meaning of property classification in the source generator. The data structures remain unchanged; only the classification logic changes.

## Core Entities

### PropertyInfo

**Location**: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs:294-310`

**Purpose**: Represents analyzed property metadata for code generation

**Fields**:
- `Name` (string): Property name
- `ProviderType` (string): "IncrementalValueProvider" or "IncrementalValuesProvider"
- `ItemType` (string): Generic type argument (e.g., "string", "SourceFile")
- `HasSetter` (bool): Whether property has a setter declared
- `IsCollection` (bool): Whether it's IncrementalValuesProvider (collection) vs IncrementalValueProvider (single value)

**Semantic Change**:

| Field | Old Meaning | New Meaning |
|-------|-------------|-------------|
| `HasSetter = true` | "Generate mutation methods for this property" | "User will manage this property manually" |
| `HasSetter = false` | "User will implement derived logic" | "Generate mutation methods for this property" |

**Relationships**:
- Created by: `PropertyAnalyzer.Analyze()`
- Consumed by: All 5 code generators (via InputProperties/DerivedProperties lists)

### PropertyAnalysisResult

**Location**: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs:315-330`

**Purpose**: Contains classification results and diagnostics

**Fields**:
- `InputProperties` (List<PropertyInfo>): Properties requiring code generation infrastructure
- `DerivedProperties` (List<PropertyInfo>): Properties user manages themselves
- `Diagnostics` (List<Diagnostic>): Any analysis errors/warnings

**Semantic Change**:

| List | Old Contents | New Contents |
|------|--------------|--------------|
| `InputProperties` | Properties with setters | Properties **without** setters (get-only) |
| `DerivedProperties` | Properties without setters | Properties **with** setters |

## State Transitions

### Property Classification Flow

**Before (Current Behavior)**:
```
User declares property
    ↓
PropertyAnalyzer.Analyze()
    ↓
Has setter?
    ├─ YES → Add to InputProperties → Generate mutations/tracking/nodes
    └─ NO  → Add to DerivedProperties → Generate property impl only
```

**After (New Behavior)**:
```
User declares property
    ↓
PropertyAnalyzer.Analyze()
    ↓
Has setter?
    ├─ YES → Add to DerivedProperties → Generate property impl only
    └─ NO  → Add to InputProperties → Generate mutations/tracking/nodes
```

## Validation Rules

### Unchanged Validation Rules

These rules remain enforced regardless of classification:

1. **Partial Property Required** (GUAC0003)
   - Property must be declared `partial`
   - Location: PropertyAnalyzer.cs:33-52

2. **IncrementalProvider Type Required**
   - Property type must be `IncrementalValueProvider<T>` or `IncrementalValuesProvider<T>`
   - Location: PropertyAnalyzer.cs:56-57

3. **No Duplicate Properties** (GUAC0001)
   - Property names must be unique within context
   - Location: PropertyAnalyzer.cs:95-122

4. **Consistent Type Parameters** (GUAC0002)
   - Properties with same name must have same type argument
   - Location: PropertyAnalyzer.cs:124-160

5. **Equality Implementation Warning** (GUAC0004)
   - Types should implement `IEquatable<T>` for performance
   - Location: PropertyAnalyzer.cs:185-230

### New Behavioral Expectations

After the change, developers should expect:

1. **Get-Only Property Behavior**:
   - Example: `public partial IncrementalValueProvider<string> Name { get; }`
   - Generated: `SetName(string value)` mutation method
   - Generated: `NameChange` change record types
   - Generated: `NameNode` computation node
   - Generated: SaveChanges() logic for Name

2. **Property with Setter Behavior**:
   - Example: `public partial IncrementalValueProvider<int> Count { get; set; }`
   - Generated: Property implementation only (getter/setter)
   - NOT Generated: Mutation methods
   - NOT Generated: Change records
   - User Responsibility: Initialize in constructor, manage changes manually

## Entity Lifecycle

### PropertyInfo Lifecycle

1. **Creation**: PropertyAnalyzer.Analyze() creates PropertyInfo for each partial property
2. **Classification**: Added to InputProperties OR DerivedProperties based on HasSetter
3. **Consumption**: Each generator reads from InputProperties/DerivedProperties lists
4. **Code Generation**: Generators emit C# code based on classification
5. **Disposal**: PropertyInfo is temporary, exists only during compilation

**Lifecycle Diagram**:
```
Parse source code
    ↓
Create PropertyInfo instances
    ↓
Classify into lists (LOGIC INVERTED HERE)
    ↓
Generate code for each property
    ↓
Emit generated .cs files
```

## Data Invariants

### Pre-Change Invariants (maintain these)

1. `InputProperties ∪ DerivedProperties = AllProperties`
2. `InputProperties ∩ DerivedProperties = ∅` (disjoint sets)
3. `∀p ∈ Properties: p.IsPartialDefinition = true`
4. `∀p ∈ Properties: IsIncrementalProvider(p.Type) = true`

### Post-Change Invariants (same as above)

Classification logic inverts, but invariants remain:
- Still disjoint, complete partition
- Still only partial properties
- Still only IncrementalProvider types

**Key Insight**: The change doesn't violate any invariants, it just swaps which properties go into which set.

## Migration Impact

### User-Visible Data Model Changes

**Example Context Before**:
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase {
    // Generates SetSourceFiles(), change tracking, etc.
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; set; }

    // User implements in constructor
    public partial IncrementalValuesProvider<SyntaxTree> ParsedTrees { get; }
}
```

**Same Context After**:
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase {
    // Generates SetSourceFiles(), change tracking, etc.
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }  // REMOVED SETTER

    // User implements in constructor
    public partial IncrementalValuesProvider<SyntaxTree> ParsedTrees { get; set; }  // ADDED SETTER
}
```

**Migration Rule**: To preserve behavior, invert all property accessor declarations (add/remove setters).

## Summary

| Aspect | Change |
|--------|--------|
| **Data Structures** | No changes (PropertyInfo, PropertyAnalysisResult unchanged) |
| **Classification Logic** | Inverted (lines 66-73 in PropertyAnalyzer.cs) |
| **Validation Rules** | No changes (all diagnostics still apply) |
| **Invariants** | No changes (sets still disjoint and complete) |
| **User Impact** | Must invert setter presence to preserve behavior |
