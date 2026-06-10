using System.Globalization;
using System.Text;
using Antlr4.Runtime;
using OclNet.Core.Ast;
using OclNet.Core.Values;
using Gen = OclNet.Core.Grammar;

namespace OclNet.Core.Parser;

/// <summary>
/// Lowers the ANTLR parse tree into OclNet's own <see cref="OclExpression"/> AST.
/// Visitor returns <see cref="OclExpression"/> for every expression alternative;
/// the constraint/file wrappers are assembled by <see cref="OclParser"/>, so this
/// visitor stays single-purpose. Keeping our AST separate from ANTLR's tree means
/// the interpreter (and later passes) run against a stable, hand-owned shape.
/// </summary>
internal sealed class OclAstBuilder : Gen.OclBaseVisitor<OclExpression>
{
    // ---- let / if ------------------------------------------------------------------

    public override OclExpression VisitLetExpr(Gen.OclParser.LetExprContext ctx) =>
        new LetExpr(ctx.IDENT().GetText(), ctx.typeName()?.GetText(),
            Visit(ctx.expression(0)), Visit(ctx.expression(1))) { Location = Loc(ctx) };

    public override OclExpression VisitIfExpr(Gen.OclParser.IfExprContext ctx) =>
        new IfExpr(Visit(ctx.expression(0)), Visit(ctx.expression(1)), Visit(ctx.expression(2))) { Location = Loc(ctx) };

    // ---- navigation & calls --------------------------------------------------------

    public override OclExpression VisitDotNav(Gen.OclParser.DotNavContext ctx) =>
        new NavigationExpr(Visit(ctx.expression()), ctx.IDENT().GetText()) { Location = Loc(ctx) };

    public override OclExpression VisitDotOpCall(Gen.OclParser.DotOpCallContext ctx) =>
        new OperationCallExpr(Visit(ctx.expression()), ctx.IDENT().GetText(), Args(ctx.argList()), CallStyle.Dot) { Location = Loc(ctx) };

    public override OclExpression VisitArrowCall(Gen.OclParser.ArrowCallContext ctx)
    {
        var source = Visit(ctx.expression());
        var name = ctx.IDENT().GetText();

        switch (ctx.iterBody())
        {
            case Gen.OclParser.IteratorBodyContext it:
                var vars = it.iterVars().IDENT().Select(t => t.GetText()).ToList();
                return new IteratorExpr(source, name, vars, Visit(it.expression())) { Location = Loc(ctx) };
            case Gen.OclParser.SimpleArgsContext sa:
                return new OperationCallExpr(source, name, Args(sa.argList()), CallStyle.Arrow) { Location = Loc(ctx) };
            default: // ->size(), ->asSet(), … — nullary
                return new OperationCallExpr(source, name, Array.Empty<OclExpression>(), CallStyle.Arrow) { Location = Loc(ctx) };
        }
    }

    // ---- operators -----------------------------------------------------------------

    public override OclExpression VisitUnaryExpr(Gen.OclParser.UnaryExprContext ctx) =>
        new UnaryExpr(ctx.op.Text == "not" ? UnaryOperator.Not : UnaryOperator.Minus, Visit(ctx.expression())) { Location = Loc(ctx) };

    public override OclExpression VisitMulExpr(Gen.OclParser.MulExprContext ctx) => Bin(ctx.op.Text, ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitAddExpr(Gen.OclParser.AddExprContext ctx) => Bin(ctx.op.Text, ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitRelExpr(Gen.OclParser.RelExprContext ctx) => Bin(ctx.op.Text, ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitEqExpr(Gen.OclParser.EqExprContext ctx) => Bin(ctx.op.Text, ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitOrExpr(Gen.OclParser.OrExprContext ctx) => Bin(ctx.op.Text, ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitAndExpr(Gen.OclParser.AndExprContext ctx) => Bin("and", ctx, ctx.expression(0), ctx.expression(1));
    public override OclExpression VisitImpliesExpr(Gen.OclParser.ImpliesExprContext ctx) => Bin("implies", ctx, ctx.expression(0), ctx.expression(1));

    // ---- primaries -----------------------------------------------------------------

    public override OclExpression VisitPrimaryExpr(Gen.OclParser.PrimaryExprContext ctx) => Visit(ctx.primary());
    public override OclExpression VisitParenPrimary(Gen.OclParser.ParenPrimaryContext ctx) => Visit(ctx.expression());
    public override OclExpression VisitSelfPrimary(Gen.OclParser.SelfPrimaryContext ctx) => new VariableExpr("self") { Location = Loc(ctx) };
    public override OclExpression VisitNamePrimary(Gen.OclParser.NamePrimaryContext ctx) => new VariableExpr(ctx.IDENT().GetText()) { Location = Loc(ctx) };
    public override OclExpression VisitLiteralPrimary(Gen.OclParser.LiteralPrimaryContext ctx) => Visit(ctx.literal());

    public override OclExpression VisitCollectionLiteral(Gen.OclParser.CollectionLiteralContext ctx) =>
        new CollectionLiteralExpr(ctx.collectionKind().GetText(), ctx.expression().Select(Visit).ToList()) { Location = Loc(ctx) };

    // ---- literals ------------------------------------------------------------------

    public override OclExpression VisitIntLiteral(Gen.OclParser.IntLiteralContext ctx)
    {
        var text = ctx.INT().GetText();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new OclParseException($"integer literal '{text}' is out of range", Loc(ctx));
        return new LiteralExpr(OclValue.Int(value)) { Location = Loc(ctx) };
    }

    public override OclExpression VisitRealLiteral(Gen.OclParser.RealLiteralContext ctx)
    {
        var text = ctx.REAL().GetText();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new OclParseException($"real literal '{text}' is out of range", Loc(ctx));
        return new LiteralExpr(OclValue.Real(value)) { Location = Loc(ctx) };
    }

    public override OclExpression VisitStringLiteral(Gen.OclParser.StringLiteralContext ctx) =>
        new LiteralExpr(OclValue.Str(Unescape(ctx.STRING().GetText()))) { Location = Loc(ctx) };

    public override OclExpression VisitTrueLiteral(Gen.OclParser.TrueLiteralContext ctx) => new LiteralExpr(OclValue.True) { Location = Loc(ctx) };
    public override OclExpression VisitFalseLiteral(Gen.OclParser.FalseLiteralContext ctx) => new LiteralExpr(OclValue.False) { Location = Loc(ctx) };

    // ---- helpers -------------------------------------------------------------------

    private OclExpression Bin(string op, ParserRuleContext ctx, Gen.OclParser.ExpressionContext l, Gen.OclParser.ExpressionContext r) =>
        new BinaryExpr(MapBinary(op), Visit(l), Visit(r)) { Location = Loc(ctx) };

    private IReadOnlyList<OclExpression> Args(Gen.OclParser.ArgListContext? list) =>
        list is null ? Array.Empty<OclExpression>() : list.expression().Select(Visit).ToList();

    private static SourceLocation Loc(ParserRuleContext ctx) => new(ctx.Start.Line, ctx.Start.Column + 1);

    private static BinaryOperator MapBinary(string op) => op switch
    {
        "and" => BinaryOperator.And,
        "or" => BinaryOperator.Or,
        "xor" => BinaryOperator.Xor,
        "implies" => BinaryOperator.Implies,
        "=" => BinaryOperator.Equal,
        "<>" => BinaryOperator.NotEqual,
        "<" => BinaryOperator.Less,
        "<=" => BinaryOperator.LessOrEqual,
        ">" => BinaryOperator.Greater,
        ">=" => BinaryOperator.GreaterOrEqual,
        "+" => BinaryOperator.Add,
        "-" => BinaryOperator.Subtract,
        "*" => BinaryOperator.Multiply,
        "/" => BinaryOperator.Divide,
        "mod" => BinaryOperator.Modulo,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown binary operator."),
    };

    private static string Unescape(string quoted)
    {
        var inner = quoted.Substring(1, quoted.Length - 2);
        if (!inner.Contains('\\')) return inner;

        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                var next = inner[++i];
                sb.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => next });
            }
            else sb.Append(inner[i]);
        }
        return sb.ToString();
    }
}
