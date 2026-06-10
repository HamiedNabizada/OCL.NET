using OclNet.Core.Ast;
using OclNet.Core.Interpreter;
using OclNet.Core.Parser;
using OclNet.Core.Values;
using Xunit;

namespace OclNet.Core.Tests;

/// <summary>
/// Phase-2 <c>def:</c> mechanism: load the published geometry helper library
/// (<c>spec/vdi3682-helpers.ocl</c>) and evaluate constraints that call its
/// operations against (mock) Bounds. Proves the helper strategy — the catalogue's
/// spatial predicates run as plain OCL, no native code.
/// </summary>
public class OperationDefTests
{
    private readonly OclParser _parser = new();
    private readonly OclInterpreter _interp = new();
    private readonly MockMetamodel _mm = MockMetamodel.Fpd();
    private readonly IReadOnlyList<OclOperationDef> _defs;
    private readonly OperationRegistry _ops;

    public OperationDefTests()
    {
        var helpers = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-helpers.ocl"));
        _defs = _parser.ParseDefinitions(helpers);
        _ops = new OperationRegistry(_defs);
    }

    private static MockElement Bounds(double x, double y, double w, double h) =>
        new MockElement("Bounds")
            .With("x", OclValue.Real(x)).With("y", OclValue.Real(y))
            .With("width", OclValue.Real(w)).With("height", OclValue.Real(h));

    private bool Eval(string ocl, MockElement self, MockElement other)
    {
        var env = new EvaluationEnvironment(_mm, _ops).Bind("self", self.AsValue()).Bind("other", other.AsValue());
        return _interp.Evaluate(_parser.ParseExpression(ocl), env).AsBool();
    }

    [Fact]
    public void Helper_file_defines_the_geometry_operations()
    {
        Assert.Equal(5, _defs.Count);
        Assert.Contains(_defs, d => d.Name == "isWithin");
        Assert.Contains(_defs, d => d.Name == "overlapsWith");
        Assert.Contains(_defs, d => d.Name == "isOnBorderOf");
    }

    [Fact]
    public void IsWithin_holds_when_inner_box_is_contained()
    {
        Assert.True(Eval("self.isWithin(other)", Bounds(10, 10, 5, 5), Bounds(0, 0, 100, 100)));
        Assert.False(Eval("self.isWithin(other)", Bounds(10, 10, 200, 5), Bounds(0, 0, 100, 100)));
    }

    [Fact]
    public void OverlapsWith_detects_intersection()
    {
        Assert.True(Eval("self.overlapsWith(other)", Bounds(0, 0, 50, 50), Bounds(25, 25, 50, 50)));
        Assert.False(Eval("self.overlapsWith(other)", Bounds(0, 0, 10, 10), Bounds(100, 100, 10, 10)));
    }

    [Fact]
    public void IsOnBorderOf_when_an_edge_aligns()
    {
        Assert.True(Eval("self.isOnBorderOf(other)", Bounds(0, 50, 10, 10), Bounds(0, 0, 100, 100)));
        Assert.False(Eval("self.isOnBorderOf(other)", Bounds(5, 50, 10, 10), Bounds(0, 0, 100, 100)));
    }

    [Fact]
    public void Nullary_definition_right_computes_x_plus_width()
    {
        var env = new EvaluationEnvironment(_mm, _ops).Bind("self", Bounds(10, 0, 5, 5).AsValue());
        Assert.Equal(15.0, _interp.Evaluate(_parser.ParseExpression("self.right()"), env).AsReal());
    }
}
