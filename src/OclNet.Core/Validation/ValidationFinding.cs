namespace OclNet.Core.Validation;

/// <summary>Triage level of a finding, ordered Info &lt; Warning &lt; Error.</summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One rule violation: which rule, how severe, a human-readable message, and the
/// id of the offending model element (for editor focus jumps).
/// </summary>
public sealed record ValidationFinding(
    string RuleId,
    ValidationSeverity Severity,
    string Message,
    string? TargetId = null)
{
    public override string ToString() => $"[{Severity}] {Message}";
}
