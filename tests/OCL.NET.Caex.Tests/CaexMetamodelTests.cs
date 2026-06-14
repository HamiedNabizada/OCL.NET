using Aml.Engine.CAEX;
using OCL.NET.Caex;
using OCL.NET.Core.Interpreter;
using OCL.NET.Core.Parser;
using OCL.NET.Core.Values;
using Xunit;

namespace OCL.NET.Caex.Tests;

/// <summary>
/// Milestone-3 acceptance: the CAEX binding lets parser + interpreter evaluate
/// catalogue-style OCL against a real AutomationML document
/// (<c>TestData/test.aml</c>). Asserted counts are read directly from that file:
/// process "Step" holds 3 ProcessOperators, 1 Product (state) and 1 SystemLimit;
/// "TestProcess" holds 1 PO, 2 Products, 1 TechnicalResource and 1 SystemLimit.
/// </summary>
public class CaexMetamodelTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly CaexMetamodel _mm = new();
    private readonly CAEXDocument _doc = LoadTestAml();

    private static CAEXDocument LoadTestAml() =>
        CAEXDocument.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "test.aml"));

    private InternalElementType Process(string name) =>
        _doc.CAEXFile.InstanceHierarchy
            .SelectMany(ih => ih.InternalElement)
            .First(ie => ie.RefBaseSystemUnitPath?.EndsWith("/FPD_Process") == true && ie.Name == name);

    private static InternalElementType Child(InternalElementType parent, string name) =>
        parent.InternalElement.First(ie => ie.Name == name);

    private OclValue Eval(string ocl, InternalElementType self)
    {
        var expr = _parser.ParseExpression(ocl);
        var env = new EvaluationEnvironment(_mm).Bind("self", CaexMetamodel.Wrap(self));
        return _interp.Evaluate(expr, env);
    }

    // ---- navigation & cardinality (A2/A3/A4 shapes) --------------------------------

    [Fact]
    public void ContainedElement_counts_direct_children()
    {
        Assert.Equal(5, Eval("self.containedElement->size()", Process("Step")).AsInt());
    }

    [Theory]
    [InlineData("Step", "FPD_SystemLimit", 1)]
    [InlineData("Step", "FPD_ProcessOperator", 3)]
    [InlineData("Step", "FPD_State", 1)]            // 1 Product, counted as a State (subtype)
    [InlineData("TestProcess", "FPD_State", 2)]
    [InlineData("TestProcess", "FPD_ProcessOperator", 1)]
    [InlineData("TestProcess", "FPD_TechnicalResource", 1)]
    [InlineData("TestProcess", "FPD_SystemLimit", 1)]
    public void Select_by_type_then_size(string process, string type, int expected)
    {
        var actual = Eval($"self.containedElement->select(e | e.oclIsKindOf({type}))->size()", Process(process)).AsInt();
        Assert.Equal(expected, actual);
    }

    // ---- type operations -----------------------------------------------------------

    [Fact]
    public void Product_is_kind_of_State_but_not_type_of_State()
    {
        var product = Child(Process("Step"), "IntermediateProduct");
        Assert.True(Eval("self.oclIsKindOf(FPD_State)", product).AsBool());
        Assert.False(Eval("self.oclIsTypeOf(FPD_State)", product).AsBool());
        Assert.True(Eval("self.oclIsTypeOf(FPD_Product)", product).AsBool());
    }

    [Fact]
    public void OclType_returns_concrete_type_name()
    {
        Assert.Equal("FPD_ProcessOperator", Eval("self.oclType()", Child(Process("Step"), "Step3")).AsString());
    }

    // ---- identification attribute navigation ---------------------------------------

    [Fact]
    public void UniqueIdent_is_present_but_longName_is_empty()
    {
        var po = Child(Process("Step"), "Step3");
        Assert.True(Eval("self.identification.uniqueIdent->notEmpty()", po).AsBool());
        Assert.False(Eval("self.identification.longName->notEmpty()", po).AsBool()); // self-closing in test.aml
    }

    [Fact]
    public void ProcessOperatorNamed_rule_holds_via_element_name()
    {
        // D3: PO is named — longName/shortName are empty but the element Name is set.
        var po = Child(Process("Step"), "Step3");
        var ocl = "(self.identification.longName->notEmpty() and self.identification.longName->size() > 0) or " +
                  "(self.identification.shortName->notEmpty() and self.identification.shortName->size() > 0) or " +
                  "(self.name->notEmpty())";
        Assert.True(Eval(ocl, po).AsBool());
    }

    // ---- full constraint body (preview of Milestone 4) -----------------------------

    [Fact]
    public void SystemLimitCardinality_constraint_holds_on_test_aml()
    {
        var constraint = _parser.ParseConstraint(
            "context FPD_Process inv SystemLimitCardinality: " +
            "self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1");

        var env = new EvaluationEnvironment(_mm).Bind("self", CaexMetamodel.Wrap(Process("TestProcess")));
        Assert.True(_interp.Evaluate(constraint.Body, env).AsBool());
    }

    // ---- project-wide aggregation (F1/F2/F3 navigation) ----------------------------

    [Fact]
    public void Project_aggregates_all_elements_and_processes()
    {
        // test.aml holds 12 InternalElements total across 2 processes.
        var anyElement = Child(Process("Step"), "Step3");
        Assert.Equal(12, Eval("self.project.containedElement->size()", anyElement).AsInt());
        Assert.Equal(2, Eval("self.project.process->size()", anyElement).AsInt());
    }

    // ---- numeric attribute values (Diagram-Interchange bounds) ---------------------

    [Fact]
    public void Bounds_coordinate_resolves_as_a_real_number()
    {
        // ViewInformation/position/x is declared xs:double — must navigate to a Real, not a string.
        var x = Eval("self.viewInformation.position.x", Child(Process("Step"), "Step3"));
        Assert.Equal(OclKind.Real, x.Kind);
    }

    // ---- identity wrapper ----------------------------------------------------------

    [Fact]
    public void Element_refs_compare_by_aml_id()
    {
        var ie = Process("Step");
        Assert.Equal(new CaexElementRef(ie), new CaexElementRef(ie));
    }
}
