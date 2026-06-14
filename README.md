# OCL.NET

An embeddable **OCL (Object Constraint Language) engine for .NET**. It parses OCL
constraints and evaluates them against a model, producing structured validation
findings. The engine core is metamodel-agnostic: it binds to any model
representation through a small interface, so it is not tied to a particular schema
or domain.

## Why this exists

There is no mature, embeddable OCL engine for .NET — Eclipse OCL is a
Java/Eclipse-stack tool. OCL.NET fills that gap with a clean, dependency-light,
readable implementation: a fast tree-walking interpreter with three-valued logic, a
compiled rule-set API, and a metamodel seam that keeps the OCL language entirely
separate from whatever you bind it to.

## Quickstart

```csharp
using OCL.NET.Core;
using OCL.NET.Core.Validation;

var engine = new OclEngine();

// 1. Evaluate an expression against a model element (bound as `self`).
bool ok = engine.Evaluate("self.items->size() >= 1", model, self).AsBool();

// 2. Validate a model against a rule set (compile once, validate many).
var rules = new[]
{
    new OclRuleSpec("Order.HasItems", ValidationSeverity.Error, "domain rules",
        "context Order inv HasItems: self.items->notEmpty()"),
};
var compiled = engine.Compile(rules);          // parses once (AST cache)
List<ValidationFinding> findings = engine.Validate(model, compiled);
```

`model` is any `IOclModel` binding. To validate AutomationML/CAEX documents, the
repository ships a ready-made binding in `OCL.NET.Caex`:

```csharp
using Aml.Engine.CAEX;
using OCL.NET.Caex;

var doc = CAEXDocument.LoadFromFile("model.aml");
var model = new CaexMetamodel(doc);            // IOclModel over Aml.Engine
```

## Architecture

Language and model binding are kept separate so neither pollutes the other:

```
OCL text ─▶ Parser ─▶ AST (pure data) ─▶ Interpreter ─▶ OclValue ─▶ Finding
                                              │
                                      IOclMetamodel  ◀── binding (CAEX, POCOs, …)
```

| Layer | Project / namespace | Notes |
|---|---|---|
| AST | `OCL.NET.Core.Ast` | Pure-data records + source locations. |
| Values | `OCL.NET.Core.Values` | `OclValue` tagged union incl. `OclVoid`/`OclInvalid`. |
| Interpreter | `OCL.NET.Core.Interpreter` | Tree-walking, three-valued boolean logic; `def:` helper operations. |
| Parser | `OCL.NET.Core.Parser` (+ ANTLR4 grammar `Grammar/Ocl.g4`) | Fail-fast `OclParseException` with location. |
| Validation | `OCL.NET.Core.Validation` | `OclValidator`, `CompiledRuleSet`, `ValidationFinding`. |
| Metamodel seam | `OCL.NET.Core.Metamodel` | `IOclMetamodel` / `IOclModel`. |
| CAEX binding | `OCL.NET.Caex` | Example binding: `CaexMetamodel` over Aml.Engine. |

`OCL.NET.Core` has **no dependency on Aml.Engine** — the CAEX specifics live entirely
in `OCL.NET.Caex`. A different binding (EMF, POCOs, a graph DB, …) is a new
`IOclMetamodel` implementation, not a fork of the core.

## Supported OCL subset

Type operations (`oclIsKindOf`/`oclIsTypeOf`/`oclType`), collection operations
(`size`/`isEmpty`/`notEmpty`/`first`/`last`/`includes`/`excludes`/`asSet`/`asSequence`),
iterators (`select`/`reject`/`collect`/`forAll`/`exists` incl. multi-variable, plus
`closure`/`closureDepth`), boolean logic with three-valued semantics, comparison and
arithmetic, property navigation, `let … in`, `if … then … else … endif`, collection
literals, `Sequence{…}`, string `matches`/`size`/`substring`/`toUpperCase`/…, and
user-defined `def:` operations.

Out of scope (by design, for now): the full Set/Bag/Sequence/OrderedSet kind algebra,
tuples, and `oclAsType`. The subset grows demand-driven rather than speculatively.

## Fail-loud guarantees

A validator's worst failure mode is a silent pass. The engine therefore treats an
unknown context type as a diagnostic finding (never a silent skip), and a binding can
surface model elements it could not classify rather than leaving them invisible to
every rule. The CAEX binding implements both, and exposes unclassified elements via
`CaexMetamodel.UnclassifiedElements()`.

## CAEX binding

The repository ships `OCL.NET.Caex`, a ready-made `IOclMetamodel` over
Aml.Engine that binds OCL navigation to CAEX `InternalElement`s,
`ExternalInterface`s and `InternalLink`s. It is one example of how a
binding looks — bindings for EMF, POCOs, a graph DB, etc. are added the
same way (a new `IOclMetamodel` implementation, no fork of the core).

Domain-specific rule sets live in the libraries that consume the engine,
not in this repo.

## Build & test

```bash
dotnet test OCL.NET.sln
# with coverage (generated parser excluded):
dotnet test OCL.NET.sln --settings coverlet.runsettings
```

168 tests; ~90% line coverage on engine code. A 50-rule set over a ~100-element
model with ~90 typed links validates in well under the 500 ms budget.

## OCL conformance

OCL.NET implements a subset of OMG OCL 2.4 (`formal/2014-02-03`). The covered
constructs are listed under *Supported OCL subset*; the structural omissions are
noted explicitly and grow demand-driven rather than speculatively. The engine is not
a certified OCL processor — its conformance claim is that the subset it implements
matches the specification, verified by the test suite.

## License

[MIT](LICENSE) — see file for full text.
