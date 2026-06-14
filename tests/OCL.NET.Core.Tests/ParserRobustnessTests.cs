using OCL.NET.Core.Ast;
using OCL.NET.Core.Parser;
using Xunit;

namespace OCL.NET.Core.Tests;

/// <summary>
/// Parser must turn every malformed input into a clean <see cref="OclParseException"/>
/// — never a raw CLR exception that would escape the validator's per-rule isolation.
/// Plus a regression for the let/if precedence bug.
/// </summary>
public class ParserRobustnessTests
{
    private readonly OclParser _parser = new();

    [Theory]
    [InlineData("99999999999999999999999999")]      // integer literal > Int64 (was OverflowException)
    [InlineData("self.x->")]                          // truncated
    [InlineData("(1 + 2")]                            // unbalanced paren
    [InlineData("@#$%")]                              // garbage
    [InlineData("")]                                  // empty
    [InlineData("let x = 1 in")]                       // dangling let
    public void Malformed_input_throws_only_OclParseException(string ocl)
    {
        Assert.Throws<OclParseException>(() => _parser.ParseExpression(ocl));
    }

    [Fact]
    public void Let_body_extends_to_the_right()
    {
        // Regression: `let x = 1 in x + 2` must have the whole `x + 2` as its body,
        // not parse as `(let x = 1 in x) + 2`.
        var let = Assert.IsType<LetExpr>(_parser.ParseExpression("let x = 1 in x + 2"));
        Assert.IsType<BinaryExpr>(let.Body);
    }

    [Fact]
    public void Parse_error_carries_a_location()
    {
        var ex = Assert.Throws<OclParseException>(() => _parser.ParseExpression("self.x->select("));
        Assert.NotEqual(SourceLocation.None, ex.Location);
    }
}
