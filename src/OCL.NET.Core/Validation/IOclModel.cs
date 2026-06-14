using OCL.NET.Core.Metamodel;
using OCL.NET.Core.Values;

namespace OCL.NET.Core.Validation;

/// <summary>
/// A validatable model: an <see cref="IOclMetamodel"/> (type identity + property
/// navigation) extended with the two things the validator needs beyond evaluating
/// a single expression — enumerating the instances a <c>context</c> applies to, and
/// identifying an instance for a finding.
///
/// Splitting this from <see cref="IOclMetamodel"/> keeps the pure expression
/// evaluator independent of how a model lists its elements.
/// </summary>
public interface IOclModel : IOclMetamodel
{
    /// <summary>All instances a <c>context TypeName</c> invariant must hold for — i.e. every element that <c>oclIsKindOf(TypeName)</c>.</summary>
    IEnumerable<OclValue> InstancesOf(string typeName);

    /// <summary>A stable identifier for <paramref name="instance"/> (e.g. an AML ID) for use as a finding's target; null if unavailable.</summary>
    string? IdOf(OclValue instance);
}
