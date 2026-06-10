using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Core.Tests;

/// <summary>
/// String operations (dot-style value ops vs arrow-style collection coercion) and
/// the equality fix for <c>invalid</c>. Regression cover for the review findings
/// "String.size() returned 1" and "invalid = invalid returned true".
/// </summary>
public class StringAndEqualityTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();

    private OclValue E(string ocl) => _interp.Evaluate(_parser.ParseExpression(ocl), new EvaluationEnvironment());

    [Fact] public void Dot_size_is_string_length() => Assert.Equal(3, E("'abc'.size()").AsInt());

    [Fact] public void Arrow_size_treats_string_as_singleton_collection() => Assert.Equal(1, E("'abc'->size()").AsInt());

    [Fact] public void Substring_is_one_based_inclusive() => Assert.Equal("bc", E("'abcd'.substring(2, 3)").AsString());

    [Fact] public void Case_conversion() => Assert.Equal("ABC", E("'abc'.toUpperCase()").AsString());

    [Fact] public void Concat_joins() => Assert.Equal("ab", E("'a'.concat('b')").AsString());

    [Fact] public void IndexOf_is_one_based() => Assert.Equal(2, E("'abc'.indexOf('b')").AsInt());

    [Theory]
    [InlineData("'abc'->matches('a.*')", true)]   // arrow form (catalogue style)
    [InlineData("'abc'.matches('a.*')", true)]    // dot form
    [InlineData("'abc'.matches('x.*')", false)]
    public void Matches_full_string(string ocl, bool expected) => Assert.Equal(expected, E(ocl).AsBool());

    [Fact] public void Invalid_equals_invalid_is_invalid() => Assert.Equal(OclKind.Invalid, E("1 / 0 = 1 / 0").Kind);

    [Fact] public void Invalid_notequal_is_invalid() => Assert.Equal(OclKind.Invalid, E("1 / 0 <> 2").Kind);
}
