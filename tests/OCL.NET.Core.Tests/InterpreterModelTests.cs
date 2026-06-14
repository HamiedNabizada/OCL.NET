using OCL.NET.Core.Interpreter;
using OCL.NET.Core.Metamodel;
using OCL.NET.Core.Parser;
using OCL.NET.Core.Values;
using Xunit;

namespace OCL.NET.Core.Tests;

/// <summary>
/// End-to-end Milestone-2 slice: parse real catalogue-style OCL and evaluate it
/// against a mock model. Exercises navigation, the type operations, collection
/// operations, iterators (incl. the two-variable form), let, and the implicit-self
/// rule together.
/// </summary>
public class InterpreterModelTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly MockMetamodel _fpd = MockMetamodel.Fpd();

    private OclValue Eval(string ocl, MockElement self)
    {
        var expr = _parser.ParseExpression(ocl);
        var env = new EvaluationEnvironment(_fpd).Bind("self", self.AsValue());
        return _interp.Evaluate(expr, env);
    }

    private static OclValue Coll(params MockElement[] elements) =>
        OclValue.Collection(elements.Select(e => e.AsValue()).ToList());

    // ---- navigation ----------------------------------------------------------------

    [Fact]
    public void Navigates_nested_property()
    {
        var self = new MockElement("FPD_ProcessOperator")
            .With("identification", new MockElement("Identification").With("longName", OclValue.Str("Pump")).AsValue());
        Assert.Equal("Pump", Eval("self.identification.longName", self).AsString());
    }

    [Fact]
    public void NotEmpty_is_false_for_absent_property()
    {
        var self = new MockElement("FPD_Process");
        Assert.False(Eval("self.name->notEmpty()", self).AsBool());
    }

    [Fact]
    public void NotEmpty_is_true_for_present_scalar()
    {
        var self = new MockElement("FPD_Process").With("name", OclValue.Str("P1"));
        Assert.True(Eval("self.name->notEmpty()", self).AsBool());
    }

    [Fact]
    public void Collection_navigation_collects_and_flattens()
    {
        // self.process.containedElement  over two processes, each holding a collection.
        var p1 = new MockElement("FPD_Process").With("containedElement", Coll(new MockElement("FPD_State")));
        var p2 = new MockElement("FPD_Process").With("containedElement", Coll(new MockElement("FPD_State"), new MockElement("FPD_ProcessOperator")));
        var self = new MockElement("FPD_Project").With("process", Coll(p1, p2));
        Assert.Equal(3, Eval("self.process.containedElement->size()", self).AsInt());
    }

    // ---- type operations -----------------------------------------------------------

    [Fact]
    public void OclIsKindOf_respects_subtype_hierarchy()
    {
        var self = new MockElement("FPD_Product");
        Assert.True(Eval("self.oclIsKindOf(FPD_State)", self).AsBool());   // Product is-a State
        Assert.False(Eval("self.oclIsTypeOf(FPD_State)", self).AsBool());  // but not exactly a State
    }

    [Fact]
    public void Implicit_self_resolves_bare_property_names()
    {
        // C2: in context FPD_Flow, bare "source"/"target" mean self.source / self.target.
        var self = new MockElement("FPD_Flow")
            .With("source", new MockElement("FPD_State").AsValue())
            .With("target", new MockElement("FPD_State").AsValue());
        Assert.False(Eval("not (source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_State))", self).AsBool());
    }

    [Fact]
    public void OclType_comparison()
    {
        var flow = new MockElement("FPD_Flow");
        var self = new MockElement("X").With("a", flow.AsValue()).With("b", flow.AsValue());
        Assert.True(Eval("self.a.oclType() = self.b.oclType()", self).AsBool());
    }

    // ---- select / size (rule A2 shape) ---------------------------------------------

    [Fact]
    public void Select_by_type_then_size_matches_cardinality_rule()
    {
        var self = new MockElement("FPD_Process").With("containedElement", Coll(
            new MockElement("FPD_SystemLimit"),
            new MockElement("FPD_State"),
            new MockElement("FPD_ProcessOperator")));
        Assert.True(Eval("self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1", self).AsBool());
    }

    // ---- forAll / exists -----------------------------------------------------------

    [Fact]
    public void ForAll_over_collection()
    {
        var self = new MockElement("FPD_Process").With("containedElement", Coll(
            new MockElement("FPD_State"), new MockElement("FPD_Product")));
        Assert.True(Eval("self.containedElement->forAll(e | e.oclIsKindOf(FPD_Object))", self).AsBool());
    }

    [Fact]
    public void Exists_finds_matching_element()
    {
        var self = new MockElement("FPD_Process").With("containedElement", Coll(
            new MockElement("FPD_State"), new MockElement("FPD_ProcessOperator")));
        Assert.True(Eval("self.containedElement->exists(e | e.oclIsKindOf(FPD_ProcessOperator))", self).AsBool());
    }

    [Fact]
    public void Two_variable_forAll_detects_duplicate_identifiers()
    {
        // D1 shape: uniqueIdent must differ for any two distinct elements.
        MockElement WithIdent(string id) =>
            new MockElement("FPD_State").With("identification", new MockElement("Id").With("uniqueIdent", OclValue.Str(id)).AsValue());

        var ocl = "self.containedElement->forAll(e1, e2 | e1 <> e2 implies e1.identification.uniqueIdent <> e2.identification.uniqueIdent)";

        var unique = new MockElement("FPD_Process").With("containedElement", Coll(WithIdent("a"), WithIdent("b")));
        Assert.True(Eval(ocl, unique).AsBool());

        var dup = new MockElement("FPD_Process").With("containedElement", Coll(WithIdent("a"), WithIdent("a")));
        Assert.False(Eval(ocl, dup).AsBool());
    }

    // ---- let, includes, Sequence literal -------------------------------------------

    [Fact]
    public void Let_binds_then_uses_in_body()
    {
        var self = new MockElement("FPD_Process").With("containedElement", Coll(
            new MockElement("FPD_State"), new MockElement("FPD_State")));
        Assert.True(Eval("let states = self.containedElement in states->size() = 2", self).AsBool());
    }

    [Fact]
    public void Includes_and_excludes_over_scalar_collection()
    {
        var self = new MockElement("X").With("tags", OclValue.Collection(new[] { OclValue.Str("a"), OclValue.Str("b") }));
        Assert.True(Eval("self.tags->includes('a')", self).AsBool());
        Assert.True(Eval("self.tags->excludes('z')", self).AsBool());
    }

    // ---- reference-resolution rules (F1 / F3) --------------------------------------

    private static MockElement WithUniqueIdent(string uid) =>
        new MockElement("FPD_Object").With("identification", new MockElement("Id").With("uniqueIdent", OclValue.Str(uid)).AsValue());

    [Fact]
    public void RefObjResolvable_holds_when_target_exists_and_fails_when_dangling()
    {
        // F1: self.refObj->notEmpty() implies project.containedElement->exists(e | e.identification.uniqueIdent = self.refObj)
        var project = new MockElement("Project").With("containedElement", Coll(WithUniqueIdent("X1"), WithUniqueIdent("X2")));
        const string f1 = "self.refObj->notEmpty() implies " +
                          "self.project.containedElement->exists(e | e.identification.uniqueIdent = self.refObj)";

        var resolves = new MockElement("FPD_Object").With("refObj", OclValue.Str("X1")).With("project", project.AsValue());
        Assert.True(Eval(f1, resolves).AsBool());

        var dangling = new MockElement("FPD_Object").With("refObj", OclValue.Str("MISSING")).With("project", project.AsValue());
        Assert.False(Eval(f1, dangling).AsBool());

        // refObj absent ⇒ vacuously satisfied (false implies … = true).
        var noRef = new MockElement("FPD_Object").With("project", project.AsValue());
        Assert.True(Eval(f1, noRef).AsBool());
    }

    [Fact]
    public void AllReferencesResolvable_checks_every_non_empty_reference()
    {
        // F3: each non-empty ref in the Sequence must resolve.
        var project = new MockElement("Project").With("containedElement", Coll(WithUniqueIdent("A"), WithUniqueIdent("B")));
        const string f3 = "Sequence{self.refObj, self.refBaseObj, self.refExtendedObj, self.refComposedObj}" +
                          "->select(r | r->notEmpty())" +
                          "->forAll(r | self.project.containedElement->exists(e | e.identification.uniqueIdent = r))";

        var ok = new MockElement("FPD_Object")
            .With("refObj", OclValue.Str("A")).With("refBaseObj", OclValue.Str("B"))
            .With("project", project.AsValue()); // refExtendedObj / refComposedObj absent → filtered out
        Assert.True(Eval(f3, ok).AsBool());

        var bad = new MockElement("FPD_Object")
            .With("refObj", OclValue.Str("A")).With("refComposedObj", OclValue.Str("GHOST"))
            .With("project", project.AsValue());
        Assert.False(Eval(f3, bad).AsBool());
    }

    // ---- transitive closure (F4 / F7) ---------------------------------------------

    [Fact]
    public void Closure_collects_transitively_reachable_elements()
    {
        var c = new MockElement("PO");
        var b = new MockElement("PO").With("children", Coll(c));
        var a = new MockElement("PO").With("children", Coll(b));
        Assert.Equal(2, Eval("self->closure(e | e.children)->size()", a).AsInt()); // b and c
    }

    [Fact]
    public void Closure_includes_self_only_on_a_cycle_detecting_circular_decomposition()
    {
        // F4: not self->closure(...)->includes(self)
        const string f4 = "not self->closure(e | e.children)->includes(self)";

        var x = new MockElement("PO");
        var y = new MockElement("PO");
        x.With("children", Coll(y));
        y.With("children", Coll(x)); // cycle x → y → x
        Assert.False(Eval(f4, x).AsBool()); // cycle ⇒ violation

        var lone = new MockElement("PO");
        Assert.True(Eval(f4, lone).AsBool()); // no decomposition ⇒ holds
    }

    [Fact]
    public void ClosureDepth_measures_nesting_depth()
    {
        var c = new MockElement("PO");
        var b = new MockElement("PO").With("children", Coll(c));
        var a = new MockElement("PO").With("children", Coll(b));
        Assert.Equal(2, Eval("self->closureDepth(e | e.children)", a).AsInt());
        Assert.True(Eval("self->closureDepth(e | e.children) <= 5", a).AsBool()); // F7
    }

    // ---- string regex (C7) ---------------------------------------------------------

    [Fact]
    public void Matches_does_a_full_string_regex_match()
    {
        var canonical = new MockElement("X").With("path", OclValue.Str("VDI_FPD_InterfaceClassLib/FPD_FlowOut"));
        Assert.True(Eval("self.path->matches('VDI_FPD_InterfaceClassLib/.*')", canonical).AsBool());

        var foreign = new MockElement("X").With("path", OclValue.Str("OtherLib/Foo"));
        Assert.False(Eval("self.path->matches('VDI_FPD_InterfaceClassLib/.*')", foreign).AsBool());
    }

    [Fact]
    public void Sequence_literal_select_notEmpty_filters_blanks()
    {
        // F3 shape: keep only the references that are non-empty.
        var self = new MockElement("FPD_Object")
            .With("refObj", OclValue.Str("r1"))
            .With("refBaseObj", OclValue.Void);
        Assert.Equal(1, Eval("Sequence{self.refObj, self.refBaseObj}->select(r | r->notEmpty())->size()", self).AsInt());
    }
}
