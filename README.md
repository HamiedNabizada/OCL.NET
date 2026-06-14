# OclNet

An embeddable **OCL (Object Constraint Language) engine for .NET**. Parses OCL
constraints and evaluates them against a model, producing structured validation
findings.

The first driving use case is the machine-checkable rule catalogue for **VDI 3682
Blatt 3** (Formalized Process Description), validated against CAEX/AutomationML
documents. The engine core is metamodel-agnostic, so it can grow toward general OCL
support and bind to other model representations.

## Why this exists

There is no mature OCL engine for .NET — Eclipse OCL is a Java/Eclipse-stack tool.
OclNet fills that gap with a clean, dependency-light, readable reference
implementation whose goal is to execute the *exact* published VDI 3682 Blatt 3 OCL
constraints, demonstrating the norm is machine checkable.

## Quickstart

```csharp
using OclNet.Core;
using OclNet.Core.Validation;

var engine = new OclEngine();

// 1. Evaluate an expression against a model element (bound as `self`).
bool ok = engine.Evaluate("self.containedElement->size() >= 1", model, self).AsBool();

// 2. Validate a model against a rule set (compile once, validate many).
var rules = new[]
{
    new OclRuleSpec("VDI3682.SystemLimitCardinality", ValidationSeverity.Error, "VDI 3682 Bild 2",
        "context FPD_Process inv SystemLimitCardinality: " +
        "self.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->size() = 1"),
};
var compiled = engine.Compile(rules);          // parses once (AST cache)
List<ValidationFinding> findings = engine.Validate(model, compiled);
```

`model` is an `IOclModel` binding. For CAEX/AutomationML use `OclNet.Caex`:

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
                                      IOclMetamodel  ◀── binding (CAEX, …)
```

| Layer | Project / namespace | Notes |
|---|---|---|
| AST | `OclNet.Core.Ast` | Pure-data records + source locations. |
| Values | `OclNet.Core.Values` | `OclValue` tagged union incl. `OclVoid`/`OclInvalid`. |
| Interpreter | `OclNet.Core.Interpreter` | Tree-walking, three-valued boolean logic; `def:` helper operations. |
| Parser | `OclNet.Core.Parser` (+ ANTLR4 grammar `Grammar/Ocl.g4`) | Fail-fast `OclParseException` with location. |
| Validation | `OclNet.Core.Validation` | `OclValidator`, `CompiledRuleSet`, `ValidationFinding`. |
| Metamodel seam | `OclNet.Core.Metamodel` | `IOclMetamodel` / `IOclModel`. |
| CAEX binding | `OclNet.Caex` | `CaexMetamodel` + `FpdTypeRegistry` over Aml.Engine. |

`OclNet.Core` has **no dependency on Aml.Engine** — the CAEX specifics live entirely
in `OclNet.Caex`. A different binding (EMF, POCOs, …) is a new `IOclMetamodel`
implementation, not a fork of the core.

## Supported OCL subset

Type operations (`oclIsKindOf`/`oclIsTypeOf`/`oclType`), collection operations
(`size`/`isEmpty`/`notEmpty`/`first`/`last`/`includes`/`excludes`/`asSet`/`asSequence`),
iterators (`select`/`reject`/`collect`/`forAll`/`exists` incl. multi-variable, plus
`closure`/`closureDepth`), boolean logic with three-valued semantics, comparison and
arithmetic, property navigation, `let … in`, `if … then … else … endif`, collection
literals, `Sequence{…}`, string `matches`/`size`/`substring`/`toUpperCase`/…, and
user-defined `def:` operations.

Out of scope (by design, for now): the full Set/Bag/Sequence/OrderedSet kind algebra,
tuples, `oclAsType`, and operations the VDI 3682 catalogue does not exercise. These
are added demand-driven.

## Specs & artifacts

- [`spec/vdi3682-pure-rules.ocl`](spec/vdi3682-pure-rules.ocl) — the 26 executable PURE rules.
- [`spec/vdi3682-phase2-rules.ocl`](spec/vdi3682-phase2-rules.ocl) — the 4 FPD_Characteristic rules, deferred until the binding exposes characteristics (running them today would be a vacuous pass; the validator reports their context as a diagnostic instead).
- [`spec/vdi3682-helpers.ocl`](spec/vdi3682-helpers.ocl) — geometry helper `def:` library.

## Fail-loud guarantees

A reference validator's worst failure mode is a silent pass. The engine therefore:
unknown context types raise a diagnostic finding (never a silent skip); elements the
type registry cannot classify are exposed via `CaexMetamodel.UnclassifiedElements()`;
role-typed AML files (RoleRequirements without SUC paths) resolve correctly; and the
project scope is a real enumerable instance, so project-level rules (uniqueness,
minimum cardinality) actually fire.

## Build & test

```bash
dotnet test OclNet.sln
# with coverage (generated parser excluded):
dotnet test OclNet.sln --settings coverlet.runsettings
```

168 tests; ~90% line coverage on engine code; 50 rules over a ~100-element model
with ~90 typed links validate in well under the 500 ms budget.

## Roadmap

See [019-OCL-Engine.md](../FPB.JS_Docs/Geplante-Features/019-OCL-Engine.md) and the
[rule-catalogue audit](../FPB.JS_Docs/Standards/Drafts/VDI3682-Blatt3-Regelkatalog-Audit.md).

## OCL conformance

OclNet implements a subset of OMG OCL 2.4 (`formal/2014-02-03`). The covered
constructs are listed under *Supported OCL subset* above; the structural
omissions are noted explicitly and grow demand-driven rather than speculatively.
The engine is not a certified OCL processor — its conformance claim is that the
subset it implements matches the specification, verified by the test suite.

## License

[MIT](LICENSE) — see file for full text.
