using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Validation;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Caex.Tests;

/// <summary>
/// Validator-level coverage on real AML: derived connection navigation, the
/// def:-wired validation path, a mutation-driven violation case, and the
/// "every PURE rule executes" acceptance check against the published rule set.
/// </summary>
public class CaexValidationTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly CAEXDocument _doc = LoadTestAml();
    private readonly CaexMetamodel _mm;

    public CaexValidationTests() => _mm = new CaexMetamodel(_doc);

    private static CAEXDocument LoadTestAml() =>
        CAEXDocument.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "test.aml"));

    private static string ReadSpec(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    private InternalElementType Process(string name) =>
        _doc.CAEXFile.InstanceHierarchy.SelectMany(ih => ih.InternalElement)
            .First(ie => ie.RefBaseSystemUnitPath?.EndsWith("/FPD_Process") == true && ie.Name == name);

    private static InternalElementType Child(InternalElementType parent, string name) =>
        parent.InternalElement.First(ie => ie.Name == name);

    private OclValue Eval(string ocl, InternalElementType self) =>
        _interp.Evaluate(_parser.ParseExpression(ocl), new EvaluationEnvironment(_mm).Bind("self", CaexMetamodel.Wrap(self)));

    // ---- derived connection navigation (G1 shape) ----------------------------------

    [Fact]
    public void Incoming_and_outgoing_connections_resolve()
    {
        var po = Child(Process("TestProcess"), "Step");
        Assert.Equal(2, Eval("self.incomingConnections->size()", po).AsInt());  // Input_to_Step + TR_uses_Step
        Assert.Equal(1, Eval("self.outgoingConnections->size()", po).AsInt());  // Step_to_Output

        // G1: ≥1 incoming Flow and ≥1 outgoing Flow (Usage excluded by the select)
        Assert.True(Eval(
            "self.incomingConnections->select(c | c.oclIsKindOf(FPD_Flow))->size() >= 1 and " +
            "self.outgoingConnections->select(c | c.oclIsKindOf(FPD_Flow))->size() >= 1", po).AsBool());
    }

    // ---- def: wired through the validator ------------------------------------------

    [Fact]
    public void Validator_resolves_def_helpers_no_evaluation_errors()
    {
        var b2 = new OclRuleSpec("VDI3682.PoWithinSystemLimit", ValidationSeverity.Error, "VDI 3682 B2",
            "context FPD_ProcessOperator inv PoWithinSystemLimit: " +
            "let sl: FPD_SystemLimit = self.process.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->first() in " +
            "self.bounds.isWithin(sl.bounds)");
        var helpers = _parser.ParseDefinitions(ReadSpec("vdi3682-helpers.ocl"));

        var findings = new OclValidator().Validate(_mm, new[] { b2 }, helpers);

        // If `isWithin` weren't resolved, every PO would yield an "evaluation error" finding.
        Assert.DoesNotContain(findings, f => f.Message.Contains("error"));
    }

    // ---- violation case via document mutation --------------------------------------

    [Fact]
    public void Removing_the_SystemLimit_triggers_a_cardinality_finding()
    {
        Child(Process("TestProcess"), "SystemLimit_TestProcess").Remove();

        var rule = new OclRuleSpec("VDI3682.SystemLimitCardinality", ValidationSeverity.Error, "VDI 3682 A2",
            "context FPD_Process inv SystemLimitCardinality: " +
            "self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1");

        var findings = new OclValidator().Validate(_mm, new[] { rule });

        Assert.Contains(findings, f => f.RuleId == "VDI3682.SystemLimitCardinality" && f.TargetId == "TestProcess");
    }

    // ---- acceptance: every PURE rule executes against real AML ----------------------

    [Fact]
    public void Every_pure_rule_executes_to_a_definite_value()
    {
        var helpers = new OperationRegistry(_parser.ParseDefinitions(ReadSpec("vdi3682-helpers.ocl")));
        var constraints = _parser.ParseConstraints(ReadSpec("vdi3682-pure-rules.ocl"));

        Assert.Equal(30, constraints.Count);

        foreach (var constraint in constraints)
            foreach (var self in _mm.InstancesOf(constraint.ContextType))
            {
                var env = new EvaluationEnvironment(_mm, helpers).Bind("self", self);
                var result = _interp.Evaluate(constraint.Body, env); // must not throw
                Assert.True(result.Kind is OclKind.Boolean or OclKind.Invalid,
                    $"{constraint.Name} on '{_mm.IdOf(self)}' produced {result.Kind}");
            }
    }
}
