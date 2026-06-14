using OCL.NET.Core.Ast;
using OCL.NET.Core.Values;

namespace OCL.NET.Core.Interpreter;

// Collection iterators. select/reject/collect bind a single loop variable;
// forAll/exists support the multi-variable (Cartesian) form the catalogue's
// uniqueness rules use, e.g. forAll(c1, c2 | …).
public sealed partial class OclInterpreter
{
    private OclValue EvaluateIterator(IteratorExpr expr, EvaluationEnvironment env)
    {
        // Only the quantifiers support the multi-variable (Cartesian) form; a second
        // variable on select/collect/... would silently fall into the implicit-self
        // fallback — fail loudly instead.
        if (expr.Variables.Count > 1 && expr.Name is not ("forAll" or "exists"))
            throw new NotSupportedException($"OCL iterator '{expr.Name}' takes exactly one loop variable.");

        var items = StandardLibrary.AsCollection(Evaluate(expr.Source, env));
        return expr.Name switch
        {
            "select" => Filter(expr, env, items, keepWhen: true),
            "reject" => Filter(expr, env, items, keepWhen: false),
            "collect" => Collect(expr, env, items),
            "forAll" => Quantify(expr, env, items, universal: true),
            "exists" => Quantify(expr, env, items, universal: false),
            "closure" => Closure(expr, env, items),
            "closureDepth" => OclValue.Int(ClosureDepth(expr, env, items)),
            _ => throw new NotSupportedException($"OCL iterator '{expr.Name}' is not supported in Phase 1."),
        };
    }

    private OclValue Filter(IteratorExpr expr, EvaluationEnvironment env, IReadOnlyList<OclValue> items, bool keepWhen)
    {
        var result = new List<OclValue>();
        foreach (var element in items)
        {
            var body = EvaluateBody(expr, env, element);
            if (body.Kind == OclKind.Boolean && body.AsBool() == keepWhen)
                result.Add(element);
        }
        return OclValue.Collection(result);
    }

    private OclValue Collect(IteratorExpr expr, EvaluationEnvironment env, IReadOnlyList<OclValue> items)
    {
        var result = new List<OclValue>();
        foreach (var element in items)
        {
            var mapped = EvaluateBody(expr, env, element);
            // collect flattens one level (Collection(...) → its elements) and drops
            // undefined results — consistent with implicit collect via navigation.
            if (mapped.Kind == OclKind.Collection) result.AddRange(mapped.AsCollection());
            else if (mapped.Kind != OclKind.Void) result.Add(mapped);
        }
        return OclValue.Collection(result);
    }

    /// <summary>
    /// forAll / exists with three-valued aggregation: forAll returns false as soon
    /// as one combination is false, true if all are true, and invalid if some
    /// combination was undefined but none decided the result. exists is the dual.
    /// </summary>
    private OclValue Quantify(IteratorExpr expr, EvaluationEnvironment env, IReadOnlyList<OclValue> items, bool universal)
    {
        var sawUndefined = false;
        foreach (var combination in Combinations(items, expr.Variables.Count))
        {
            var scope = env.Push();
            for (var i = 0; i < expr.Variables.Count; i++)
                scope.Bind(expr.Variables[i], combination[i]);

            var body = Evaluate(expr.Body, scope);
            if (body.Kind != OclKind.Boolean) { sawUndefined = true; continue; }

            if (universal && !body.AsBool()) return OclValue.False;
            if (!universal && body.AsBool()) return OclValue.True;
        }

        if (sawUndefined) return OclValue.Invalid;
        return OclValue.Bool(universal);
    }

    /// <summary>
    /// OCL <c>closure(v | body)</c> — the transitive closure: repeatedly apply the
    /// body to each reached element, accumulating results until a fixpoint. The
    /// source elements appear in the result only if reached again (i.e. a cycle),
    /// which is exactly what the no-circular-decomposition rule relies on. Terminates
    /// on cycles because an element is enqueued at most once after entering the result.
    /// </summary>
    private OclValue Closure(IteratorExpr expr, EvaluationEnvironment env, IReadOnlyList<OclValue> items)
    {
        var result = new List<OclValue>();
        var pending = new Queue<OclValue>(items);
        while (pending.Count > 0)
        {
            var reached = StandardLibrary.AsCollection(EvaluateBody(expr, env, pending.Dequeue()));
            foreach (var element in reached)
                if (!result.Any(x => x.ValueEquals(element)))
                {
                    result.Add(element);
                    pending.Enqueue(element);
                }
        }
        return OclValue.Collection(result);
    }

    /// <summary>
    /// <c>closureDepth(v | body)</c> (non-standard helper) — the number of body
    /// applications along the longest path before the closure saturates, i.e. the
    /// nesting depth. 0 when the body yields nothing for the source.
    /// </summary>
    private long ClosureDepth(IteratorExpr expr, EvaluationEnvironment env, IReadOnlyList<OclValue> items)
    {
        var seen = new List<OclValue>(items);
        var frontier = items;
        long depth = 0;
        while (frontier.Count > 0)
        {
            var next = new List<OclValue>();
            foreach (var element in frontier)
                foreach (var reached in StandardLibrary.AsCollection(EvaluateBody(expr, env, element)))
                    if (!seen.Any(x => x.ValueEquals(reached)))
                    {
                        seen.Add(reached);
                        next.Add(reached);
                    }
            if (next.Count > 0) depth++;
            frontier = next;
        }
        return depth;
    }

    private OclValue EvaluateBody(IteratorExpr expr, EvaluationEnvironment env, OclValue element) =>
        Evaluate(expr.Body, env.Push().Bind(expr.Variables[0], element));

    /// <summary>All ordered combinations of length <paramref name="count"/> from <paramref name="items"/> (Cartesian power).</summary>
    private static IEnumerable<OclValue[]> Combinations(IReadOnlyList<OclValue> items, int count)
    {
        if (count == 0) { yield return Array.Empty<OclValue>(); yield break; }
        foreach (var prefix in Combinations(items, count - 1))
            foreach (var element in items)
            {
                var combination = new OclValue[count];
                Array.Copy(prefix, combination, count - 1);
                combination[count - 1] = element;
                yield return combination;
            }
    }
}
