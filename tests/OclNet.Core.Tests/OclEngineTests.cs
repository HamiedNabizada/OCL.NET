using OclNet.Core;
using OclNet.Core.Validation;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Core.Tests;

/// <summary>The top-level facade: parse, evaluate against an element, compile and validate.</summary>
public class OclEngineTests
{
    private readonly OclEngine _engine = new();

    [Fact]
    public void Parse_produces_an_ast() => Assert.NotNull(_engine.Parse("1 + 2"));

    [Fact]
    public void Evaluate_binds_self_and_navigates()
    {
        var self = new MockElement("FPD_Process").With("name", OclValue.Str("P"));
        Assert.True(_engine.Evaluate("self.name->notEmpty()", MockMetamodel.Fpd(), self.AsValue()).AsBool());
    }

    [Fact]
    public void Compile_then_validate_reports_the_violator()
    {
        var model = MockMetamodel.Fpd().WithInstances(
            new MockElement("FPD_Process").With("name", OclValue.Str("P1")), // satisfies
            new MockElement("FPD_Process"));                                  // violates: no name
        var rules = new[]
        {
            new OclRuleSpec("D6", ValidationSeverity.Warning, "VDI 3682",
                "context FPD_Process inv ProcessNamed: self.name->notEmpty()"),
        };

        var findings = _engine.Validate(model, _engine.Compile(rules));

        Assert.Equal("D6", Assert.Single(findings).RuleId);
    }

    [Fact]
    public void Validate_convenience_overload_compiles_on_the_fly()
    {
        var model = MockMetamodel.Fpd().WithInstances(new MockElement("FPD_Process").With("name", OclValue.Str("P")));
        var rules = new[]
        {
            new OclRuleSpec("D6", ValidationSeverity.Warning, "VDI 3682",
                "context FPD_Process inv ProcessNamed: self.name->notEmpty()"),
        };

        Assert.Empty(_engine.Validate(model, rules));
    }
}
