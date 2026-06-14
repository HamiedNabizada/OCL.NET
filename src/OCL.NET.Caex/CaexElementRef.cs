using Aml.Engine.CAEX;

namespace OCL.NET.Caex;

/// <summary>
/// Stable-identity wrapper around an Aml.Engine <see cref="InternalElementType"/>.
///
/// Aml.Engine hands out fresh, non-reference-stable wrapper instances for the same
/// underlying element, so OCL identity comparisons (<c>e1 &lt;&gt; e2</c>,
/// <c>source = target</c>) must key on the AML <c>ID</c>, not object identity. This
/// wrapper provides exactly that, so an <see cref="OCL.NET.Core.Values.OclValue"/>
/// holding a CAEX element compares correctly.
/// </summary>
public sealed class CaexElementRef
{
    public InternalElementType Element { get; }

    public CaexElementRef(InternalElementType element) => Element = element;

    public override bool Equals(object? obj)
    {
        if (obj is not CaexElementRef other) return false;
        return Element.ID is { Length: > 0 } id
            ? id == other.Element.ID
            : ReferenceEquals(Element, other.Element);
    }

    public override int GetHashCode() => Element.ID?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => Element.Name ?? Element.ID ?? "<element>";
}
