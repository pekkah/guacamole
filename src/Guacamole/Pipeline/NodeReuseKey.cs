namespace Guacamole.Core.Pipeline;

/// <summary>
/// Structural identity of a pipeline operator call, used to reuse the node created by
/// an earlier, identical call (for example a getter-style property such as
/// <c>Items.Select(x =&gt; x * 2)</c> that is evaluated on every access).
/// </summary>
/// <remarks>
/// Two calls are identical when they create the same closed node type, over the same
/// ordered input node IDs, with equal arguments. Delegates are compared with
/// <see cref="Delegate.Equals(object?)"/> (same method and same target), so
/// non-capturing lambdas and lambdas capturing only <c>this</c> match across calls,
/// while lambdas capturing locals allocate a new closure per call and never match.
/// Comparers and other arguments are compared with <see cref="object.Equals(object?, object?)"/>.
/// </remarks>
internal sealed class NodeReuseKey : IEquatable<NodeReuseKey>
{
    private readonly Type _nodeType;
    private readonly string[] _inputIds;
    private readonly object?[] _arguments;
    private readonly int _hashCode;

    public NodeReuseKey(Type nodeType, string[] inputIds, params object?[] arguments)
    {
        _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        _inputIds = inputIds ?? throw new ArgumentNullException(nameof(inputIds));
        _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));

        var hash = new HashCode();
        hash.Add(_nodeType);
        foreach (var id in _inputIds)
        {
            hash.Add(id, StringComparer.Ordinal);
        }

        foreach (var argument in _arguments)
        {
            hash.Add(argument);
        }

        _hashCode = hash.ToHashCode();
    }

    public bool Equals(NodeReuseKey? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (_hashCode != other._hashCode
            || _nodeType != other._nodeType
            || _inputIds.Length != other._inputIds.Length
            || _arguments.Length != other._arguments.Length)
        {
            return false;
        }

        for (var i = 0; i < _inputIds.Length; i++)
        {
            if (!string.Equals(_inputIds[i], other._inputIds[i], StringComparison.Ordinal))
                return false;
        }

        for (var i = 0; i < _arguments.Length; i++)
        {
            if (!Equals(_arguments[i], other._arguments[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as NodeReuseKey);

    public override int GetHashCode() => _hashCode;
}
