using OclNet.Core.Ast;

namespace OclNet.Core.Interpreter;

/// <summary>
/// Holds the user-defined <c>def:</c> operations available during evaluation and
/// resolves a call to one. OCL operations are scoped to a context type and may be
/// overloaded by arity, so resolution keys on <c>(name, arity, receiver type)</c>
/// rather than name alone — two same-named helpers on different context types no
/// longer collide.
/// </summary>
public sealed class OperationRegistry
{
    public static readonly OperationRegistry Empty = new(Array.Empty<OclOperationDef>());

    private readonly List<OclOperationDef> _definitions;

    public OperationRegistry(IEnumerable<OclOperationDef> definitions) => _definitions = definitions.ToList();

    public bool IsEmpty => _definitions.Count == 0;

    /// <summary>
    /// Find the definition for a call. Prefers an exact context-type match; otherwise
    /// falls back to any same-name/arity definition (covers receivers whose concrete
    /// type name differs from the conceptual context, e.g. a CAEX bounds wrapper).
    /// </summary>
    public OclOperationDef? Resolve(string name, int arity, string receiverType)
    {
        OclOperationDef? fallback = null;
        foreach (var definition in _definitions)
        {
            if (definition.Name != name || definition.Parameters.Count != arity) continue;
            if (definition.ContextType == receiverType) return definition;
            fallback ??= definition;
        }
        return fallback;
    }
}
