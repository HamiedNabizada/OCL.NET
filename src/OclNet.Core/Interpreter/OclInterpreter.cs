using OclNet.Core.Ast;
using OclNet.Core.Values;

namespace OclNet.Core.Interpreter;

/// <summary>
/// Tree-walking evaluator over the OCL AST. A second visitor — distinct from the
/// AST itself, so further language phases (type-checking, optimisation) slot in
/// alongside rather than bloating the node types.
///
/// Boolean operators implement OCL's three-valued logic (true / false / invalid):
/// e.g. <c>false and invalid = false</c>, because the result is determined
/// regardless of the undefined operand. Arithmetic or comparison touching an
/// undefined operand yields <see cref="OclValue.Invalid"/>, which is how an
/// invariant "fails safe" when a navigation hit a null.
/// </summary>
public sealed partial class OclInterpreter
{
    public OclValue Evaluate(OclExpression expression, EvaluationEnvironment environment) => expression switch
    {
        LiteralExpr lit => lit.Value,
        VariableExpr var => EvaluateVariable(var, environment),
        UnaryExpr un => EvaluateUnary(un, environment),
        BinaryExpr bin => EvaluateBinary(bin, environment),
        NavigationExpr nav => EvaluateNavigation(nav, environment),
        OperationCallExpr op => EvaluateOperationCall(op, environment),
        IteratorExpr it => EvaluateIterator(it, environment),
        LetExpr let => EvaluateLet(let, environment),
        IfExpr iff => EvaluateIf(iff, environment),
        CollectionLiteralExpr coll => EvaluateCollectionLiteral(coll, environment),
        _ => throw new NotSupportedException($"No evaluation rule for AST node {expression.GetType().Name}."),
    };

    /// <summary>
    /// Resolve a name: a bound variable first, otherwise OCL's implicit-self rule —
    /// an unqualified name in a constraint body is a property of <c>self</c>
    /// (so <c>source</c> in <c>context Flow</c> means <c>self.source</c>).
    /// </summary>
    private static OclValue EvaluateVariable(VariableExpr expr, EvaluationEnvironment env)
    {
        if (env.TryResolve(expr.Name, out var value)) return value;
        if (env.Metamodel is not null && env.TryResolve("self", out var self) && self.Kind == OclKind.Object)
            return env.Metamodel.GetProperty(self.AsObject(), expr.Name);
        return OclValue.Invalid;
    }

    private OclValue EvaluateUnary(UnaryExpr expr, EvaluationEnvironment env)
    {
        var operand = Evaluate(expr.Operand, env);
        return expr.Operator switch
        {
            UnaryOperator.Not => operand.Kind == OclKind.Boolean ? OclValue.Bool(!operand.AsBool()) : OclValue.Invalid,
            UnaryOperator.Minus => operand.Kind switch
            {
                OclKind.Integer => OclValue.Int(-operand.AsInt()),
                OclKind.Real => OclValue.Real(-operand.AsReal()),
                _ => OclValue.Invalid,
            },
            _ => OclValue.Invalid,
        };
    }

    private OclValue EvaluateBinary(BinaryExpr expr, EvaluationEnvironment env)
    {
        // Boolean operators short-circuit and follow three-valued logic, so they
        // must inspect operands lazily rather than eagerly evaluating both.
        switch (expr.Operator)
        {
            case BinaryOperator.And: return EvaluateAnd(expr, env);
            case BinaryOperator.Or: return EvaluateOr(expr, env);
            case BinaryOperator.Implies: return EvaluateImplies(expr, env);
            case BinaryOperator.Xor: return EvaluateXor(expr, env);
        }

        var left = Evaluate(expr.Left, env);
        var right = Evaluate(expr.Right, env);

        return expr.Operator switch
        {
            BinaryOperator.Equal => Equality(left, right, negate: false),
            BinaryOperator.NotEqual => Equality(left, right, negate: true),
            BinaryOperator.Less or BinaryOperator.LessOrEqual or
            BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => Compare(expr.Operator, left, right),
            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or
            BinaryOperator.Divide or BinaryOperator.Modulo => Arithmetic(expr.Operator, left, right),
            _ => OclValue.Invalid,
        };
    }

    // ---- three-valued boolean logic ------------------------------------------------

    private OclValue EvaluateAnd(BinaryExpr expr, EvaluationEnvironment env)
    {
        var left = Evaluate(expr.Left, env);
        if (left.Kind == OclKind.Boolean && !left.AsBool()) return OclValue.False; // false and _ = false
        var right = Evaluate(expr.Right, env);
        if (right.Kind == OclKind.Boolean && !right.AsBool()) return OclValue.False;
        if (left.Kind == OclKind.Boolean && right.Kind == OclKind.Boolean) return OclValue.True;
        return OclValue.Invalid;
    }

    private OclValue EvaluateOr(BinaryExpr expr, EvaluationEnvironment env)
    {
        var left = Evaluate(expr.Left, env);
        if (left.Kind == OclKind.Boolean && left.AsBool()) return OclValue.True; // true or _ = true
        var right = Evaluate(expr.Right, env);
        if (right.Kind == OclKind.Boolean && right.AsBool()) return OclValue.True;
        if (left.Kind == OclKind.Boolean && right.Kind == OclKind.Boolean) return OclValue.False;
        return OclValue.Invalid;
    }

    private OclValue EvaluateImplies(BinaryExpr expr, EvaluationEnvironment env)
    {
        var left = Evaluate(expr.Left, env);
        if (left.Kind == OclKind.Boolean && !left.AsBool()) return OclValue.True; // false implies _ = true
        var right = Evaluate(expr.Right, env);
        if (right.Kind == OclKind.Boolean && right.AsBool()) return OclValue.True; // _ implies true = true
        if (left.Kind == OclKind.Boolean && right.Kind == OclKind.Boolean) return OclValue.False; // true implies false
        return OclValue.Invalid;
    }

    private OclValue EvaluateXor(BinaryExpr expr, EvaluationEnvironment env)
    {
        var left = Evaluate(expr.Left, env);
        var right = Evaluate(expr.Right, env);
        if (left.Kind != OclKind.Boolean || right.Kind != OclKind.Boolean) return OclValue.Invalid;
        return OclValue.Bool(left.AsBool() ^ right.AsBool());
    }

    /// <summary>OCL <c>=</c>/<c>&lt;&gt;</c>: any operand being <c>invalid</c> makes the result invalid; <c>null = null</c> is true.</summary>
    private static OclValue Equality(OclValue left, OclValue right, bool negate)
    {
        if (left.Kind == OclKind.Invalid || right.Kind == OclKind.Invalid) return OclValue.Invalid;
        var equal = left.ValueEquals(right);
        return OclValue.Bool(negate ? !equal : equal);
    }

    // ---- comparison & arithmetic ---------------------------------------------------

    private static OclValue Compare(BinaryOperator op, OclValue left, OclValue right)
    {
        if (!left.IsNumeric || !right.IsNumeric) return OclValue.Invalid;
        var cmp = left.AsReal().CompareTo(right.AsReal());
        return OclValue.Bool(op switch
        {
            BinaryOperator.Less => cmp < 0,
            BinaryOperator.LessOrEqual => cmp <= 0,
            BinaryOperator.Greater => cmp > 0,
            BinaryOperator.GreaterOrEqual => cmp >= 0,
            _ => false,
        });
    }

    private static OclValue Arithmetic(BinaryOperator op, OclValue left, OclValue right)
    {
        if (!left.IsNumeric || !right.IsNumeric) return OclValue.Invalid;

        // OCL '/' always yields Real; 'mod' is integer-only. '+' '-' '*' stay
        // Integer when both operands are Integer, otherwise widen to Real.
        var bothInt = left.Kind == OclKind.Integer && right.Kind == OclKind.Integer;

        switch (op)
        {
            case BinaryOperator.Divide:
                var divisor = right.AsReal();
                return divisor == 0 ? OclValue.Invalid : OclValue.Real(left.AsReal() / divisor);

            case BinaryOperator.Modulo:
                if (!bothInt) return OclValue.Invalid;
                var m = right.AsInt();
                return m == 0 ? OclValue.Invalid : OclValue.Int(left.AsInt() % m);

            case BinaryOperator.Add:
                return bothInt ? OclValue.Int(left.AsInt() + right.AsInt()) : OclValue.Real(left.AsReal() + right.AsReal());
            case BinaryOperator.Subtract:
                return bothInt ? OclValue.Int(left.AsInt() - right.AsInt()) : OclValue.Real(left.AsReal() - right.AsReal());
            case BinaryOperator.Multiply:
                return bothInt ? OclValue.Int(left.AsInt() * right.AsInt()) : OclValue.Real(left.AsReal() * right.AsReal());

            default:
                return OclValue.Invalid;
        }
    }
}
