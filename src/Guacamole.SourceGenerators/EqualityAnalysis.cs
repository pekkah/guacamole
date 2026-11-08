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
/// elements, <c>ImmutableDictionary</c>/<c>ImmutableSortedDictionary</c> values, the components of
/// value tuples of exactly two or three components, <c>EquatableArray&lt;T&gt;</c> elements), the
/// runtime's default comparer applies (<c>DefaultEqualityComparer</c>). Inside a record (or a plain
/// struct declared in source) without its own <c>Equals</c>, and in tuples of other arities, members
/// are compared with <c>EqualityComparer&lt;T&gt;.Default</c>, which compares
/// <c>ImmutableArray&lt;T&gt;</c>, arrays and the other collections by reference.
/// </para>
/// <para>
/// Only collections are reported inside records: a member of a class type without value equality
/// may deliberately be compared by identity, so it is not reported. Records and structs are followed
/// up to <see cref="MaxDepth"/> levels; a type already on the current path is not followed again, and
/// each type is walked once per analysed property (memoized), keeping the analysis linear in the
/// number of distinct types even when a type is reachable along exponentially many paths.
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

    /// <summary>At most this many reference members are collected (the message names five).</summary>
    public const int MaxCollectedMembers = 5;

    /// <summary>
    /// A collection member compared by reference: its path from the value type (for example
    /// <c>Module.Items</c> or <c>Module.Groups[].Names</c>) and its type.
    /// </summary>
    public readonly record struct ReferenceMember(string Path, ITypeSymbol Type);

    /// <summary>The result for one provider value type.</summary>
    /// <param name="TypeWithoutEquality">The type lacking value equality (GUAC0004), or null.</param>
    /// <param name="ReferenceMembers">
    /// The first <see cref="MaxCollectedMembers"/> collection members compared by reference (GUAC0010).
    /// </param>
    /// <param name="ReferenceMemberCount">The number of such members (paths) in total.</param>
    public sealed record Result(ITypeSymbol? TypeWithoutEquality, IReadOnlyList<ReferenceMember> ReferenceMembers, long ReferenceMemberCount);

    public Result Analyze(ITypeSymbol valueType)
    {
        var walker = new Walker(this);
        walker.VisitTopLevel(valueType, path: "", structural: true);
        return new Result(walker.TypeWithoutEquality, walker.ReferenceMembers, walker.ReferenceMemberCount);
    }

    /// <summary>
    /// The reference members below one type, as path suffixes relative to it (for example
    /// <c>.Items</c> or <c>[].Members</c>; empty for the type itself): the total count and the first
    /// <see cref="MaxCollectedMembers"/>.
    /// </summary>
    private sealed class Summary
    {
        public static readonly Summary Empty = new();

        public long Count { get; private set; }

        public List<ReferenceMember> First { get; } = [];

        public static Summary Single(ITypeSymbol type)
        {
            var summary = new Summary { Count = 1 };
            summary.First.Add(new ReferenceMember("", type));
            return summary;
        }

        public void Add(string prefix, Summary child)
        {
            Count += child.Count;
            foreach (var member in child.First)
            {
                if (First.Count == MaxCollectedMembers)
                    break;
                First.Add(new ReferenceMember(prefix + member.Path, member.Type));
            }
        }
    }

    /// <summary>
    /// Walks one provider value type. Below the top level, the result of each record or struct is
    /// computed once (memoized), so a type reached along many paths costs one walk, and only the first
    /// few member paths are kept: the walk is linear in the number of distinct types and members.
    /// </summary>
    private sealed class Walker(EqualityAnalysis owner)
    {
        private readonly Dictionary<ITypeSymbol, Summary> _memo = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ITypeSymbol> _inProgress = new(SymbolEqualityComparer.Default);

        public ITypeSymbol? TypeWithoutEquality { get; private set; }

        public List<ReferenceMember> ReferenceMembers { get; } = [];

        public long ReferenceMemberCount { get; private set; }

        /// <summary>
        /// Visits a value the runtime compares with its default comparer (at the top level, or inside
        /// what it compares structurally): a type without value equality is GUAC0004.
        /// </summary>
        /// <param name="type">The compared type.</param>
        /// <param name="path">The member path so far (empty at the top level).</param>
        /// <param name="structural">Whether the runtime's structural comparison applies.</param>
        public void VisitTopLevel(ITypeSymbol type, string path, bool structural)
        {
            type = Unwrap(type);
            if (IsTrivial(type))
                return;

            var named = type as INamedTypeSymbol;

            if (named is not null && TryGetStructuralElement(named, structural, out var element))
            {
                VisitTopLevel(element, Element(path), structural: true);
                return;
            }

            if (named is { IsTupleType: true })
            {
                var elementsStructural = structural && IsStructuralTuple(named);
                foreach (var tupleElement in named.TupleElements)
                {
                    VisitTopLevel(tupleElement.Type, Member(path, tupleElement.Name), elementsStructural);
                }

                return;
            }

            if (IsReferenceEqualCollection(type))
            {
                if (!owner.HasDeclaredEquality(type))
                    TypeWithoutEquality ??= type;
                else
                    AddReferenceMembers(path.Length == 0 ? type.Name : path, Summary.Single(type));
                return;
            }

            if (named is not null && owner.UsesMemberwiseEquality(named))
            {
                // A plain struct relies on ValueType.Equals: a top-level one is GUAC0004.
                if (named.TypeKind == TypeKind.Struct && !named.IsRecord)
                {
                    TypeWithoutEquality ??= type;
                    return;
                }

                AddReferenceMembers(path.Length == 0 ? named.Name : path, MembersOf(named));
                return;
            }

            if (!owner.HasDeclaredEquality(type))
                TypeWithoutEquality ??= type;
        }

        private void AddReferenceMembers(string prefix, Summary summary)
        {
            ReferenceMemberCount += summary.Count;
            foreach (var member in summary.First)
            {
                if (ReferenceMembers.Count == MaxCollectedMembers)
                    break;
                ReferenceMembers.Add(new ReferenceMember(prefix + member.Path, member.Type));
            }
        }

        /// <summary>
        /// The reference members of a value compared as a member of a record or struct (or inside
        /// what the runtime compares structurally below one). Types without value equality are not
        /// reported here: a member of such a class type may be compared by identity on purpose.
        /// </summary>
        private Summary Visit(ITypeSymbol type, bool structural)
        {
            type = Unwrap(type);
            if (IsTrivial(type))
                return Summary.Empty;

            var named = type as INamedTypeSymbol;

            if (named is not null && TryGetStructuralElement(named, structural, out var element))
            {
                var summary = new Summary();
                summary.Add("[]", Visit(element, structural: true));
                return summary;
            }

            if (named is { IsTupleType: true })
            {
                var elementsStructural = structural && IsStructuralTuple(named);
                var summary = new Summary();
                foreach (var tupleElement in named.TupleElements)
                {
                    summary.Add("." + tupleElement.Name, Visit(tupleElement.Type, elementsStructural));
                }

                return summary;
            }

            if (IsReferenceEqualCollection(type))
                return Summary.Single(type);

            if (named is not null && owner.UsesMemberwiseEquality(named))
                return MembersOf(named);

            return Summary.Empty;
        }

        /// <summary>
        /// The reference members of a record or struct with member-by-member equality, memoized.
        /// A type already being walked (a recursive type) or nested more than <see cref="MaxDepth"/>
        /// levels deep contributes nothing.
        /// </summary>
        private Summary MembersOf(INamedTypeSymbol type)
        {
            if (_memo.TryGetValue(type, out var known))
                return known;

            if (_inProgress.Count >= MaxDepth || !_inProgress.Add(type))
                return Summary.Empty;

            var summary = new Summary();
            foreach (var (name, memberType) in owner.GetComparedMembers(type))
            {
                summary.Add("." + name, Visit(memberType, structural: false));
            }

            _inProgress.Remove(type);
            _memo[type] = summary;
            return summary;
        }

        private static ITypeSymbol Unwrap(ITypeSymbol type)
            => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : type;

        /// <summary>
        /// Type parameters (unknown), enums, primitives and string (value equality), object (its
        /// runtime type's Equals) and error types are not checked.
        /// </summary>
        private static bool IsTrivial(ITypeSymbol type)
            => type.TypeKind is TypeKind.TypeParameter or TypeKind.Enum or TypeKind.Error or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer
                || type.SpecialType != SpecialType.None;

        /// <summary>
        /// The element the runtime compares when it compares <paramref name="type"/> element by
        /// element: <c>ImmutableArray&lt;T&gt;</c> elements and immutable dictionary values when
        /// structural comparison applies, and <c>EquatableArray&lt;T&gt;</c> elements always (it
        /// compares them with the runtime's default comparer).
        /// </summary>
        private bool TryGetStructuralElement(INamedTypeSymbol type, bool structural, out ITypeSymbol element)
        {
            if (structural && IsDefinition(type, owner._immutableArray))
            {
                element = type.TypeArguments[0];
                return true;
            }

            if (structural && (IsDefinition(type, owner._immutableDictionary) || IsDefinition(type, owner._immutableSortedDictionary)))
            {
                element = type.TypeArguments[1];
                return true;
            }

            if (IsDefinition(type, owner._equatableArray))
            {
                element = type.TypeArguments[0];
                return true;
            }

            element = type;
            return false;
        }

        /// <summary>
        /// The runtime compares value tuples of two or three components component by component with
        /// each component's default comparer. Other arities (one, four or more) use the tuple's own
        /// Equals, which compares the components with <c>EqualityComparer&lt;T&gt;.Default</c>.
        /// </summary>
        private static bool IsStructuralTuple(INamedTypeSymbol tuple) => tuple.TupleElements.Length is 2 or 3;

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
            return !owner.HasDeclaredEquality(type);
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
            && !HasDeclaredEquality(type);
    }

    /// <summary>
    /// Records, IEquatable&lt;T&gt; implementations, types overriding <c>Equals(object)</c> (itself or
    /// in a base class; <c>EqualityComparer&lt;T&gt;.Default</c> uses that override), primitives and
    /// enums have equality of their own.
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

        return OverridesObjectEquals(type);
    }

    private static bool OverridesObjectEquals(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
                return false;

            if (current.GetMembers("Equals").Any(m => m is IMethodSymbol { IsOverride: true, IsStatic: false, Parameters.Length: 1 } method
                    && method.Parameters[0].Type.SpecialType == SpecialType.System_Object))
            {
                return true;
            }
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
