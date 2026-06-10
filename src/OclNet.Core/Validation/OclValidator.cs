using OclNet.Core.Ast;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Values;

namespace OclNet.Core.Validation;

/// <summary>
/// Runs OCL rules over a model and reports violations. Rules are parsed once into a
/// <see cref="CompiledRuleSet"/> (with their <c>def:</c> helper operations); for each
/// rule the validator enumerates the instances of its <c>context</c> type, evaluates
/// the body against each, and records a finding wherever the body is not <c>true</c>
/// (false, or — per OCL's fail-safe default — undefined).
///
/// A rule that fails to parse, or an instance that throws during evaluation (e.g. an
/// operation outside the supported subset), yields a diagnostic finding rather than
/// derailing the whole pass — mirroring the hard-coded validator's per-rule isolation.
/// </summary>
public sealed class OclValidator
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interpreter = new();

    /// <summary>Parse a rule set once (with optional <c>def:</c> helper operations) for repeated validation.</summary>
    public CompiledRuleSet Compile(IEnumerable<OclRuleSpec> rules, IEnumerable<OclOperationDef>? definitions = null)
    {
        var compiled = new List<CompiledRule>();
        foreach (var rule in rules)
        {
            try
            {
                compiled.Add(new CompiledRule(rule.Id, rule.Severity, _parser.ParseConstraint(rule.OclConstraint), null));
            }
            catch (OclParseException ex)
            {
                compiled.Add(new CompiledRule(rule.Id, rule.Severity, null, ex));
            }
        }
        return new CompiledRuleSet(compiled, new OperationRegistry(definitions ?? Array.Empty<OclOperationDef>()));
    }

    /// <summary>Validate using a pre-compiled rule set (no re-parsing).</summary>
    public List<ValidationFinding> Validate(IOclModel model, CompiledRuleSet ruleSet)
    {
        var findings = new List<ValidationFinding>();
        foreach (var rule in ruleSet.Rules)
            ValidateRule(model, rule, ruleSet.Operations, findings);
        return findings;
    }

    /// <summary>Convenience: compile and validate in one call. Parses each invocation — prefer <see cref="Compile"/> for repeated use.</summary>
    public List<ValidationFinding> Validate(IOclModel model, IEnumerable<OclRuleSpec> rules, IEnumerable<OclOperationDef>? definitions = null) =>
        Validate(model, Compile(rules, definitions));

    private void ValidateRule(IOclModel model, CompiledRule rule, OperationRegistry operations, List<ValidationFinding> findings)
    {
        if (rule.Constraint is null)
        {
            findings.Add(new ValidationFinding(rule.Id, rule.Severity, $"[{rule.Id}] OCL parse error: {rule.ParseError?.Message}"));
            return;
        }

        var constraint = rule.Constraint;
        foreach (var instance in model.InstancesOf(constraint.ContextType))
        {
            var id = model.IdOf(instance);
            OclValue result;
            try
            {
                var env = new EvaluationEnvironment(model, operations).Bind("self", instance);
                result = _interpreter.Evaluate(constraint.Body, env);
            }
            catch (Exception ex)
            {
                findings.Add(new ValidationFinding(rule.Id, rule.Severity,
                    $"[{rule.Id}] evaluation error on {constraint.ContextType} '{id}': {ex.Message}", id));
                continue;
            }

            // Only an explicit `true` satisfies an invariant; false or undefined is a violation.
            if (result.Kind != OclKind.Boolean || !result.AsBool())
                findings.Add(new ValidationFinding(rule.Id, rule.Severity,
                    $"[{rule.Id}] {constraint.ContextType} '{id}' violates {constraint.Name ?? rule.Id}.", id));
        }
    }
}
