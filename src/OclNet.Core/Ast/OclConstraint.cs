namespace OclNet.Core.Ast;

/// <summary>
/// A parsed OCL invariant: <c>context T inv Name: body</c>. The top-level unit a
/// rule file decomposes into. Not an <see cref="OclExpression"/> itself — it wraps
/// the body expression together with the type it is anchored to.
/// </summary>
public sealed record OclConstraint(string ContextType, string? Name, OclExpression Body)
{
    /// <summary>Position of the <c>context</c> keyword in the source text.</summary>
    public SourceLocation Location { get; init; } = SourceLocation.None;
}
