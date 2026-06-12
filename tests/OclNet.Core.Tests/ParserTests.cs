using OclNet.Core.Ast;
using OclNet.Core.Parser;
using Xunit;

namespace OclNet.Core.Tests;

/// <summary>
/// Milestone-1 acceptance: every PURE-class rule of the VDI 3682 Blatt 3 catalogue
/// parses cleanly into the AST. Plus focused AST-shape and precedence checks, and
/// the error path.
/// </summary>
public class ParserTests
{
    private readonly OclParser _parser = new();

    // ---- AST shape -----------------------------------------------------------------

    [Fact]
    public void Multiplicative_binds_tighter_than_additive()
    {
        // 1 + 2 * 3  ==  1 + (2 * 3)
        var add = Assert.IsType<BinaryExpr>(_parser.ParseExpression("1 + 2 * 3"));
        Assert.Equal(BinaryOperator.Add, add.Operator);
        var mul = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal(BinaryOperator.Multiply, mul.Operator);
    }

    [Fact]
    public void Navigation_then_collection_call_then_comparison()
    {
        // self.x->size() = 1
        var eq = Assert.IsType<BinaryExpr>(_parser.ParseExpression("self.x->size() = 1"));
        Assert.Equal(BinaryOperator.Equal, eq.Operator);
        var size = Assert.IsType<OperationCallExpr>(eq.Left);
        Assert.Equal("size", size.Name);
        var nav = Assert.IsType<NavigationExpr>(size.Source);
        Assert.Equal("x", nav.Name);
    }

    [Fact]
    public void Iterator_captures_variable_and_body()
    {
        var it = Assert.IsType<IteratorExpr>(_parser.ParseExpression("coll->select(e | e)"));
        Assert.Equal("select", it.Name);
        Assert.Equal(new[] { "e" }, it.Variables);
    }

    [Fact]
    public void Two_variable_iterator_is_supported()
    {
        var it = Assert.IsType<IteratorExpr>(_parser.ParseExpression("coll->forAll(c1, c2 | c1 <> c2)"));
        Assert.Equal(new[] { "c1", "c2" }, it.Variables);
    }

    [Fact]
    public void Type_operation_carries_type_name_as_argument()
    {
        var op = Assert.IsType<OperationCallExpr>(_parser.ParseExpression("self.oclIsKindOf(FPD_State)"));
        Assert.Equal("oclIsKindOf", op.Name);
        var typeArg = Assert.IsType<VariableExpr>(Assert.Single(op.Arguments));
        Assert.Equal("FPD_State", typeArg.Name);
    }

    [Fact]
    public void Let_binding_parses()
    {
        var let = Assert.IsType<LetExpr>(_parser.ParseExpression("let x = 1 in x"));
        Assert.Equal("x", let.Variable);
    }

    [Fact]
    public void Sequence_literal_parses()
    {
        var seq = Assert.IsType<CollectionLiteralExpr>(_parser.ParseExpression("Sequence{1, 2, 3}"));
        Assert.Equal("Sequence", seq.Kind);
        Assert.Equal(3, seq.Elements.Count);
    }

    [Fact]
    public void Constraint_header_is_captured()
    {
        var c = _parser.ParseConstraint(
            "context FPD_Process inv SystemLimitCardinality: self.x->size() = 1");
        Assert.Equal("FPD_Process", c.ContextType);
        Assert.Equal("SystemLimitCardinality", c.Name);
    }

    [Fact]
    public void Qualified_context_type_parses()
    {
        var c = _parser.ParseConstraint(
            "context FPD_Characteristic::DescriptiveElement inv X: self.value->notEmpty()");
        Assert.Equal("FPD_Characteristic::DescriptiveElement", c.ContextType);
    }

    // ---- error path ----------------------------------------------------------------

    [Fact]
    public void Syntax_error_throws_with_location()
    {
        var ex = Assert.Throws<OclParseException>(() => _parser.ParseExpression("self."));
        Assert.NotEqual(SourceLocation.None, ex.Location);
    }

    // ---- M1 acceptance: all PURE rules parse ---------------------------------------

    [Theory]
    [MemberData(nameof(PureRules))]
    public void Pure_catalogue_rule_parses(string id, string ocl)
    {
        var constraint = _parser.ParseConstraint(ocl);
        Assert.False(string.IsNullOrEmpty(constraint.ContextType), $"rule {id} produced no context type");
        Assert.NotNull(constraint.Body);
    }

    /// <summary>
    /// The 30 PURE-class constraints (verbatim from VDI3682-Blatt3-Regelkatalog.md).
    /// E1/E2/E4/E5 are PURE* (depend on Characteristic CAEX mapping for *evaluation*)
    /// but must still parse here.
    /// </summary>
    public static IEnumerable<object[]> PureRules() => new (string Id, string Ocl)[]
    {
        ("A1", "context FPD_Project inv ProjectMinimumProcess: self.process->size() >= 1"),
        ("A2", "context FPD_Process inv SystemLimitCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1"),
        ("A3", "context FPD_Process inv StateMinimumCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_State))->size() >= 2"),
        ("A4", "context FPD_Process inv ProcessOperatorMinimumCardinality: self.containedElement->select(e | e.oclIsKindOf(FPD_ProcessOperator))->size() >= 1"),
        ("C1", "context FPD_Flow inv FlowEndpointsTyped: (source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_ProcessOperator)) or (source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_State))"),
        ("C2", "context FPD_Flow inv NoStateToStateFlow: not (source.oclIsKindOf(FPD_State) and target.oclIsKindOf(FPD_State))"),
        ("C3", "context FPD_Flow inv NoProcessOperatorToProcessOperatorFlow: not (source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_ProcessOperator))"),
        ("C4", "context FPD_Usage inv UsageEndpointsTyped: (source.oclIsKindOf(FPD_ProcessOperator) and target.oclIsKindOf(FPD_TechnicalResource)) or (source.oclIsKindOf(FPD_TechnicalResource) and target.oclIsKindOf(FPD_ProcessOperator))"),
        ("C5", "context FPD_Process inv NoDuplicateConnections: self.connections->forAll(c1, c2 | (c1 <> c2 and c1.source = c2.source and c1.target = c2.target) implies c1.oclType() <> c2.oclType())"),
        ("C6", "context FPD_Flow inv FlowDirected: self.sourceInterface.oclIsKindOf(FPD_FlowOut) and self.targetInterface.oclIsKindOf(FPD_FlowIn)"),
        ("C9", "context FPD_ProcessOperator inv NoMixedFlowTypes: let outFlows = self.outgoingConnections->select(c | c.oclIsKindOf(FPD_Flow)) in outFlows->forAll(f1, f2 | f1.oclType() = f2.oclType())"),
        ("D1", "context FPD_Project inv UniqueIdentifiers: let allElements = self.process.containedElement->asSet() in allElements->forAll(e1, e2 | e1 <> e2 implies e1.identification.uniqueIdent <> e2.identification.uniqueIdent)"),
        ("D3", "context FPD_ProcessOperator inv ProcessOperatorNamed: (self.identification.longName->notEmpty() and self.identification.longName->size() > 0) or (self.identification.shortName->notEmpty() and self.identification.shortName->size() > 0) or (self.name->notEmpty())"),
        ("D4", "context FPD_State inv StateNamed: self.identification.longName->notEmpty() or self.identification.shortName->notEmpty() or self.name->notEmpty()"),
        ("D5", "context FPD_TechnicalResource inv TechnicalResourceNamed: self.identification.longName->notEmpty() or self.identification.shortName->notEmpty() or self.name->notEmpty()"),
        ("D6", "context FPD_Process inv ProcessNamed: self.name->notEmpty()"),
        ("D7", "context FPD_Object inv LongNameMandatory: self.identification.longName->notEmpty() and self.identification.longName->size() > 0"),
        ("D8", "context FPD_Object inv VersionRevisionPresent: self.identification.versionNumber->size() = 1 and self.identification.revisionNumber->size() = 1"),
        ("E1", "context FPD_Characteristic inv CharacteristicCategoryPresent: self.category->notEmpty()"),
        ("E2", "context FPD_Characteristic inv CharacteristicDescriptivePresent: self.descriptiveElement->notEmpty()"),
        ("E4", "context FPD_Characteristic::RelationalElement inv RelationalElementResolvable: self.references->forAll(ref | self.project.containedElement->exists(e | e.identification.uniqueIdent = ref))"),
        ("E5", "context FPD_Characteristic::DescriptiveElement inv ValidityLimitsConsistent: self.validityLimits->notEmpty() implies self.validityLimits.lowerLimit <= self.validityLimits.upperLimit"),
        ("F1", "context FPD_Object inv RefObjResolvable: self.refObj->notEmpty() implies self.project.containedElement->exists(e | e.identification.uniqueIdent = self.refObj)"),
        ("F2", "context FPD_ProcessOperator inv RefProcessResolvable: self.refProcess->notEmpty() implies self.project.process->exists(p | p.id = self.refProcess)"),
        ("F3", "context FPD_Object inv AllReferencesResolvable: Sequence{self.refObj, self.refBaseObj, self.refExtendedObj, self.refComposedObj}->select(r | r->notEmpty())->forAll(r | self.project.containedElement->exists(e | e.identification.uniqueIdent = r))"),
        ("G1", "context FPD_ProcessOperator inv ProcessOperatorHasIO: self.incomingConnections->select(c | c.oclIsKindOf(FPD_Flow))->size() >= 1 and self.outgoingConnections->select(c | c.oclIsKindOf(FPD_Flow))->size() >= 1"),
        ("G2", "context FPD_State inv StateHasConcreteType: self.oclIsKindOf(FPD_Product) xor self.oclIsKindOf(FPD_Energy) xor self.oclIsKindOf(FPD_Information)"),
        ("G3", "context FPD_Connection inv NoSelfReference: self.source <> self.target"),
        ("G4", "context FPD_Object inv NoOrphanedElements: (self.oclIsKindOf(FPD_State) or self.oclIsKindOf(FPD_ProcessOperator) or self.oclIsKindOf(FPD_TechnicalResource)) implies (self.incomingConnections->notEmpty() or self.outgoingConnections->notEmpty())"),
        ("I3", "context FPD_Connection inv SourceTargetSameProcess: self.source.process = self.target.process"),
    }.Select(r => new object[] { r.Id, r.Ocl });
}
