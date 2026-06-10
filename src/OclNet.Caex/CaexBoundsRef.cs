using Aml.Engine.CAEX;

namespace OclNet.Caex;

/// <summary>
/// Bridges the catalogue's <em>conceptual flat</em> Bounds (with <c>x</c>, <c>y</c>,
/// <c>width</c>, <c>height</c>) onto the FPD diagram-interchange serialization, where
/// an element's bounds live in a <c>ViewInformation</c> compound attribute and the
/// coordinates sit one level deeper under <c>position</c>
/// (<c>ViewInformation/position/x</c>). Wrapping the ViewInformation attribute in this
/// ref lets the published geometry helpers (<c>self.x</c>, …) run unchanged against
/// real AML — the flat↔nested gap is absorbed here in the binding, not in the OCL.
/// </summary>
public sealed class CaexBoundsRef
{
    public AttributeType ViewInformation { get; }

    public CaexBoundsRef(AttributeType viewInformation) => ViewInformation = viewInformation;
}
