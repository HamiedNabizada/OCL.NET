namespace OclNet.Caex;

/// <summary>
/// Resolves OCL type identity for CAEX objects — InternalElements, InternalLinks
/// (connections), and ExternalInterfaces. This is the one domain-specific seam of
/// the CAEX binding: the generic <see cref="CaexMetamodel"/> handles navigation,
/// but *what counts as an <c>FPD_State</c> or an <c>FPD_Flow</c>* is knowledge a
/// registry supplies. Swapping the registry retargets the binding to a different
/// CAEX domain library without touching the navigation code.
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
}
