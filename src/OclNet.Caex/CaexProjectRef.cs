using Aml.Engine.CAEX;

namespace OclNet.Caex;

/// <summary>
/// Represents the OCL <c>project</c> scope for a CAEX document — the aggregate of
/// all elements across every InstanceHierarchy. Reached via <c>self.project</c>;
/// <c>self.project.containedElement</c> then yields every InternalElement in the
/// document (the catalogue's project-wide aggregation, e.g. for reference-resolution
/// rules). Identity is the document itself.
/// </summary>
public sealed class CaexProjectRef
{
    public CAEXDocument Document { get; }

    public CaexProjectRef(CAEXDocument document) => Document = document;

    public override bool Equals(object? obj) => obj is CaexProjectRef other && ReferenceEquals(Document, other.Document);

    public override int GetHashCode() => Document.GetHashCode();
}
