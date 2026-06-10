using OclNet.Core.Metamodel;
using OclNet.Core.Values;

namespace OclNet.Core.Interpreter;

/// <summary>
/// The lexical scope an expression evaluates in: variable bindings plus the
/// metamodel binding used to resolve types and navigate properties.
///
/// Scopes nest. <c>let</c> bindings and iterator variables (<c>select</c>,
/// <c>forAll</c>, …) push a child scope via <see cref="Push"/> so a binding is
/// visible to inner expressions but disappears when the iterator moves on.
/// Lookup walks the parent chain.
/// </summary>
public sealed class EvaluationEnvironment
{
    private readonly EvaluationEnvironment? _parent;
    private readonly Dictionary<string, OclValue> _bindings;

    /// <summary>The metamodel binding for type checks and property navigation. Null until a binding is supplied (pure-expression evaluation).</summary>
    public IOclMetamodel? Metamodel { get; }

    /// <summary>User-defined <c>def:</c> operations available to calls. Null if none were supplied.</summary>
    public OperationRegistry? Operations { get; }

    public EvaluationEnvironment(IOclMetamodel? metamodel = null, OperationRegistry? operations = null)
    {
        _parent = null;
        _bindings = new Dictionary<string, OclValue>(StringComparer.Ordinal);
        Metamodel = metamodel;
        Operations = operations;
    }

    private EvaluationEnvironment(EvaluationEnvironment parent)
    {
        _parent = parent;
        _bindings = new Dictionary<string, OclValue>(StringComparer.Ordinal);
        Metamodel = parent.Metamodel;
        Operations = parent.Operations;
    }

    /// <summary>A fresh root scope sharing this environment's metamodel and operations — used when entering a <c>def:</c> body.</summary>
    public EvaluationEnvironment NewRoot() => new(Metamodel, Operations);

    /// <summary>Bind a variable in the current scope (e.g. <c>self</c>, a let name, an iterator var).</summary>
    public EvaluationEnvironment Bind(string name, OclValue value)
    {
        _bindings[name] = value;
        return this;
    }

    /// <summary>Create a child scope for a nested binding (let body, iterator iteration).</summary>
    public EvaluationEnvironment Push() => new(this);

    /// <summary>Resolve a variable up the parent chain. Returns false if unbound.</summary>
    public bool TryResolve(string name, out OclValue value)
    {
        for (var env = this; env is not null; env = env._parent)
            if (env._bindings.TryGetValue(name, out value!))
                return true;
        value = OclValue.Invalid;
        return false;
    }
}
