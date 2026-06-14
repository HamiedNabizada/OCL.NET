using OCL.NET.Core.Interpreter;
using OCL.NET.Core.Parser;
using OCL.NET.Core.Values;
using Xunit;

namespace OCL.NET.Core.Tests;

/// <summary>
/// Edge coverage for value rendering, the collection/standard library, the
/// if-then-else expression, and operation resolution.
/// </summary>
public class ValueLibraryTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();

    private OclValue E(string ocl) => _interp.Evaluate(_parser.ParseExpression(ocl), new EvaluationEnvironment());

    // ---- OclValue rendering & accessors --------------------------------------------

    [Theory]
    [InlineData("true", "true")]
    [InlineData("42", "42")]
    [InlineData("1.5", "1.5")]
    [InlineData("'x'", "'x'")]
    public void Value_to_string(string ocl, string expected) => Assert.Equal(expected, E(ocl).ToString());

    [Fact] public void Collection_to_string() => Assert.Equal("Collection{1, 2}", E("Sequence{1, 2}").ToString());

    [Fact] public void Void_and_invalid_render() { Assert.Equal("invalid", OclValue.Invalid.ToString()); Assert.Equal("null", OclValue.Void.ToString()); }

    [Fact] public void AsInt_on_string_throws() => Assert.Throws<InvalidOperationException>(() => OclValue.Str("x").AsInt());

    [Fact] public void IsDefined_distinguishes_void() { Assert.False(OclValue.Void.IsDefined); Assert.True(OclValue.Int(1).IsDefined); }

    [Fact] public void Integer_widens_to_real() => Assert.Equal(2.0, OclValue.Int(2).AsReal());

    // ---- standard library ----------------------------------------------------------

    [Fact] public void First_and_last() { Assert.Equal(1, E("Sequence{1, 2, 3}->first()").AsInt()); Assert.Equal(3, E("Sequence{1, 2, 3}->last()").AsInt()); }

    [Fact] public void First_on_empty_is_invalid() => Assert.Equal(OclKind.Invalid, E("Sequence{}->first()").Kind);

    [Fact] public void AsSet_deduplicates() => Assert.Equal(2, E("Sequence{1, 1, 2}->asSet()->size()").AsInt());

    [Fact] public void AsSequence_keeps_all() => Assert.Equal(3, E("Sequence{1, 2, 3}->asSequence()->size()").AsInt());

    [Fact] public void Excludes() { Assert.True(E("Sequence{1, 2}->excludes(3)").AsBool()); Assert.False(E("Sequence{1, 2}->excludes(1)").AsBool()); }

    [Fact] public void Reject_keeps_non_matching() => Assert.Equal(1, E("Sequence{1, 2}->reject(x | x = 2)->size()").AsInt());

    [Fact] public void Collect_maps_each_element() => Assert.Equal(2, E("Sequence{1, 2}->collect(x | x)->size()").AsInt());

    // ---- if-then-else (previously uncovered) ---------------------------------------

    [Fact] public void If_true_branch() => Assert.Equal(1, E("if true then 1 else 2 endif").AsInt());

    [Fact] public void If_false_branch() => Assert.Equal(2, E("if false then 1 else 2 endif").AsInt());

    [Fact] public void If_non_boolean_condition_is_invalid() => Assert.Equal(OclKind.Invalid, E("if 5 then 1 else 2 endif").Kind);

    // ---- operation registry --------------------------------------------------------

    [Fact] public void Empty_registry_resolves_nothing() => Assert.Null(OperationRegistry.Empty.Resolve("foo", 0, "T"));

    [Fact]
    public void Registry_disambiguates_same_name_by_context_type()
    {
        var defs = _parser.ParseDefinitions("context A def: f(): Boolean = true  context B def: f(): Boolean = false");
        var registry = new OperationRegistry(defs);
        Assert.Equal("A", registry.Resolve("f", 0, "A")!.ContextType);
        Assert.Equal("B", registry.Resolve("f", 0, "B")!.ContextType);
    }

    [Fact]
    public void Registry_refuses_ambiguous_fallback_for_foreign_receiver()
    {
        // Two same-named defs on different contexts: a third receiver type must NOT
        // silently get "the first one" — that would defeat type-scoped operations.
        var defs = _parser.ParseDefinitions("context A def: f(): Boolean = true  context B def: f(): Boolean = false");
        Assert.Null(new OperationRegistry(defs).Resolve("f", 0, "C"));
    }

    [Fact]
    public void Registry_allows_unambiguous_fallback()
    {
        // Exactly one definition: a receiver whose concrete type name differs from
        // the conceptual context (e.g. a binding wrapper) still resolves.
        var defs = _parser.ParseDefinitions("context A def: g(): Boolean = true");
        Assert.NotNull(new OperationRegistry(defs).Resolve("g", 0, "SomethingElse"));
    }
}
