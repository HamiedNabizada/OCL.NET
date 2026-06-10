using OclNet.Core.Interpreter;
using OclNet.Core.Metamodel;
using OclNet.Core.Parser;
using OclNet.Core.Validation;
using OclNet.Core.Values;

namespace OclNet.Core;

/// <summary>
/// Convenience entry point that ties parser, interpreter and validator together for
/// the common cases. Lower layers stay available for advanced use; this is the
/// "embed it in three lines" facade:
/// <code>
/// var engine = new OclEngine();
/// var findings = engine.Validate(model, rules);          // model is an IOclModel binding
/// var ok = engine.Evaluate("self.x->size() = 1", model, self).AsBool();
/// </code>
/// </summary>
public sealed class OclEngine
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interpreter = new();
    private readonly OclValidator _validator = new();

    /// <summary>Parse a bare OCL expression into an AST.</summary>
    public Ast.OclExpression Parse(string ocl) => _parser.ParseExpression(ocl);

    /// <summary>Parse and evaluate an expression against a model element bound as <c>self</c>.</summary>
    public OclValue Evaluate(string ocl, IOclMetamodel model, OclValue self) =>
        _interpreter.Evaluate(_parser.ParseExpression(ocl), new EvaluationEnvironment(model).Bind("self", self));

    /// <summary>Compile a rule set once (with optional <c>def:</c> helpers) for repeated validation.</summary>
    public CompiledRuleSet Compile(IEnumerable<OclRuleSpec> rules, IEnumerable<Ast.OclOperationDef>? definitions = null) =>
        _validator.Compile(rules, definitions);

    /// <summary>Validate a model against a pre-compiled rule set.</summary>
    public List<ValidationFinding> Validate(IOclModel model, CompiledRuleSet ruleSet) =>
        _validator.Validate(model, ruleSet);

    /// <summary>Validate a model against rules, compiling them on the fly (convenient for one-offs).</summary>
    public List<ValidationFinding> Validate(IOclModel model, IEnumerable<OclRuleSpec> rules, IEnumerable<Ast.OclOperationDef>? definitions = null) =>
        _validator.Validate(model, rules, definitions);
}
