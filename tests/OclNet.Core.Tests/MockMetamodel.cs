using OclNet.Core.Metamodel;
using OclNet.Core.Validation;
using OclNet.Core.Values;

namespace OclNet.Core.Tests;

/// <summary>
/// A tiny in-memory model element: a type name plus named properties. Stands in
/// for a CAEX InternalElement so the interpreter can be exercised without the
/// Aml.Engine binding (which lands in Milestone 3).
/// </summary>
internal sealed class MockElement
{
    public string Type { get; }
    private readonly Dictionary<string, OclValue> _properties = new(StringComparer.Ordinal);

    public MockElement(string type) => Type = type;

    public MockElement With(string name, OclValue value)
    {
        _properties[name] = value;
        return this;
    }

    public bool TryGet(string name, out OclValue value) => _properties.TryGetValue(name, out value!);

    public OclValue AsValue() => OclValue.Obj(this);
}

/// <summary>
/// Mock <see cref="IOclMetamodel"/> over <see cref="MockElement"/>, configured with
/// a single-inheritance type hierarchy (subtype → supertype). Identity is reference
/// equality — adequate for the mock; the real CAEX binding will compare by AML ID.
/// </summary>
internal sealed class MockMetamodel : IOclModel
{
    private readonly Dictionary<string, string?> _parent;
    private readonly List<MockElement> _instances = new();

    public MockMetamodel(Dictionary<string, string?> parent) => _parent = parent;

    /// <summary>Register elements that <see cref="InstancesOf"/> can enumerate (for validator tests).</summary>
    public MockMetamodel WithInstances(params MockElement[] elements)
    {
        _instances.AddRange(elements);
        return this;
    }

    public IEnumerable<OclValue> InstancesOf(string typeName) =>
        _instances.Where(e => IsKindOf(e, typeName)).Select(e => e.AsValue());

    public string? IdOf(OclValue instance) =>
        instance.Kind == OclKind.Object && instance.AsObject() is MockElement e
            ? (e.TryGet("name", out var n) && n.Kind == OclKind.String ? n.AsString() : e.Type)
            : null;

    /// <summary>The FPD type hierarchy used by the VDI 3682 catalogue rules.</summary>
    public static MockMetamodel Fpd() => new(new Dictionary<string, string?>
    {
        ["FPD_Object"] = null,
        ["FPD_State"] = "FPD_Object",
        ["FPD_Product"] = "FPD_State",
        ["FPD_Energy"] = "FPD_State",
        ["FPD_Information"] = "FPD_State",
        ["FPD_ProcessOperator"] = "FPD_Object",
        ["FPD_TechnicalResource"] = "FPD_Object",
        ["FPD_SystemLimit"] = "FPD_Object",
        ["FPD_Connection"] = null,
        ["FPD_Flow"] = "FPD_Connection",
        ["FPD_Usage"] = "FPD_Connection",
    });

    public bool IsKindOf(object element, string typeName)
    {
        string? current = ((MockElement)element).Type;
        while (current is not null)
        {
            if (current == typeName) return true;
            current = _parent.TryGetValue(current, out var p) ? p : null;
        }
        return false;
    }

    public bool IsTypeOf(object element, string typeName) => ((MockElement)element).Type == typeName;

    public string TypeOf(object element) => ((MockElement)element).Type;

    public OclValue GetProperty(object element, string propertyName) =>
        ((MockElement)element).TryGet(propertyName, out var value) ? value : OclValue.Void;
}
