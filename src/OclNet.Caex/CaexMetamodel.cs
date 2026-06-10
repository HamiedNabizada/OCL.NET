using System.Globalization;
using Aml.Engine.CAEX;
using OclNet.Core.Metamodel;
using OclNet.Core.Validation;
using OclNet.Core.Values;

namespace OclNet.Caex;

/// <summary>
/// <see cref="IOclModel"/> over CAEX / Aml.Engine. Bridges OCL navigation and type
/// tests onto InternalElements, their attribute hierarchy, and InternalLinks
/// (connections):
/// <list type="bullet">
///   <item><c>self.containedElement</c> → child InternalElements; <c>self.connections</c> → the process's links;</item>
///   <item><c>self.identification.longName</c> → the Identification compound attribute and its sub-attribute value;</item>
///   <item>on a connection: <c>source</c>/<c>target</c> resolve through the link's A-/B-interface to the endpoint IEs, <c>sourceInterface</c>/<c>targetInterface</c> to the interfaces themselves;</item>
///   <item><c>e.oclIsKindOf(FPD_State)</c> / <c>oclIsKindOf(FPD_Flow)</c> → delegated to the <see cref="ICaexTypeRegistry"/>.</item>
/// </list>
/// Elements and links are exposed through identity wrappers
/// (<see cref="CaexElementRef"/>/<see cref="CaexLinkRef"/>) so comparisons key on AML
/// identity. Scalar attributes resolve to their value (or <see cref="OclValue.Void"/>
/// when empty/absent); compound attributes resolve to a navigable object.
/// </summary>
public sealed class CaexMetamodel : IOclModel
{
    private readonly ICaexTypeRegistry _types;
    private readonly CAEXDocument? _document;

    /// <summary>For evaluating expressions against already-obtained elements (no instance enumeration).</summary>
    public CaexMetamodel(ICaexTypeRegistry? types = null) : this(null, types) { }

    /// <summary>For full validation: <paramref name="document"/> is the source for <see cref="InstancesOf"/>.</summary>
    public CaexMetamodel(CAEXDocument? document, ICaexTypeRegistry? types = null)
    {
        _document = document;
        _types = types ?? new FpdTypeRegistry();
    }

    /// <summary>Wrap an InternalElement as an OCL value (e.g. to bind as <c>self</c>).</summary>
    public static OclValue Wrap(InternalElementType element) => OclValue.Obj(new CaexElementRef(element));

    /// <summary>Wrap an InternalLink (connection) as an OCL value.</summary>
    public static OclValue WrapLink(InternalLinkType link) => OclValue.Obj(new CaexLinkRef(link));

    // ---- type identity (delegates to the registry on the unwrapped object) ---------

    public bool IsKindOf(object element, string typeName) => Unwrap(element) is { } o && _types.IsKindOf(o, typeName);
    public bool IsTypeOf(object element, string typeName) => Unwrap(element) is { } o && _types.IsTypeOf(o, typeName);

    public string TypeOf(object element) => element switch
    {
        CaexBoundsRef => "Bounds",
        AttributeType a => a.Name ?? "",
        _ => Unwrap(element) is { } o ? _types.TypeOf(o) : "",
    };

    // ---- navigation ----------------------------------------------------------------

    public OclValue GetProperty(object element, string propertyName) => element switch
    {
        CaexElementRef r => GetElementProperty(r.Element, propertyName),
        CaexLinkRef l => GetLinkProperty(l.Link, propertyName),
        CaexProjectRef p => GetProjectProperty(p.Document, propertyName),
        CaexBoundsRef b => GetBoundsProperty(b.ViewInformation, propertyName),
        AttributeType a => ResolveAttribute(FindAttribute(a.Attribute, propertyName)),
        _ => OclValue.Void,
    };

    private OclValue GetElementProperty(InternalElementType element, string name) => name switch
    {
        "containedElement" => OclValue.Collection(element.InternalElement.Select(Wrap).ToList()),
        "connections" => OclValue.Collection(element.InternalLink.Select(WrapLink).ToList()),
        "incomingConnections" => ConnectionsOf(element, incoming: true),
        "outgoingConnections" => ConnectionsOf(element, incoming: false),
        "process" => ProcessOf(element),
        "project" => OclValue.Obj(new CaexProjectRef(element.CAEXDocument)),
        "bounds" => BoundsOf(element),
        "name" => Scalar(element.Name),
        "id" => Scalar(element.ID),
        _ => ResolveAttribute(FindAttribute(element.Attribute, name)), // any other step is an attribute
    };

    /// <summary>Flat bounds access: x/y reach into the nested <c>position</c>, width/height are direct.</summary>
    private OclValue GetBoundsProperty(AttributeType viewInformation, string name) => name switch
    {
        "x" or "y" => ResolveAttribute(FindAttribute(SubAttributes(FindAttribute(viewInformation.Attribute, "position")), name)),
        "width" or "height" => ResolveAttribute(FindAttribute(viewInformation.Attribute, name)),
        _ => OclValue.Void,
    };

    /// <summary>The element's bounds (its <c>ViewInformation</c> / FPD_Bounds attribute), wrapped for flat access.</summary>
    private static OclValue BoundsOf(InternalElementType element)
    {
        var view = element.Attribute.FirstOrDefault(a =>
            a.RefAttributeType?.EndsWith("/FPD_Bounds") == true ||
            string.Equals(a.Name, "ViewInformation", StringComparison.OrdinalIgnoreCase));
        return view is null ? OclValue.Void : OclValue.Obj(new CaexBoundsRef(view));
    }

    private OclValue GetProjectProperty(CAEXDocument document, string name) => name switch
    {
        // project-wide aggregation across all hierarchies
        "containedElement" => OclValue.Collection(AllElements(document).Select(Wrap).ToList()),
        "process" => OclValue.Collection(AllElements(document).Where(ie => _types.IsKindOf(ie, "FPD_Process")).Select(Wrap).ToList()),
        _ => OclValue.Void,
    };

    private OclValue GetLinkProperty(InternalLinkType link, string name) => name switch
    {
        "source" => WrapOrVoid(EndpointElement(link.AInterface)),
        "target" => WrapOrVoid(EndpointElement(link.BInterface)),
        "sourceInterface" => InterfaceOrVoid(link.AInterface),
        "targetInterface" => InterfaceOrVoid(link.BInterface),
        _ => OclValue.Void,
    };

    private OclValue ProcessOf(InternalElementType element)
    {
        var process = NearestProcess(element);
        return process is null ? OclValue.Void : Wrap(process);
    }

    /// <summary>Climb the parent chain to the nearest enclosing FPD_Process IE.</summary>
    private InternalElementType? NearestProcess(InternalElementType element)
    {
        var current = element.CAEXParent;
        while (current is InternalElementType ie)
        {
            if (_types.IsKindOf(ie, "FPD_Process")) return ie;
            current = ie.CAEXParent;
        }
        return null;
    }

    /// <summary>
    /// Connections of an element: links in its enclosing process whose target (incoming)
    /// or source (outgoing) endpoint is this element. The A-interface is the source side,
    /// the B-interface the target side (FPD convention).
    /// </summary>
    private OclValue ConnectionsOf(InternalElementType element, bool incoming)
    {
        var process = NearestProcess(element);
        if (process is null) return OclValue.Collection(Array.Empty<OclValue>());

        var result = new List<OclValue>();
        foreach (var link in process.InternalLink)
        {
            var endpoint = EndpointElement(incoming ? link.BInterface : link.AInterface);
            if (endpoint is not null && SameElement(endpoint, element))
                result.Add(WrapLink(link));
        }
        return OclValue.Collection(result);
    }

    private static bool SameElement(InternalElementType a, InternalElementType b) =>
        a.ID is { Length: > 0 } id ? id == b.ID : ReferenceEquals(a, b);

    // ---- instance enumeration ------------------------------------------------------

    /// <summary>Every InternalElement and InternalLink in the document that <c>oclIsKindOf(typeName)</c>.</summary>
    public IEnumerable<OclValue> InstancesOf(string typeName)
    {
        if (_document?.CAEXFile is null)
            throw new InvalidOperationException("CaexMetamodel was created without a document; InstancesOf requires one.");

        foreach (var ie in AllElements(_document))
        {
            if (_types.IsKindOf(ie, typeName)) yield return Wrap(ie);
            foreach (var link in ie.InternalLink)
                if (_types.IsKindOf(link, typeName)) yield return WrapLink(link);
        }
    }

    public string? IdOf(OclValue instance)
    {
        if (instance.Kind != OclKind.Object) return null;
        return instance.AsObject() switch
        {
            CaexElementRef r => string.IsNullOrEmpty(r.Element.Name) ? r.Element.ID : r.Element.Name,
            CaexLinkRef l => l.Link.Name,
            _ => null,
        };
    }

    // ---- helpers -------------------------------------------------------------------

    private static object? Unwrap(object element) => element switch
    {
        CaexElementRef r => r.Element,
        CaexLinkRef l => l.Link,
        _ => element, // e.g. an ExternalInterfaceType passed straight through
    };

    private static InternalElementType? EndpointElement(InterfaceClassType? endpoint) =>
        (endpoint as ExternalInterfaceType)?.CAEXParent as InternalElementType;

    private static OclValue WrapOrVoid(InternalElementType? element) => element is null ? OclValue.Void : Wrap(element);

    private static OclValue InterfaceOrVoid(InterfaceClassType? endpoint) =>
        endpoint is ExternalInterfaceType iface ? OclValue.Obj(iface) : OclValue.Void;

    private static OclValue ResolveAttribute(AttributeType? attribute)
    {
        if (attribute is null) return OclValue.Void;
        // A compound attribute (sub-attributes) navigates further; a scalar one yields its typed value.
        return attribute.Attribute.Any() ? OclValue.Obj(attribute) : ScalarValue(attribute);
    }

    /// <summary>Convert a scalar attribute's string value to a typed OCL value per its <c>AttributeDataType</c> (so e.g. xs:double bounds compare numerically).</summary>
    private static OclValue ScalarValue(AttributeType attribute)
    {
        var value = attribute.Value;
        if (string.IsNullOrEmpty(value)) return OclValue.Void;

        return (attribute.AttributeDataType ?? "") switch
        {
            "xs:double" or "xs:float" or "xs:decimal"
                => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? OclValue.Real(d) : OclValue.Str(value),
            "xs:int" or "xs:integer" or "xs:long" or "xs:short" or "xs:byte"
                => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? OclValue.Int(i) : OclValue.Str(value),
            "xs:boolean"
                => value is "true" or "false" ? OclValue.Bool(value == "true") : OclValue.Str(value),
            _ => OclValue.Str(value),
        };
    }

    private static OclValue Scalar(string? value) =>
        string.IsNullOrEmpty(value) ? OclValue.Void : OclValue.Str(value);

    private static AttributeType? FindAttribute(IEnumerable<AttributeType> attributes, string name) =>
        attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<AttributeType> SubAttributes(AttributeType? attribute) =>
        attribute?.Attribute ?? Enumerable.Empty<AttributeType>();

    private static IEnumerable<InternalElementType> AllElements(CAEXDocument document)
    {
        if (document?.CAEXFile is null) yield break;
        foreach (var ih in document.CAEXFile.InstanceHierarchy)
            foreach (var ie in Descendants(ih.InternalElement))
                yield return ie;
    }

    /// <summary>Pre-order depth-first walk, iterative so a deeply nested AML hierarchy cannot overflow the stack.</summary>
    private static IEnumerable<InternalElementType> Descendants(IEnumerable<InternalElementType> roots)
    {
        var stack = new Stack<InternalElementType>(roots.Reverse());
        while (stack.Count > 0)
        {
            var ie = stack.Pop();
            yield return ie;
            foreach (var child in ie.InternalElement.Reverse())
                stack.Push(child);
        }
    }
}
