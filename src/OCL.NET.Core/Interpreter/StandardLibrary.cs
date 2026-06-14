using System.Globalization;
using System.Text.RegularExpressions;
using OCL.NET.Core.Ast;
using OCL.NET.Core.Values;

namespace OCL.NET.Core.Interpreter;

/// <summary>
/// The non-iterator part of the OCL standard library: collection operations that
/// are pure functions of an already-evaluated source value and arguments
/// (<c>size</c>, <c>first</c>, <c>includes</c>, <c>asSet</c>, …). Iterators
/// (<c>select</c>/<c>forAll</c>/…) need the evaluation environment and live in the
/// interpreter; everything here is side-effect-free and independently testable.
///
/// As the operation set grows toward full OCL this is the natural place to evolve
/// into a registry; for the Phase-1 set a dispatch switch is clearer.
/// </summary>
public static class StandardLibrary
{
    /// <summary>
    /// OCL's implicit-collection rule for <c>-&gt;</c>: a non-collection source is
    /// treated as a singleton, and the undefined value (<c>OclVoid</c>) as the empty
    /// collection — which is exactly why <c>self.longName-&gt;notEmpty()</c> is false
    /// when the navigation found nothing.
    /// </summary>
    public static IReadOnlyList<OclValue> AsCollection(OclValue value) => value.Kind switch
    {
        OclKind.Collection => value.AsCollection(),
        OclKind.Void => Array.Empty<OclValue>(),
        _ => new[] { value },
    };

    /// <summary>
    /// Invoke a named collection operation. Returns <c>null</c> if the name is not a
    /// Phase-1 standard operation, so the interpreter can distinguish "unsupported"
    /// from a legitimate result.
    /// </summary>
    /// <summary>String-typed operations — a Void source makes these undefined (Invalid), not "unsupported".</summary>
    private static readonly HashSet<string> StringOperations = new(StringComparer.Ordinal)
    {
        "matches", "toUpperCase", "toLowerCase", "trim", "concat", "indexOf", "substring",
    };

    public static OclValue? Invoke(string name, OclValue source, IReadOnlyList<OclValue> arguments, CallStyle style)
    {
        // A collection operation on an erroneous source stays erroneous.
        if (source.Kind == OclKind.Invalid) return OclValue.Invalid;

        // A string operation on an undefined source is undefined — without this,
        // `x->matches(...)` with x = Void would fall through to "not supported"
        // and surface as a misleading evaluation error instead of a violation.
        if (source.Kind == OclKind.Void && StringOperations.Contains(name)) return OclValue.Invalid;

        // String operations: `matches` applies regardless of call style (the catalogue
        // writes `path->matches(...)`); the others are dot-style value operations, so
        // that `s->size()` still means "collection of one" while `s.size()` is length.
        if (source.Kind == OclKind.String)
        {
            if (name == "matches") return Matches(source.AsString(), arguments);
            if (style == CallStyle.Dot)
            {
                var stringResult = InvokeString(name, source.AsString(), arguments);
                if (stringResult is not null) return stringResult;
            }
        }

        var items = AsCollection(source);
        switch (name)
        {
            case "size": return OclValue.Int(items.Count);
            case "isEmpty": return OclValue.Bool(items.Count == 0);
            case "notEmpty": return OclValue.Bool(items.Count > 0);
            case "first": return items.Count > 0 ? items[0] : OclValue.Invalid;
            case "last": return items.Count > 0 ? items[^1] : OclValue.Invalid;
            case "includes": return OclValue.Bool(Contains(items, Arg(arguments)));
            case "excludes": return OclValue.Bool(!Contains(items, Arg(arguments)));
            case "asSet": return OclValue.Collection(Distinct(items));
            case "asSequence": return OclValue.Collection(items.ToList());
            default: return null;
        }
    }

    /// <summary>Dot-style String operations. Returns null if <paramref name="name"/> is not a known String op.</summary>
    private static OclValue? InvokeString(string name, string s, IReadOnlyList<OclValue> arguments)
    {
        switch (name)
        {
            case "size": return OclValue.Int(s.Length);
            case "toUpperCase": return OclValue.Str(s.ToUpperInvariant());
            case "toLowerCase": return OclValue.Str(s.ToLowerInvariant());
            case "trim": return OclValue.Str(s.Trim());
            case "concat":
                return Arg(arguments) is { Kind: OclKind.String } c ? OclValue.Str(s + c.AsString()) : OclValue.Invalid;
            case "indexOf":
                return Arg(arguments) is { Kind: OclKind.String } n ? OclValue.Int(s.IndexOf(n.AsString(), StringComparison.Ordinal) + 1) : OclValue.Invalid;
            case "substring":
                // OCL substring(lower, upper): 1-based, inclusive.
                if (arguments.Count >= 2 && arguments[0].Kind == OclKind.Integer && arguments[1].Kind == OclKind.Integer)
                {
                    var lower = (int)arguments[0].AsInt();
                    var upper = (int)arguments[1].AsInt();
                    if (lower >= 1 && upper <= s.Length && lower <= upper + 1)
                        return OclValue.Str(s.Substring(lower - 1, upper - lower + 1));
                }
                return OclValue.Invalid;
            default: return null;
        }
    }

    /// <summary>OCL <c>matches(regex)</c> — full-string regex match (like Java's String.matches). Invalid pattern ⇒ OclInvalid.</summary>
    private static OclValue Matches(string input, IReadOnlyList<OclValue> arguments)
    {
        if (Arg(arguments) is not { Kind: OclKind.String } patternValue) return OclValue.Invalid;
        try
        {
            return OclValue.Bool(Regex.IsMatch(input, "^(?:" + patternValue.AsString() + ")$"));
        }
        catch (ArgumentException)
        {
            return OclValue.Invalid; // malformed pattern
        }
    }

    private static OclValue Arg(IReadOnlyList<OclValue> arguments) =>
        arguments.Count > 0 ? arguments[0] : OclValue.Invalid;

    private static bool Contains(IReadOnlyList<OclValue> items, OclValue value) =>
        items.Any(e => e.ValueEquals(value));

    /// <summary>Duplicate-free copy using OCL value equality (also used for Set/OrderedSet literals).</summary>
    internal static List<OclValue> Distinct(IReadOnlyList<OclValue> items)
    {
        var result = new List<OclValue>();
        foreach (var item in items)
            if (!Contains(result, item))
                result.Add(item);
        return result;
    }
}
