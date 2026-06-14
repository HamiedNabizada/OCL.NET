using Aml.Engine.CAEX;

namespace OCL.NET.Caex;

/// <summary>
/// Stable-identity wrapper around an Aml.Engine <see cref="InternalLinkType"/> (an
/// FPD connection). Identity keys on the two partner-side interface IDs
/// (<c>RefPartnerSideA</c>/<c>RefPartnerSideB</c>) — which uniquely pin a link's
/// endpoints and, unlike <c>Name</c>, are always present. This makes OCL comparisons
/// such as <c>c1 &lt;&gt; c2</c> in the no-duplicate-connections rule behave correctly
/// despite non-reference-stable Aml.Engine wrappers and unnamed links.
/// </summary>
public sealed class CaexLinkRef
{
    public InternalLinkType Link { get; }

    public CaexLinkRef(InternalLinkType link) => Link = link;

    private string? Key =>
        Link.RefPartnerSideA is { Length: > 0 } a && Link.RefPartnerSideB is { Length: > 0 } b
            ? a + "|" + b
            : null;

    public override bool Equals(object? obj)
    {
        if (obj is not CaexLinkRef other) return false;
        return Key is { } key ? key == other.Key : ReferenceEquals(Link, other.Link);
    }

    public override int GetHashCode() => Key?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => Link.Name ?? Key ?? "<link>";
}
