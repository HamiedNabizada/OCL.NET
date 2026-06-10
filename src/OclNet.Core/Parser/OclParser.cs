using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using OclNet.Core.Ast;
using Gen = OclNet.Core.Grammar;

namespace OclNet.Core.Parser;

/// <summary>
/// Public front end: OCL source text → OclNet AST. Wraps the ANTLR-generated
/// lexer/parser, installs a fail-fast error handler (the default ANTLR behaviour
/// recovers and writes to the console — we want a clean <see cref="OclParseException"/>
/// with a source location instead), and hands the parse tree to
/// <see cref="OclAstBuilder"/>.
/// </summary>
public sealed class OclParser
{
    private readonly OclAstBuilder _builder = new();

    /// <summary>Parse the <c>context … inv …</c> invariants of a rule file (ignoring any <c>def:</c> blocks).</summary>
    public IReadOnlyList<OclConstraint> ParseConstraints(string ocl)
    {
        var file = Guard(ocl, p => p.file_());
        return file.unit().Select(u => u.constraint()).Where(c => c is not null).Select(BuildConstraint!).ToList();
    }

    /// <summary>Parse the <c>def:</c> operation definitions of a helper file (ignoring any <c>inv</c> blocks).</summary>
    public IReadOnlyList<OclOperationDef> ParseDefinitions(string ocl)
    {
        var file = Guard(ocl, p => p.file_());
        return file.unit().Select(u => u.operationDef()).Where(d => d is not null).Select(BuildDefinition!).ToList();
    }

    /// <summary>Parse exactly one invariant. Throws if the text holds more or fewer than one.</summary>
    public OclConstraint ParseConstraint(string ocl)
    {
        var constraints = ParseConstraints(ocl);
        if (constraints.Count != 1)
            throw new OclParseException($"expected exactly one constraint, found {constraints.Count}.", SourceLocation.None);
        return constraints[0];
    }

    /// <summary>Parse a bare expression (no <c>context/inv</c> wrapper) — convenient for tests and REPL use.</summary>
    public OclExpression ParseExpression(string ocl) => _builder.Visit(Guard(ocl, p => p.expression()));

    private OclConstraint BuildConstraint(Gen.OclParser.ConstraintContext ctx) =>
        new(ctx.typeName().GetText(), ctx.name?.Text, _builder.Visit(ctx.expression()))
        {
            Location = new SourceLocation(ctx.Start.Line, ctx.Start.Column + 1),
        };

    private OclOperationDef BuildDefinition(Gen.OclParser.OperationDefContext ctx)
    {
        var parameters = ctx.paramList() is { } p
            ? p.param().Select(pp => new OclParameter(pp.IDENT().GetText(), pp.typeName().GetText())).ToList()
            : new List<OclParameter>();

        return new OclOperationDef(
            ContextType: ctx.ctx.GetText(),
            Name: ctx.IDENT().GetText(),
            Parameters: parameters,
            ReturnType: ctx.ret.GetText(),
            Body: _builder.Visit(ctx.expression()))
        {
            Location = new SourceLocation(ctx.Start.Line, ctx.Start.Column + 1),
        };
    }

    /// <summary>Run a parse and convert any ANTLR failure into a clean <see cref="OclParseException"/> with a source location.</summary>
    private static T Guard<T>(string ocl, Func<Gen.OclParser, T> parse)
    {
        try
        {
            return parse(CreateParser(ocl));
        }
        catch (ParseCanceledException pce)
        {
            // BailErrorStrategy wraps the first RecognitionException; surface its location.
            var location = pce.InnerException is RecognitionException { OffendingToken: { } token }
                ? new SourceLocation(token.Line, token.Column + 1)
                : SourceLocation.None;
            throw new OclParseException(pce.InnerException?.Message ?? "syntax error", location);
        }
    }

    private static Gen.OclParser CreateParser(string ocl)
    {
        var lexer = new Gen.OclLexer(new AntlrInputStream(ocl));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(ThrowingErrorListener.Instance); // lexer errors → OclParseException

        var parser = new Gen.OclParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        // Fail fast on the first parser error instead of recovering and building a partial tree.
        parser.ErrorHandler = new BailErrorStrategy();
        return parser;
    }

    /// <summary>Turns the first lexer or parser syntax error into an <see cref="OclParseException"/>.</summary>
    private sealed class ThrowingErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public static readonly ThrowingErrorListener Instance = new();

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Throw(line, charPositionInLine, msg);

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Throw(line, charPositionInLine, msg);

        private static void Throw(int line, int charPositionInLine, string msg) =>
            throw new OclParseException(msg, new SourceLocation(line, charPositionInLine + 1));
    }
}
