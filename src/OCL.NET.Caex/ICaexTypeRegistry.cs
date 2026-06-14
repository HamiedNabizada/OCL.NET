using Aml.Engine.CAEX;

namespace OCL.NET.Caex;

/// <summary>
/// The domain seam of the CAEX binding. Everything a concrete domain library
/// (e.g. VDI 3682 / FPD) defines — type identity, the type vocabulary, and the
/// domain's serialization conventions — flows through this interface, so that
/// <see cref="CaexMetamodel"/> stays free of domain knowledge. Swapping the
/// registry retargets the binding to a different CAEX domain library without
/// touching the navigation code.
///
/// Implementations receive the raw Aml.Engine object (already unwrapped from any
/// identity wrapper).
/// </summary>
public interface ICaexTypeRegistry
{
    /// <summary><c>e.oclIsKindOf(T)</c> — <paramref name="caexObject"/> is of <paramref name="oclTypeName"/> or a subtype.</summary>
    bool IsKindOf(object caexObject, string oclTypeName);

    /// <summary><c>e.oclIsTypeOf(T)</c> — <paramref name="caexObject"/> is exactly <paramref name="oclTypeName"/>.</summary>
    bool IsTypeOf(object caexObject, string oclTypeName);

    /// <summary><c>e.oclType()</c> — the most specific OCL type name, or empty if the object is outside this domain.</summary>
    string TypeOf(object caexObject);

    /// <summary>
    /// Whether <paramref name="oclTypeName"/> is part of this domain's type vocabulary
    /// at all. Lets the validator distinguish "type has no instances in this model"
    /// (a legitimate empty set) from "type is unknown to the binding" (a rule that can
    /// never fire — reported as a diagnostic instead of silently passing).
    /// </summary>
    bool KnowsType(string oclTypeName);

    /// <summary>The OCL type name of the model root / project scope (e.g. <c>FPD_Project</c>).</summary>
    string ProjectTypeName { get; }

    /// <summary>The OCL type name of the process container that scopes connections and elements (e.g. <c>FPD_Process</c>).</summary>
    string ProcessTypeName { get; }

    /// <summary>The OCL type name of the flat geometry box exposed via <c>self.bounds</c> (e.g. <c>Bounds</c>).</summary>
    string BoundsTypeName { get; }

    /// <summary>Whether <paramref name="attribute"/> is this domain's diagram-interchange bounds attribute (located for <c>self.bounds</c>).</summary>
    bool IsBoundsAttribute(AttributeType attribute);
}
