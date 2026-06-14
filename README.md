# OclNet

An embeddable **OCL (Object Constraint Language) engine for .NET**. It parses OCL
constraints and evaluates them against a model, producing structured validation
findings. The engine core is metamodel-agnostic: it binds to any model
representation through a small interface, so it is not tied to a particular schema
or domain.

## Why this exists

There is no mature, embeddable OCL engine for .NET — Eclipse OCL is a
Java/Eclipse-stack tool. OclNet fills that gap with a clean, dependency-light,
readable implementation: a fast tree-walking interpreter with three-valued logic, a
compiled rule-set API, and a metamodel seam that keeps the OCL language entirely
separate from whatever you bind it to.

## Quickstart

```csharp
using OclNet.Core;
using OclNet.Core.Validation;

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
repository ships a ready-made binding in `OclNet.Caex`:

```csharp
using Aml.Engine.CAEX;
using OclNet.Caex;

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
| AST | `OclNet.Core.Ast` | Pure-data records + source locations. |
| Values | `OclNet.Core.Values` | `OclValue` tagged union incl. `OclVoid`/`OclInvalid`. |
| Interpreter | `OclNet.Core.Interpreter` | Tree-walking, three-valued boolean logic; `def:` helper operations. |
| Parser | `OclNet.Core.Parser` (+ ANTLR4 grammar `Grammar/Ocl.g4`) | Fail-fast `OclParseException` with location. |
| Validation | `OclNet.Core.Validation` | `OclValidator`, `CompiledRuleSet`, `ValidationFinding`. |
| Metamodel seam | `OclNet.Core.Metamodel` | `IOclMetamodel` / `IOclModel`. |
| CAEX binding | `OclNet.Caex` | Example binding: `CaexMetamodel` over Aml.Engine. |

`OclNet.Core` has **no dependency on Aml.Engine** — the CAEX specifics live entirely
in `OclNet.Caex`. A different binding (EMF, POCOs, a graph DB, …) is a new
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

## Example application: a machine-checkable rule set

As a worked example, the repository includes a rule set and the matching CAEX
binding for the Formalized Process Description (VDI 3682) under
[`spec/`](spec/) and `OclNet.Caex`. It demonstrates running a real, published
constraint catalogue against AutomationML documents end-to-end — a concrete
showcase of the engine, not part of the core. Bindings and rule sets for other
domains are added the same way.

## Build & test

```bash
dotnet test OclNet.sln
# with coverage (generated parser excluded):
dotnet test OclNet.sln --settings coverlet.runsettings
```

168 tests; ~90% line coverage on engine code. A 50-rule set over a ~100-element
model with ~90 typed links validates in well under the 500 ms budget.

## OCL conformance

OclNet implements a subset of OMG OCL 2.4 (`formal/2014-02-03`). The covered
constructs are listed under *Supported OCL subset*; the structural omissions are
noted explicitly and grow demand-driven rather than speculatively. The engine is not
a certified OCL processor — its conformance claim is that the subset it implements
matches the specification, verified by the test suite.

## License

[MIT](LICENSE) — see file for full text.
