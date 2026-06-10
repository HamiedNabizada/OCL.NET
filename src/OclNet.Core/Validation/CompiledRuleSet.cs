using OclNet.Core.Ast;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;

namespace OclNet.Core.Validation;

/// <summary>
/// A single rule parsed once. A parse failure is captured (not thrown) so it can be
/// reported as a finding at validation time rather than aborting compilation.
/// </summary>
public sealed record CompiledRule(string Id, ValidationSeverity Severity, OclConstraint? Constraint, OclParseException? ParseError);

/// <summary>
/// A set of rules parsed once together with their helper operations, ready to be
/// run against many models. Build it once via <see cref="OclValidator.Compile"/> and
/// reuse it — this is the AST-caching that keeps repeated validation cheap.
/// </summary>
public sealed class CompiledRuleSet
{
    public IReadOnlyList<CompiledRule> Rules { get; }
    public OperationRegistry Operations { get; }

    public CompiledRuleSet(IReadOnlyList<CompiledRule> rules, OperationRegistry operations)
    {
        Rules = rules;
        Operations = operations;
    }
}
