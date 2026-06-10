using OclNet.Core.Values;

namespace OclNet.Core.Ast;

/// <summary>
/// Abstract base of the OCL abstract syntax tree.
///
/// AST nodes are deliberately *pure data* — they carry no evaluation logic. All
/// behaviour lives in external visitors (today <see cref="Interpreter.OclInterpreter"/>;
/// later a type-checker / optimiser pass). Keeping the tree behaviour-free is what
/// lets the engine grow toward full OCL without rewriting the node types: a new
/// language phase is a new visitor, not a new method on every node.
/// </summary>
public abstract record OclExpression
{
    /// <summary>Position in the source OCL text; <see cref="SourceLocation.None"/> for synthetic nodes.</summary>
    public SourceLocation Location { get; init; } = SourceLocation.None;
}

/// <summary>A literal value: integer, real, string, boolean, or the void/invalid singletons.</summary>
public sealed record LiteralExpr(OclValue Value) : OclExpression;

/// <summary>
/// A reference to a bound variable — <c>self</c>, a <c>let</c> binding, or an
/// iterator variable introduced by <c>select</c>/<c>forAll</c>/… Resolved against
/// the <see cref="Interpreter.EvaluationEnvironment"/> at evaluation time.
/// </summary>
public sealed record VariableExpr(string Name) : OclExpression;

/// <summary>A unary operation: <c>not</c> or arithmetic negation.</summary>
public sealed record UnaryExpr(UnaryOperator Operator, OclExpression Operand) : OclExpression;

/// <summary>
/// A binary operation covering boolean logic, comparison, and arithmetic. Property
/// navigation and collection iterators get their own node types once the parser
/// and metamodel binding land (Milestone 1 / 3); they are intentionally absent here.
/// </summary>
public sealed record BinaryExpr(BinaryOperator Operator, OclExpression Left, OclExpression Right) : OclExpression;

public enum UnaryOperator
{
    /// <summary>Boolean negation (<c>not</c>), three-valued.</summary>
    Not,
    /// <summary>Arithmetic negation (unary <c>-</c>).</summary>
    Minus,
}

public enum BinaryOperator
{
    // Boolean (three-valued)
    And, Or, Xor, Implies,
    // Comparison / equality
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    // Arithmetic
    Add, Subtract, Multiply, Divide, Modulo,
}
