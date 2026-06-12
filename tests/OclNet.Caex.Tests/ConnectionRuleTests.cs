using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Validation;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Caex.Tests;

/// <summary>
/// Connection-context rules (self = InternalLink). Exercised on the well-formed,
/// within-process links of "TestProcess": <c>Input_to_Step</c> (Flow State→PO),
/// <c>Step_to_Output</c> (Flow PO→State), <c>TR_uses_Step</c> (Usage PO↔TR).
/// Covers the catalogue's C1/C2/C3/C4/C6 and G3.
/// </summary>
public class ConnectionRuleTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly CAEXDocument _doc = LoadTestAml();
    private readonly CaexMetamodel _mm;

    public ConnectionRuleTests() => _mm = new CaexMetamodel(_doc);

    private static CAEXDocument LoadTestAml() =>
        CAEXDocument.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "test.aml"));

    private InternalElementType Process(string name) =>
        _doc.CAEXFile.InstanceHierarchy
            .SelectMany(ih => ih.InternalElement)
            .First(ie => ie.RefBaseSystemUnitPath?.EndsWith("/FPD_Process") == true && ie.Name == name);

    private InternalLinkType Link(string process, string name) =>
        Process(process).InternalLink.First(l => l.Name == name);

    private bool EvalLink(string ocl, InternalLinkType self)
    {
        var expr = _parser.ParseExpression(ocl);
        var env = new EvaluationEnvironment(_mm).Bind("self", CaexMetamodel.WrapLink(self));
        return _interp.Evaluate(expr, env).AsBool();
    }

    private string TypeOfLink(string ocl, InternalLinkType self)
    {
        var env = new EvaluationEnvironment(_mm).Bind("self", CaexMetamodel.WrapLink(self));
        return _interp.Evaluate(_parser.ParseExpression(ocl), env).AsString();
    }

    [Fact] // C1
    public void Flow_endpoints_are_State_and_ProcessOperator()
    {
        const string ocl =
            "(source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_ProcessOperator)) or " +
            "(source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_State))";
        Assert.True(EvalLink(ocl, Link("TestProcess", "Input_to_Step")));
        Assert.True(EvalLink(ocl, Link("TestProcess", "Step_to_Output")));
    }

    [Fact] // C2 / C3
    public void Flow_is_neither_state_to_state_nor_po_to_po()
    {
        var link = Link("TestProcess", "Input_to_Step");
        Assert.True(EvalLink("not (source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_State))", link));
        Assert.True(EvalLink("not (source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_ProcessOperator))", link));
    }

    [Fact] // C4
    public void Usage_connects_ProcessOperator_and_TechnicalResource()
    {
        const string ocl =
            "(source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_TechnicalResource)) or " +
            "(source.oclIsKindOf(FPD_TechnicalResource) and target.oclIsKindOf(FPD_ProcessOperator))";
        Assert.True(EvalLink(ocl, Link("TestProcess", "TR_uses_Step")));
    }

    [Fact] // C6
    public void Flow_is_directed_via_typed_interfaces()
    {
        Assert.True(EvalLink(
            "self.sourceInterface.oclIsKindOf(FPD_FlowOut) and self.targetInterface.oclIsKindOf(FPD_FlowIn)",
            Link("TestProcess", "Input_to_Step")));
    }

    [Fact] // G3
    public void Connection_has_no_self_reference()
    {
        Assert.True(EvalLink("self.source <> self.target", Link("TestProcess", "Input_to_Step")));
    }

    [Fact]
    public void Connection_type_is_derived_from_interface_paths()
    {
        Assert.Equal("FPD_Flow", TypeOfLink("self.oclType()", Link("TestProcess", "Input_to_Step")));
        Assert.Equal("FPD_Usage", TypeOfLink("self.oclType()", Link("TestProcess", "TR_uses_Step")));
    }

    [Fact]
    public void Validator_enumerates_Usage_links_and_finds_no_violation()
    {
        // Guard against a vacuous pass: the Usage link must actually be enumerable.
        Assert.Single(_mm.InstancesOf("FPD_Usage"));

        // FPD_Usage context selects only TR_uses_Step; its PO↔TR typing holds.
        var rule = new OclRuleSpec("VDI3682.UsageEndpoints", ValidationSeverity.Error, "VDI 3682 C4",
            "context FPD_Usage inv UsageEndpointsTyped: " +
            "(source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_TechnicalResource)) or " +
            "(source.oclIsKindOf(FPD_TechnicalResource) and target.oclIsKindOf(FPD_ProcessOperator))");

        Assert.Empty(new OclValidator().Validate(_mm, new[] { rule }));
    }

    [Fact] // negative case: a rewired link must VIOLATE the typing rules
    public void Rewired_state_to_state_flow_violates_endpoint_typing()
    {
        // Point Input_to_Step's target at Output's FlowIn → the flow runs State→State.
        var broken = Link("TestProcess", "Input_to_Step");
        broken.RefPartnerSideB = Link("TestProcess", "Step_to_Output").RefPartnerSideB;

        const string c1 =
            "(source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_ProcessOperator)) or " +
            "(source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_State))";
        Assert.False(EvalLink(c1, broken));                                                              // C1 violated
        Assert.False(EvalLink("not (source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_State))", broken)); // C2 violated
    }
}
