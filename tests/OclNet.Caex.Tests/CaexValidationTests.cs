using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Validation;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Caex.Tests;

/// <summary>
/// Validator-level acceptance on real AML. The headline test runs ALL published
/// PURE rules through the validator against <c>test.aml</c> and checks a concrete
/// findings baseline — it asserts per rule that instances exist (no vacuous pass),
/// that no rule errors out, and that known-good/known-bad rules report exactly as
/// expected. Plus: unknown-context diagnostics, unclassified-element surfacing,
/// role-based typing, and a mutation-driven violation.
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

    /// <summary>Split a rule file into per-invariant specs (id = invariant name).</summary>
    private List<OclRuleSpec> LoadRules(string specFile)
    {
        var blocks = ReadSpec(specFile).Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Where(b => b.StartsWith("context"))
            .ToList();
        return blocks
            .Select(b => new OclRuleSpec(_parser.ParseConstraint(b).Name ?? "?", ValidationSeverity.Warning, "VDI 3682", b))
            .ToList();
    }

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
        Assert.DoesNotContain(findings, f => f.Message.Contains("evaluation error"));
    }

    // ---- violation case via document mutation --------------------------------------

    [Fact]
    public void Removing_the_SystemLimit_triggers_a_cardinality_finding()
    {
        var process = Process("TestProcess");
        Child(process, "SystemLimit_TestProcess").Remove();

        var rule = new OclRuleSpec("VDI3682.SystemLimitCardinality", ValidationSeverity.Error, "VDI 3682 A2",
            "context FPD_Process inv SystemLimitCardinality: " +
            "self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1");

        var findings = new OclValidator().Validate(_mm, new[] { rule });

        Assert.Contains(findings, f => f.RuleId == "VDI3682.SystemLimitCardinality" && f.TargetId == process.ID);
    }

    // ---- acceptance: ALL published PURE rules against real AML ----------------------

    [Fact]
    public void All_pure_rules_validate_with_expected_baseline()
    {
        var rules = LoadRules("vdi3682-pure-rules.ocl");
        Assert.Equal(26, rules.Count);

        // No vacuous pass: every rule's context type has at least one instance here.
        foreach (var contextType in rules.Select(r => _parser.ParseConstraint(r.OclConstraint).ContextType).Distinct())
            Assert.True(_mm.InstancesOf(contextType).Any(), $"context '{contextType}' has no instances in test.aml — rule would pass vacuously");

        var validator = new OclValidator();
        var helpers = _parser.ParseDefinitions(ReadSpec("vdi3682-helpers.ocl"));
        var compiled = validator.Compile(rules, helpers);
        Assert.All(compiled.Rules, r => Assert.Null(r.ParseError));

        var findings = validator.Validate(_mm, compiled);

        // Every rule evaluated cleanly — no engine errors, no unknown context types.
        Assert.DoesNotContain(findings, f => f.Message.Contains("evaluation error"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("unknown to the model binding"));

        // Concrete baseline on test.aml:
        var stepProcessId = Process("Step").ID;
        Assert.Contains(findings, f => f.RuleId == "StateMinimumCardinality" && f.TargetId == stepProcessId); // "Step" has 1 state
        Assert.DoesNotContain(findings, f => f.RuleId == "SystemLimitCardinality");                            // both processes have exactly 1 SL
        Assert.DoesNotContain(findings, f => f.RuleId == "ProcessOperatorMinimumCardinality");
        Assert.DoesNotContain(findings, f => f.RuleId == "ProjectMinimumProcess");                             // 2 processes exist
        Assert.Contains(findings, f => f.RuleId == "LongNameMandatory");                                       // longName empty throughout test.aml
        // D1 guards empty idents (missing mandatory value is a D-category issue, not a
        // duplicate) and exempts decomposition pairs — test.aml's empty uniqueIdents
        // therefore no longer collide. The positive D1 case is covered by
        // OclEngine/SideBySide duplicate-ident tests.
        Assert.DoesNotContain(findings, f => f.RuleId == "UniqueIdentifiers");
    }

    // ---- silent-pass killers --------------------------------------------------------

    [Fact]
    public void Unknown_context_type_is_reported_as_a_diagnostic()
    {
        // FPD_Characteristic is not (yet) part of the binding's vocabulary — the rule
        // must surface as a diagnostic, never as a silent pass.
        var rule = new OclRuleSpec("E1", ValidationSeverity.Warning, "VDI 3682 E1",
            "context FPD_Characteristic inv CharacteristicCategoryPresent: self.category->notEmpty()");

        var finding = Assert.Single(new OclValidator().Validate(_mm, new[] { rule }));
        Assert.Contains("unknown to the model binding", finding.Message);
    }

    [Fact]
    public void Untyped_elements_are_surfaced_not_hidden()
    {
        var po = Child(Process("TestProcess"), "Step");
        po.RefBaseSystemUnitPath = "SomeOtherLib/Unrelated";
        foreach (var rr in po.RoleRequirements.ToList()) rr.Remove();

        var unclassified = _mm.UnclassifiedElements().ToList();
        Assert.Contains(unclassified, ie => ie.ID == po.ID);
    }

    [Fact]
    public void Role_typed_elements_resolve_without_suc_path()
    {
        // A foreign-authored AML may type elements via RoleRequirements only.
        var po = Child(Process("TestProcess"), "Step");
        po.RefBaseSystemUnitPath = "";

        Assert.True(Eval("self.oclIsKindOf(FPD_ProcessOperator)", po).AsBool());
    }

    [Fact]
    public void Project_rules_run_against_a_real_instance()
    {
        // The project scope is an enumerable instance — D1/A1 can actually fire.
        var instances = _mm.InstancesOf("FPD_Project").ToList();
        var project = Assert.Single(instances);
        Assert.Equal("FPD_Project", _mm.TypeOf(project.AsObject()));

        var env = new EvaluationEnvironment(_mm).Bind("self", project);
        var result = _interp.Evaluate(_parser.ParseExpression("self.process->size()"), env);
        Assert.Equal(2, result.AsInt());
    }
}
