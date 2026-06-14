using Aml.Engine.CAEX;
using OCL.NET.Caex;
using OCL.NET.Core.Ast;
using OCL.NET.Core.Interpreter;
using OCL.NET.Core.Parser;
using OCL.NET.Core.Values;
using Xunit;

namespace OCL.NET.Caex.Tests;

/// <summary>
/// Phase-2 end-to-end on real AML: a spatial rule (category B) using the published
/// geometry helpers runs against <c>test.aml</c>. Proves the whole chain — flat
/// bounds bridging (<c>self.bounds.x</c> → <c>ViewInformation/position/x</c>),
/// numeric attributes, the <c>def:</c> helper <c>isWithin</c>, plus let / select /
/// first / implicit-self navigation — works together on a serialized model.
/// </summary>
public class BoundsRuleTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly CAEXDocument _doc = LoadTestAml();
    private readonly CaexMetamodel _mm;
    private readonly OperationRegistry _ops;

    public BoundsRuleTests()
    {
        _mm = new CaexMetamodel(_doc);
        var helpers = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-helpers.ocl"));
        _ops = new OperationRegistry(_parser.ParseDefinitions(helpers));
    }

    private static CAEXDocument LoadTestAml() =>
        CAEXDocument.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "test.aml"));

    private InternalElementType Process(string name) =>
        _doc.CAEXFile.InstanceHierarchy.SelectMany(ih => ih.InternalElement)
            .First(ie => ie.RefBaseSystemUnitPath?.EndsWith("/FPD_Process") == true && ie.Name == name);

    private static InternalElementType Child(InternalElementType parent, string name) =>
        parent.InternalElement.First(ie => ie.Name == name);

    private OclValue Eval(string ocl, InternalElementType self)
    {
        var env = new EvaluationEnvironment(_mm, _ops).Bind("self", CaexMetamodel.Wrap(self));
        return _interp.Evaluate(_parser.ParseExpression(ocl), env);
    }

    [Fact]
    public void Flat_bounds_access_reaches_nested_coordinates()
    {
        var po = Child(Process("TestProcess"), "Step");
        Assert.Equal(OclKind.Real, Eval("self.bounds.x", po).Kind);
        Assert.Equal(OclKind.Real, Eval("self.bounds.width", po).Kind);
    }

    [Fact]
    public void A_box_is_always_within_itself()
    {
        // Deterministic regardless of coordinates: every bound contains itself.
        var po = Child(Process("TestProcess"), "Step");
        Assert.True(Eval("self.bounds.isWithin(self.bounds)", po).AsBool());
    }

    [Fact]
    public void ProcessOperatorWithinSystemLimit_evaluates_live_on_caex()
    {
        // B2 — the full chain: typed let + process navigation + select/first + flat
        // bounds bridging + the def: helper isWithin, all against the serialized model.
        var constraint = _parser.ParseConstraint(
            "context FPD_ProcessOperator inv ProcessOperatorWithinSystemLimit: " +
            "let sl: FPD_SystemLimit = self.process.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->first() in " +
            "self.bounds.isWithin(sl.bounds)");

        var po = Child(Process("TestProcess"), "Step");
        var env = new EvaluationEnvironment(_mm, _ops).Bind("self", CaexMetamodel.Wrap(po));
        var result = _interp.Evaluate(constraint.Body, env);

        // A definite Boolean (not Invalid) proves every navigation and the helper
        // resolved; and the PO sits inside its SystemLimit, so the rule holds.
        Assert.Equal(OclKind.Boolean, result.Kind);
        Assert.True(result.AsBool());
    }
}
