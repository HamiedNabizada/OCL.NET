using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Validation;
using Xunit;

namespace OclNet.Caex.Tests;

/// <summary>
/// Milestone-4 acceptance: the <see cref="OclValidator"/> runs IE-context rules over
/// the real <c>test.aml</c> and produces exactly the findings the hard-coded
/// cardinality rules would. In that file only process "Step" violates the
/// ≥2-states rule (it has a single Product); every other cardinality rule holds.
/// </summary>
public class OclValidatorTests
{
    private readonly CaexMetamodel _model = new(LoadTestAml());
    private readonly OclValidator _validator = new();

    private static CAEXDocument LoadTestAml() =>
        CAEXDocument.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "test.aml"));

    private static OclRuleSpec Rule(string id, ValidationSeverity severity, string ocl) => new(id, severity, "VDI 3682", ocl);

    private static readonly OclRuleSpec SystemLimitCardinality = Rule(
        "VDI3682.SystemLimitCardinality", ValidationSeverity.Error,
        "context FPD_Process inv SystemLimitCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1");

    private static readonly OclRuleSpec StateCardinality = Rule(
        "VDI3682.StateCardinality", ValidationSeverity.Warning,
        "context FPD_Process inv StateMinimumCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_State))->size() >= 2");

    private static readonly OclRuleSpec ProcessOperatorCardinality = Rule(
        "VDI3682.ProcessOperatorCardinality", ValidationSeverity.Warning,
        "context FPD_Process inv ProcessOperatorMinimumCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_ProcessOperator))->size() >= 1");

    [Fact]
    public void Cardinality_rules_report_only_the_state_shortfall_on_Step()
    {
        var findings = _validator.Validate(_model,
            new[] { SystemLimitCardinality, StateCardinality, ProcessOperatorCardinality });

        var finding = Assert.Single(findings);
        Assert.Equal("VDI3682.StateCardinality", finding.RuleId);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("Step", finding.TargetId);
    }

    [Fact]
    public void ProcessOperatorNamed_holds_for_every_operator()
    {
        // D3: longName/shortName empty in test.aml, but each PO has an element Name.
        var rule = Rule("VDI3682.ProcessOperatorIdentification", ValidationSeverity.Info,
            "context FPD_ProcessOperator inv ProcessOperatorNamed: " +
            "(self.identification.longName->notEmpty() and self.identification.longName->size() > 0) or " +
            "(self.identification.shortName->notEmpty() and self.identification.shortName->size() > 0) or " +
            "(self.name->notEmpty())");

        Assert.Empty(_validator.Validate(_model, new[] { rule }));
    }

    [Fact]
    public void Parse_error_in_rule_becomes_a_diagnostic_finding()
    {
        var findings = _validator.Validate(_model,
            new[] { Rule("Broken", ValidationSeverity.Error, "context FPD_Process inv X: self.") });

        Assert.Contains("parse error", Assert.Single(findings).Message);
    }

    [Fact]
    public void Unsupported_operation_is_isolated_as_a_diagnostic()
    {
        // isUnique parses but is not a Phase-1 iterator — must not crash the pass.
        var findings = _validator.Validate(_model,
            new[] { Rule("Unsup", ValidationSeverity.Error, "context FPD_Process inv X: self.containedElement->isUnique(e | e)") });

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Contains("error", f.Message));
    }
}
