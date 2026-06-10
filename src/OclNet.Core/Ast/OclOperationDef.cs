namespace OclNet.Core.Ast;

/// <summary>A formal parameter of a user-defined OCL operation.</summary>
public sealed record OclParameter(string Name, string Type);

/// <summary>
/// A user-defined OCL operation (<c>def:</c>) — the mechanism that lets the rule
/// catalogue ship its helper operations (geometry <c>isWithin</c>, …) as plain OCL
/// rather than native code. The body evaluates with <c>self</c> bound to the
/// receiver and the parameters bound to the call arguments.
/// </summary>
public sealed record OclOperationDef(
    string ContextType,
    string Name,
    IReadOnlyList<OclParameter> Parameters,
    string ReturnType,
    OclExpression Body)
{
    public SourceLocation Location { get; init; } = SourceLocation.None;
}
