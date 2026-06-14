namespace OCL.NET.Core.Validation;

/// <summary>
/// A validation rule: an OCL invariant plus the metadata the engine attaches to
/// any finding it produces. The <see cref="OclConstraint"/> text carries its own
/// <c>context</c> type and invariant name; <see cref="Id"/>/<see cref="Severity"/>/
/// <see cref="Source"/> are the catalogue bookkeeping around it.
/// </summary>
public sealed record OclRuleSpec(
    string Id,
    ValidationSeverity Severity,
    string Source,
    string OclConstraint);
