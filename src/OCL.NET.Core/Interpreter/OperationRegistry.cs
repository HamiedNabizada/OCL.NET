using OCL.NET.Core.Ast;

namespace OCL.NET.Core.Interpreter;

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
    /// Find the definition for a call. An exact context-type match wins. A
    /// non-matching receiver only falls back when the name/arity is unambiguous —
    /// i.e. exactly ONE definition exists (covers receivers whose concrete type name
    /// differs from the conceptual context). With several same-named definitions on
    /// different context types, a mismatching receiver resolves to nothing: silently
    /// picking "the first one" would defeat type-scoped operations.
    /// </summary>
    /// <summary>Built-in value kinds — never legal receivers for the conceptual-context fallback (a String is no Bounds).</summary>
    private static readonly HashSet<string> BuiltinKinds = new(StringComparer.Ordinal)
    { "String", "Integer", "Real", "Boolean", "Collection", "Void", "Invalid" };

    public OclOperationDef? Resolve(string name, int arity, string receiverType)
    {
        OclOperationDef? candidate = null;
        var candidates = 0;
        foreach (var definition in _definitions)
        {
            if (definition.Name != name || definition.Parameters.Count != arity) continue;
            if (definition.ContextType == receiverType) return definition;
            candidate = definition;
            candidates++;
        }
        // Unambiguous fallback only for object receivers whose concrete type name
        // merely differs from the conceptual context — never for built-in values
        // ('foo'.isWithin(...) must stay an unsupported-operation error).
        return candidates == 1 && !BuiltinKinds.Contains(receiverType) ? candidate : null;
    }
}
