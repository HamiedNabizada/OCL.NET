namespace OclNet.Core.Ast;

/// <summary>
/// 1-based position of an AST node in the originating OCL source text. Carried
/// on every <see cref="OclExpression"/> so parse/runtime diagnostics and
/// validation findings can point the user back at the exact constraint location.
/// </summary>
public readonly record struct SourceLocation(int Line, int Column)
{
    /// <summary>Sentinel for synthetic nodes that were not produced by the parser.</summary>
    public static readonly SourceLocation None = new(0, 0);

    public override string ToString() => this == None ? "<synthetic>" : $"{Line}:{Column}";
}
