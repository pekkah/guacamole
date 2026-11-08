using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Guacamole.SourceGenerators;

/// <summary>
/// Decides whether a provider property's value type has value equality, mirroring how the runtime
/// compares values: GUAC0004 for a type without value equality, GUAC0010 for collection members that
/// compiler-generated (record) equality compares by reference.
/// </summary>
/// <remarks>
/// <para>
/// At the top level, and inside what the runtime compares structurally (<c>ImmutableArray&lt;T&gt;</c>
/// elements, <c>ImmutableDictionary</c>/<c>ImmutableSortedDictionary</c> values, value tuples of two or
/// three components, <c>EquatableArray&lt;T&gt;</c> elements), the runtime's default comparer applies.
/// Inside a record (or a plain struct declared in source) without a user-written <c>Equals</c>,
/// members are compared with <c>EqualityComparer&lt;T&gt;.Default</c>, which compares
/// <c>ImmutableArray&lt;T&gt;</c>, arrays and the other collections by reference.
/// </para>
/// <para>
/// Only collections are reported inside records: a member of a class type without value equality
/// may deliberately be compared by identity, so it is not reported. Records and tuples are followed
/// up to <see cref="MaxDepth"/> levels; a type already on the current path is not followed again.
/// </para>
/// </remarks>
internal sealed class EqualityAnalysis
{
    private const int MaxDepth = 8;

    private readonly INamedTypeSymbol? _equatable;
    private readonly INamedTypeSymbol? _enumerable;
    private readonly INamedTypeSymbol? _immutableArray;
    private readonly INamedTypeSymbol? _immutableDictionary;
    private readonly INamedTypeSymbol? _immutableSortedDictionary;
    private readonly INamedTypeSymbol? _equatableArray;
    private readonly INamedTypeSymbol? _compilerGenerated;

    public EqualityAnalysis(Compilation compilation)
    {
        _equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");
        _enumerable = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        _immutableArray = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1");
        _immutableDictionary = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableDictionary`2");
        _immutableSortedDictionary = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableSortedDictionary`2");
        _equatableArray = compilation.GetTypeByMetadataName("Guacamole.EquatableArray`1");
        _compilerGenerated = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.CompilerGeneratedAttribute");
    }

    /// <summary>
    /// A collection member compared by reference: its path from the value type (for example
    /// <c>Module.Items</c> or <c>Module.Groups[].Names</c>) and its type.
    /// </summary>
    public readonly record struct ReferenceMember(string Path, ITypeSymbol Type);

    /// <summary>The result for one provider value type.</summary>
    /// <param name="TypeWithoutEquality">The type lacking value equality (GUAC0004), or null.</param>
    /// <param name="ReferenceMembers">Collection members compared by reference (GUAC0010).</param>
    public sealed record Result(ITypeSymbol? TypeWithoutEquality, IReadOnlyList<ReferenceMember> ReferenceMembers);

    public Result Analyze(ITypeSymbol valueType)
    {
        var walker = new Walker(this);
        walker.Visit(valueType, path: "", structural: true, topLevel: true, depth: 0);
        return new Result(walker.TypeWithoutEquality, walker.ReferenceMembers);
    }

    private sealed class Walker(EqualityAnalysis owner)
    {
        private readonly HashSet<ITypeSymbol> _path = new(SymbolEqualityComparer.Default);

        public ITypeSymbol? TypeWithoutEquality { get; private set; }

        public List<ReferenceMember> ReferenceMembers { get; } = [];

        /// <param name="type">The compared type.</param>
        /// <param name="path">The member path so far (empty at the top level).</param>
        /// <param name="structural">Whether the runtime's structural comparison applies.</param>
        /// <param name="topLevel">Whether a type without value equality is reported (GUAC0004).</param>
        /// <param name="depth">The number of records and tuples entered.</param>
        public void Visit(ITypeSymbol type, string path, bool structural, bool topLevel, int depth)
        {
            if (depth > MaxDepth)
                return;

            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                type = nullable.TypeArguments[0];

            if (type.TypeKind is TypeKind.TypeParameter or TypeKind.Enum or TypeKind.Error or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer)
                return;

            // Primitives and string have value equality; object compares by its runtime type's Equals.
            if (type.SpecialType != SpecialType.None)
                return;

            var named = type as INamedTypeSymbol;

            if (structural && named is not null && IsDefinition(named, owner._immutableArray))
            {
                Visit(named.TypeArguments[0], Element(path), structural: true, topLevel, depth);
                return;
            }

            if (structural && named is not null
                && (IsDefinition(named, owner._immutableDictionary) || IsDefinition(named, owner._immutableSortedDictionary)))
            {
                Visit(named.TypeArguments[1], Element(path), structural: true, topLevel, depth);
                return;
            }

            if (named is not null && IsDefinition(named, owner._equatableArray))
            {
                // EquatableArray<T> compares its elements with the runtime's default comparer.
                Visit(named.TypeArguments[0], Element(path), structural: true, topLevel, depth);
                return;
            }

            if (named is { IsTupleType: true })
            {
                var elements = named.TupleElements;
                var elementsStructural = structural && elements.Length <= 3;
                foreach (var element in elements)
                {
                    Visit(element.Type, Member(path, element.Name), elementsStructural, topLevel, depth + 1);
                }

                return;
            }

            if (IsReferenceEqualCollection(type))
            {
                if (topLevel && !owner.HasDeclaredEquality(type))
                    TypeWithoutEquality ??= type;
                else
                    ReferenceMembers.Add(new ReferenceMember(path.Length == 0 ? type.Name : path, type));
                return;
            }

            if (named is not null && owner.UsesMemberwiseEquality(named))
            {
                if (named.TypeKind == TypeKind.Struct && !named.IsRecord)
                {
                    // A plain struct relies on ValueType.Equals: a top-level one is GUAC0004; inside a
                    // record its fields are followed like a record's.
                    if (topLevel)
                    {
                        TypeWithoutEquality ??= type;
                        return;
                    }
                }

                if (!_path.Add(named))
                    return;

                var prefix = path.Length == 0 ? named.Name : path;
                foreach (var (name, memberType) in owner.GetComparedMembers(named))
                {
                    Visit(memberType, Member(prefix, name), structural: false, topLevel: false, depth + 1);
                }

                _path.Remove(named);
                return;
            }

            if (topLevel && !owner.HasDeclaredEquality(type))
                TypeWithoutEquality ??= type;
        }

        private bool IsReferenceEqualCollection(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Array)
                return true;

            if (owner._enumerable is null)
                return false;

            var isEnumerable = SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, owner._enumerable)
                || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, owner._enumerable));
            if (!isEnumerable)
                return false;

            // Collection interfaces: whatever implements them almost always compares by reference.
            if (type.TypeKind == TypeKind.Interface)
                return true;

            // ImmutableArray<T> implements IEquatable<ImmutableArray<T>> but compares the underlying
            // array reference; the framework's other collections do not override Equals at all.
            if (type is INamedTypeSymbol named && IsDefinition(named, owner._immutableArray))
                return true;

            if (IsInNamespace(type, "System", "Collections"))
                return true;

            // A user collection with its own Equals (or a record) is compared by that Equals.
            return !owner.HasDeclaredEquality(type) && !type.IsRecord;
        }

        private static string Element(string path) => path.Length == 0 ? path : path + "[]";

        private static string Member(string path, string name) => path.Length == 0 ? name : path + "." + name;
    }

    /// <summary>
    /// Whether the type's equality is compiler-generated and member by member: a record (class or
    /// struct) without a user-written <c>Equals(T)</c>, or a struct declared in source without
    /// <c>IEquatable&lt;T&gt;</c> or an <c>Equals(object)</c> override.
    /// </summary>
    private bool UsesMemberwiseEquality(INamedTypeSymbol type)
    {
        if (type.IsRecord)
            return !HasUserEquals(type);

        return type.TypeKind == TypeKind.Struct
            && type.DeclaringSyntaxReferences.Length > 0
            && !HasDeclaredEquality(type)
            && !type.GetMembers("Equals").Any(m => m is IMethodSymbol { IsOverride: true, Parameters.Length: 1 } method
                && method.Parameters[0].Type.SpecialType == SpecialType.System_Object);
    }

    /// <summary>
    /// Records, IEquatable&lt;T&gt; implementations, types overriding <c>Equals(object)</c>,
    /// primitives and enums have equality of their own.
    /// </summary>
    private bool HasDeclaredEquality(ITypeSymbol type)
    {
        if (type.IsRecord || type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum)
            return true;

        if (_equatable is not null && type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, _equatable) &&
                SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// A record has user-defined equality when it declares <c>Equals(T)</c> itself (the compiler
    /// then uses it instead of synthesizing one). Synthesized members are implicitly declared in
    /// source and carry [CompilerGenerated] in metadata.
    /// </summary>
    private bool HasUserEquals(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("Equals"))
        {
            if (member is IMethodSymbol { IsStatic: false, Parameters.Length: 1 } method
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), type.WithNullableAnnotation(NullableAnnotation.NotAnnotated))
                && !method.IsImplicitlyDeclared
                && !method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _compilerGenerated)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The members compiler-generated equality compares: every instance field (including the
    /// backing fields of auto-properties and positional parameters), named after its property, and
    /// those of base records that also use compiler-generated equality. For types from metadata,
    /// whose private backing fields are not visible, init/settable instance properties stand in for them.
    /// </summary>
    private IEnumerable<(string Name, ITypeSymbol Type)> GetComparedMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!ReferenceEquals(current, type) && !(current.IsRecord && !HasUserEquals(current)))
                yield break;

            var fromSource = current.DeclaringSyntaxReferences.Length > 0;
            var seen = new HashSet<string>();
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case IFieldSymbol { IsStatic: false, IsConst: false } field:
                        var name = field.AssociatedSymbol?.Name ?? field.Name;
                        if (seen.Add(name))
                            yield return (name, field.Type);
                        break;

                    case IPropertySymbol { IsStatic: false, IsIndexer: false, SetMethod: not null } property
                        when !fromSource && property.Name != "EqualityContract":
                        if (seen.Add(property.Name))
                            yield return (property.Name, property.Type);
                        break;
                }
            }

            if (current.TypeKind == TypeKind.Struct)
                yield break;
        }
    }

    private static bool IsDefinition(INamedTypeSymbol type, INamedTypeSymbol? definition)
        => definition is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition);

    private static bool IsInNamespace(ITypeSymbol type, params string[] names)
    {
        // Matches the namespace whose leading segments are names (e.g. System.Collections.*).
        var segments = new List<string>();
        for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            segments.Insert(0, ns.Name);

        if (segments.Count < names.Length)
            return false;

        for (var i = 0; i < names.Length; i++)
        {
            if (segments[i] != names[i])
                return false;
        }

        return true;
    }
}
