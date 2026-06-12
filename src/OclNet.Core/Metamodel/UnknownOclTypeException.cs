namespace OclNet.Core.Metamodel;

/// <summary>
/// Thrown by an <see cref="Validation.IOclModel"/> when asked to enumerate instances
/// of a type name its binding does not know at all. This is deliberately distinct
/// from an empty result: an unknown context type means the rule can never fire, and
/// the validator reports that as a diagnostic finding instead of silently passing —
/// the most expensive failure mode for a reference validator is a silent pass.
/// </summary>
public sealed class UnknownOclTypeException : Exception
{
    public string TypeName { get; }

    public UnknownOclTypeException(string typeName)
        : base($"OCL type '{typeName}' is unknown to the model binding.")
    {
        TypeName = typeName;
    }
}
