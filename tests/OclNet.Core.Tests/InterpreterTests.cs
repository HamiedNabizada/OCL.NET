using OclNet.Core.Ast;
using OclNet.Core.Interpreter;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Core.Tests;

/// <summary>
/// Foundation slice: the interpreter over literals, variables, arithmetic,
/// comparison, and three-valued boolean logic — exercised by constructing the AST
/// directly (the parser arrives in Milestone 1). Locks in OCL value semantics
/// before any concrete-syntax or metamodel binding sits on top.
/// </summary>
public class InterpreterTests
{
    private readonly OclInterpreter _interp = new();

    private OclValue Eval(OclExpression expr, EvaluationEnvironment? env = null)
        => _interp.Evaluate(expr, env ?? new EvaluationEnvironment());

    private static LiteralExpr Lit(long i) => new(OclValue.Int(i));
    private static LiteralExpr Lit(double d) => new(OclValue.Real(d));
    private static LiteralExpr Lit(bool b) => new(OclValue.Bool(b));
    private static BinaryExpr Bin(BinaryOperator op, OclExpression l, OclExpression r) => new(op, l, r);

    // ---- literals & variables ------------------------------------------------------

    [Fact]
    public void Literal_evaluates_to_itself()
    {
        Assert.Equal(42, Eval(Lit(42)).AsInt());
    }

    [Fact]
    public void Variable_resolves_from_environment()
    {
        var env = new EvaluationEnvironment().Bind("self", OclValue.Int(7));
        Assert.Equal(7, Eval(new VariableExpr("self"), env).AsInt());
    }

    [Fact]
    public void Unbound_variable_is_invalid()
    {
        Assert.Equal(OclKind.Invalid, Eval(new VariableExpr("nope")).Kind);
    }

    // ---- arithmetic ----------------------------------------------------------------

    [Fact]
    public void Integer_addition_stays_integer()
    {
        var r = Eval(Bin(BinaryOperator.Add, Lit(2), Lit(3)));
        Assert.Equal(OclKind.Integer, r.Kind);
        Assert.Equal(5, r.AsInt());
    }

    [Fact]
    public void Division_always_yields_real()
    {
        var r = Eval(Bin(BinaryOperator.Divide, Lit(6), Lit(4)));
        Assert.Equal(OclKind.Real, r.Kind);
        Assert.Equal(1.5, r.AsReal());
    }

    [Fact]
    public void Division_by_zero_is_invalid()
    {
        Assert.Equal(OclKind.Invalid, Eval(Bin(BinaryOperator.Divide, Lit(1), Lit(0))).Kind);
    }

    [Fact]
    public void Modulo_of_integers()
    {
        Assert.Equal(1, Eval(Bin(BinaryOperator.Modulo, Lit(7), Lit(3))).AsInt());
    }

    // ---- comparison & equality -----------------------------------------------------

    [Fact]
    public void Less_than_compares_numerically()
    {
        Assert.True(Eval(Bin(BinaryOperator.Less, Lit(2), Lit(3))).AsBool());
    }

    [Fact]
    public void Equality_compares_integer_and_real_by_value()
    {
        Assert.True(Eval(Bin(BinaryOperator.Equal, Lit(2L), Lit(2.0))).AsBool());
    }

    [Fact]
    public void Inequality_of_strings()
    {
        var ne = Bin(BinaryOperator.NotEqual, new LiteralExpr(OclValue.Str("a")), new LiteralExpr(OclValue.Str("b")));
        Assert.True(Eval(ne).AsBool());
    }

    // ---- three-valued boolean logic ------------------------------------------------

    [Fact]
    public void False_and_anything_is_false_even_if_right_is_invalid()
    {
        // false and (unbound) = false — the right operand never decides the result.
        var r = Eval(Bin(BinaryOperator.And, Lit(false), new VariableExpr("unbound")));
        Assert.Equal(OclKind.Boolean, r.Kind);
        Assert.False(r.AsBool());
    }

    [Fact]
    public void True_or_anything_is_true_even_if_right_is_invalid()
    {
        var r = Eval(Bin(BinaryOperator.Or, Lit(true), new VariableExpr("unbound")));
        Assert.True(r.AsBool());
    }

    [Fact]
    public void False_implies_anything_is_true()
    {
        var r = Eval(Bin(BinaryOperator.Implies, Lit(false), new VariableExpr("unbound")));
        Assert.True(r.AsBool());
    }

    [Fact]
    public void True_implies_false_is_false()
    {
        Assert.False(Eval(Bin(BinaryOperator.Implies, Lit(true), Lit(false))).AsBool());
    }

    [Fact]
    public void And_with_invalid_operand_and_no_decision_is_invalid()
    {
        // (unbound) and true — neither operand forces false, so result is invalid.
        var r = Eval(Bin(BinaryOperator.And, new VariableExpr("unbound"), Lit(true)));
        Assert.Equal(OclKind.Invalid, r.Kind);
    }

    [Fact]
    public void Not_of_true_is_false()
    {
        Assert.False(Eval(new UnaryExpr(UnaryOperator.Not, Lit(true))).AsBool());
    }

    [Fact]
    public void Xor_of_true_and_false_is_true()
    {
        Assert.True(Eval(Bin(BinaryOperator.Xor, Lit(true), Lit(false))).AsBool());
    }
}
