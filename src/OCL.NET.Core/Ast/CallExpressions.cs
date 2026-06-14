namespace OCL.NET.Core.Ast;

/// <summary>
/// Navigation / call AST nodes produced by the parser. Kept separate from the
/// literal/operator nodes in <c>OclExpression.cs</c> so each file stays focused.
/// Like all AST nodes these are pure data — evaluation lives in the interpreter.
/// </summary>

/// <summary><c>let v (: Type)? = init in body</c> — a local binding scoped to <paramref name="Body"/>.</summary>
public sealed record LetExpr(string Variable, string? Type, OclExpression Init, OclExpression Body) : OclExpression;

/// <summary><c>if cond then thenExpr else elseExpr endif</c>.</summary>
public sealed record IfExpr(OclExpression Condition, OclExpression Then, OclExpression Else) : OclExpression;

/// <summary>
/// Property navigation <c>source.name</c> (no parentheses) — e.g.
/// <c>self.identification.longName</c>, resolved via the metamodel binding.
/// </summary>
public sealed record NavigationExpr(OclExpression Source, string Name) : OclExpression;

/// <summary>Whether an operation was called with <c>.</c> (object/value op) or <c>-&gt;</c> (collection op).</summary>
public enum CallStyle { Dot, Arrow }

/// <summary>
/// An operation call, either dot-style (<c>source.oclIsKindOf(T)</c>, <c>s.size()</c>) or
/// arrow-style (<c>coll-&gt;size()</c>, <c>coll-&gt;includes(x)</c>).
/// <paramref name="Arguments"/> is empty for nullary operations. The <see cref="Style"/>
/// distinguishes e.g. <c>s.size()</c> (String length) from <c>s-&gt;size()</c> (collection
/// of one). The type operations (<c>oclIsKindOf</c>/<c>oclIsTypeOf</c>/<c>oclType</c>) carry
/// their type argument as a <see cref="VariableExpr"/> whose name is the type.
/// </summary>
public sealed record OperationCallExpr(OclExpression Source, string Name, IReadOnlyList<OclExpression> Arguments, CallStyle Style) : OclExpression;

/// <summary>
/// A collection iterator: <c>source-&gt;name(v1, v2, … | body)</c> such as
/// <c>select</c>, <c>collect</c>, <c>forAll</c>, <c>exists</c>. Supports the
/// multi-variable form (<c>forAll(c1, c2 | …)</c>) used by the catalogue's
/// uniqueness/no-duplicate rules.
/// </summary>
public sealed record IteratorExpr(OclExpression Source, string Name, IReadOnlyList<string> Variables, OclExpression Body) : OclExpression;

/// <summary>A collection literal: <c>Sequence{…}</c>, <c>Set{…}</c>, <c>Bag{…}</c>, <c>OrderedSet{…}</c>.</summary>
public sealed record CollectionLiteralExpr(string Kind, IReadOnlyList<OclExpression> Elements) : OclExpression;
