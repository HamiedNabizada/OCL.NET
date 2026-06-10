namespace OclNet.Core.Values;

/// <summary>The runtime category of an <see cref="OclValue"/>.</summary>
public enum OclKind
{
    Boolean,
    Integer,
    Real,
    String,
    /// <summary>An opaque model element supplied by the metamodel binding (e.g. a CAEX InternalElement).</summary>
    Object,
    Collection,
    /// <summary>OCL <c>OclVoid</c> — the value of an undefined navigation (null).</summary>
    Void,
    /// <summary>OCL <c>OclInvalid</c> — the result of an erroneous evaluation (e.g. division by zero).</summary>
    Invalid,
}

/// <summary>
/// A tagged union over the value types the OCL interpreter produces. Immutable;
/// constructed only through the static factories so the singletons
/// (<see cref="True"/>/<see cref="False"/>/<see cref="Void"/>/<see cref="Invalid"/>)
/// stay canonical.
/// </summary>
public sealed class OclValue
{
    public OclKind Kind { get; }
    private readonly object? _value;

    private OclValue(OclKind kind, object? value)
    {
        Kind = kind;
        _value = value;
    }

    public static readonly OclValue True = new(OclKind.Boolean, true);
    public static readonly OclValue False = new(OclKind.Boolean, false);

    /// <summary>OCL <c>OclVoid</c> — result of an undefined navigation.</summary>
    public static readonly OclValue Void = new(OclKind.Void, null);

    /// <summary>OCL <c>OclInvalid</c> — result of an erroneous evaluation.</summary>
    public static readonly OclValue Invalid = new(OclKind.Invalid, null);

    public static OclValue Bool(bool b) => b ? True : False;
    public static OclValue Int(long i) => new(OclKind.Integer, i);
    public static OclValue Real(double d) => new(OclKind.Real, d);
    public static OclValue Str(string s) => new(OclKind.String, s);

    /// <summary>Wrap an opaque model element. The metamodel binding owns its identity semantics.</summary>
    public static OclValue Obj(object o) => new(OclKind.Object, o);

    public static OclValue Collection(IReadOnlyList<OclValue> items) => new(OclKind.Collection, items);

    public bool IsNumeric => Kind is OclKind.Integer or OclKind.Real;

    /// <summary>True unless the value is <c>OclVoid</c> or <c>OclInvalid</c> (OCL <c>oclIsUndefined</c> is the inverse).</summary>
    public bool IsDefined => Kind is not (OclKind.Void or OclKind.Invalid);

    public bool AsBool() => Kind == OclKind.Boolean
        ? (bool)_value!
        : throw new InvalidOperationException($"OCL value of kind {Kind} is not a Boolean.");

    public long AsInt() => Kind == OclKind.Integer
        ? (long)_value!
        : throw new InvalidOperationException($"OCL value of kind {Kind} is not an Integer.");

    /// <summary>Numeric value as a double; widens Integer to Real.</summary>
    public double AsReal() => Kind switch
    {
        OclKind.Real => (double)_value!,
        OclKind.Integer => (long)_value!,
        _ => throw new InvalidOperationException($"OCL value of kind {Kind} is not numeric."),
    };

    public string AsString() => Kind == OclKind.String
        ? (string)_value!
        : throw new InvalidOperationException($"OCL value of kind {Kind} is not a String.");

    public object AsObject() => Kind == OclKind.Object
        ? _value!
        : throw new InvalidOperationException($"OCL value of kind {Kind} is not an Object.");

    public IReadOnlyList<OclValue> AsCollection() => Kind == OclKind.Collection
        ? (IReadOnlyList<OclValue>)_value!
        : throw new InvalidOperationException($"OCL value of kind {Kind} is not a Collection.");

    /// <summary>
    /// OCL value equality (<c>=</c>). Numbers compare by value across Integer/Real;
    /// strings and booleans by value; objects by their underlying <see cref="object.Equals(object)"/>
    /// (the metamodel binding is responsible for ID-based identity there — Aml.Engine
    /// wrappers are not reference-stable); collections structurally and order-sensitively.
    /// Void/Invalid are equal only to themselves.
    /// </summary>
    public bool ValueEquals(OclValue other)
    {
        if (Kind is OclKind.Void or OclKind.Invalid || other.Kind is OclKind.Void or OclKind.Invalid)
            return Kind == other.Kind;

        if (IsNumeric && other.IsNumeric)
        {
            // Integer == Integer stays exact; any Real participant compares as double.
            if (Kind == OclKind.Integer && other.Kind == OclKind.Integer)
                return AsInt() == other.AsInt();
            return AsReal().Equals(other.AsReal());
        }

        if (Kind != other.Kind) return false;

        return Kind switch
        {
            OclKind.Boolean => AsBool() == other.AsBool(),
            OclKind.String => AsString() == other.AsString(),
            OclKind.Object => Equals(_value, other._value),
            OclKind.Collection => CollectionsEqual(AsCollection(), other.AsCollection()),
            _ => false,
        };
    }

    private static bool CollectionsEqual(IReadOnlyList<OclValue> a, IReadOnlyList<OclValue> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].ValueEquals(b[i])) return false;
        return true;
    }

    public override string ToString() => Kind switch
    {
        OclKind.Boolean => AsBool() ? "true" : "false",
        OclKind.Integer => AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
        OclKind.Real => AsReal().ToString(System.Globalization.CultureInfo.InvariantCulture),
        OclKind.String => $"'{AsString()}'",
        OclKind.Object => _value?.ToString() ?? "<object>",
        OclKind.Collection => $"Collection{{{string.Join(", ", AsCollection())}}}",
        OclKind.Void => "null",
        OclKind.Invalid => "invalid",
        _ => "<?>",
    };
}
