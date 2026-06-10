using OclNet.Core.Ast;
using OclNet.Core.Values;

namespace OclNet.Core.Interpreter;

// Navigation, operation calls (incl. the type operations), and the simple
// control-flow nodes. Split from the operator core to keep each file focused.
public sealed partial class OclInterpreter
{
    private static readonly HashSet<string> TypeOperations = new() { "oclIsKindOf", "oclIsTypeOf", "oclType" };

    /// <summary>
    /// Property navigation <c>source.name</c>. Over a single object it delegates to
    /// the metamodel; over a collection it implicitly collects (OCL flattens one
    /// level: <c>coll.attr ≡ coll-&gt;collect(attr)</c>); over <c>OclVoid</c> it stays void.
    /// </summary>
    private OclValue EvaluateNavigation(NavigationExpr expr, EvaluationEnvironment env) =>
        Navigate(Evaluate(expr.Source, env), expr.Name, env);

    private OclValue Navigate(OclValue source, string name, EvaluationEnvironment env)
    {
        switch (source.Kind)
        {
            case OclKind.Object:
                return env.Metamodel?.GetProperty(source.AsObject(), name) ?? OclValue.Invalid;
            case OclKind.Void:
                return OclValue.Void;
            case OclKind.Collection:
                var flattened = new List<OclValue>();
                foreach (var element in source.AsCollection())
                {
                    var navigated = Navigate(element, name, env);
                    if (navigated.Kind == OclKind.Collection) flattened.AddRange(navigated.AsCollection());
                    else if (navigated.Kind != OclKind.Void) flattened.Add(navigated);
                }
                return OclValue.Collection(flattened);
            default:
                return OclValue.Invalid;
        }
    }

    private OclValue EvaluateOperationCall(OperationCallExpr expr, EvaluationEnvironment env)
    {
        if (TypeOperations.Contains(expr.Name)) return EvaluateTypeOperation(expr, env);

        var source = Evaluate(expr.Source, env);
        if (source.Kind == OclKind.Invalid) return OclValue.Invalid;

        var arguments = expr.Arguments.Select(a => Evaluate(a, env)).ToList();

        var result = StandardLibrary.Invoke(expr.Name, source, arguments, expr.Style);
        if (result is not null) return result;

        // Fall back to a user-defined operation (OCL `def:`), e.g. geometry helpers.
        var definition = env.Operations?.Resolve(expr.Name, arguments.Count, ReceiverType(source, env));
        if (definition is not null)
            return InvokeDefinition(definition, source, arguments, env);

        throw new NotSupportedException($"OCL operation '{expr.Name}' is not supported in Phase 1.");
    }

    private static string ReceiverType(OclValue source, EvaluationEnvironment env) =>
        source.Kind == OclKind.Object && env.Metamodel is not null
            ? env.Metamodel.TypeOf(source.AsObject())
            : source.Kind.ToString();

    /// <summary>Evaluate a <c>def:</c> body in a fresh scope with <c>self</c> = receiver and the parameters bound to the arguments.</summary>
    private OclValue InvokeDefinition(OclOperationDef definition, OclValue receiver, IReadOnlyList<OclValue> arguments, EvaluationEnvironment env)
    {
        var scope = env.NewRoot().Bind("self", receiver);
        for (var i = 0; i < definition.Parameters.Count && i < arguments.Count; i++)
            scope.Bind(definition.Parameters[i].Name, arguments[i]);
        return Evaluate(definition.Body, scope);
    }

    /// <summary>
    /// The type operations read their argument as a type *name*, not a value:
    /// <c>oclIsKindOf(FPD_State)</c> never evaluates <c>FPD_State</c>. <c>oclType()</c>
    /// is represented as the type-name string so the catalogue's
    /// <c>a.oclType() = b.oclType()</c> comparisons work.
    /// </summary>
    private OclValue EvaluateTypeOperation(OperationCallExpr expr, EvaluationEnvironment env)
    {
        var source = Evaluate(expr.Source, env);
        if (source.Kind != OclKind.Object || env.Metamodel is null) return OclValue.Invalid;
        var element = source.AsObject();

        if (expr.Name == "oclType")
            return OclValue.Str(env.Metamodel.TypeOf(element));

        if (expr.Arguments.FirstOrDefault() is not VariableExpr typeRef)
            throw new NotSupportedException($"{expr.Name} expects a type-name argument.");

        var isKind = expr.Name == "oclIsKindOf"
            ? env.Metamodel.IsKindOf(element, typeRef.Name)
            : env.Metamodel.IsTypeOf(element, typeRef.Name);
        return OclValue.Bool(isKind);
    }

    private OclValue EvaluateLet(LetExpr expr, EvaluationEnvironment env)
    {
        var bound = Evaluate(expr.Init, env);
        return Evaluate(expr.Body, env.Push().Bind(expr.Variable, bound));
    }

    private OclValue EvaluateIf(IfExpr expr, EvaluationEnvironment env)
    {
        var condition = Evaluate(expr.Condition, env);
        if (condition.Kind != OclKind.Boolean) return OclValue.Invalid;
        return Evaluate(condition.AsBool() ? expr.Then : expr.Else, env);
    }

    private OclValue EvaluateCollectionLiteral(CollectionLiteralExpr expr, EvaluationEnvironment env) =>
        OclValue.Collection(expr.Elements.Select(e => Evaluate(e, env)).ToList());
}
