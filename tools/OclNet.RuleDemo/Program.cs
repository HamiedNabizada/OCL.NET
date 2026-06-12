// =============================================================================
// OclNet.RuleDemo — erzeugt den "So feuern die Regeln"-Anhang der
// Blatt-3-Dokumentenbasis aus ECHTEN Engine-Läufen.
//
// Vorgehen je Demo: frisches Beispielmodell laden, eine gezielte Mutation
// anwenden, validieren, Delta zur Baseline bilden. Erwartete Regel-IDs werden
// verifiziert — feuert eine erwartete Regel nicht, bricht der Generator ab
// (kein handgepflegtes, potenziell veraltetes Beispiel möglich).
//
// Aufruf:
//   dotnet run --project tools/OclNet.RuleDemo -- <test.aml> <spec-Ordner> <ausgabe.tex>
// =============================================================================
using System.Text;
using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Parser;
using OclNet.Core.Validation;

if (args.Length != 3)
{
    Console.Error.WriteLine("Aufruf: OclNet.RuleDemo <test.aml> <spec-Ordner> <ausgabe.tex>");
    return 1;
}
var amlPath = args[0];
var specDir = args[1];
var texPath = args[2];

var parser = new OclParser();
var validator = new OclValidator();

// ---- Severity je Regel nach Katalog (der Anhang muss dieselben Severities zeigen) --
var katalogSeverity = new Dictionary<string, ValidationSeverity>(StringComparer.Ordinal)
{
    ["ProjectMinimumProcess"] = ValidationSeverity.Error,                  // A1
    ["SystemLimitCardinality"] = ValidationSeverity.Error,                 // A2
    ["StateMinimumCardinality"] = ValidationSeverity.Warning,              // A3
    ["ProcessOperatorMinimumCardinality"] = ValidationSeverity.Warning,    // A4
    ["FlowEndpointsTyped"] = ValidationSeverity.Error,                     // C1
    ["NoStateToStateFlow"] = ValidationSeverity.Error,                     // C2
    ["NoProcessOperatorToProcessOperatorFlow"] = ValidationSeverity.Error, // C3
    ["UsageEndpointsTyped"] = ValidationSeverity.Error,                    // C4
    ["NoDuplicateConnections"] = ValidationSeverity.Warning,               // C5
    ["FlowDirected"] = ValidationSeverity.Error,                           // C6
    ["NoMixedFlowTypes"] = ValidationSeverity.Warning,                     // C9
    ["UniqueIdentifiers"] = ValidationSeverity.Error,                      // D1
    ["ProcessOperatorNamed"] = ValidationSeverity.Info,                    // D3
    ["StateNamed"] = ValidationSeverity.Info,                              // D4
    ["TechnicalResourceNamed"] = ValidationSeverity.Info,                  // D5
    ["ProcessNamed"] = ValidationSeverity.Warning,                         // D6
    ["LongNameMandatory"] = ValidationSeverity.Error,                      // D7
    ["VersionRevisionPresent"] = ValidationSeverity.Warning,               // D8
    ["RefObjResolvable"] = ValidationSeverity.Error,                       // F1
    ["RefProcessResolvable"] = ValidationSeverity.Error,                   // F2
    ["AllReferencesResolvable"] = ValidationSeverity.Error,                // F3
    ["ProcessOperatorHasIO"] = ValidationSeverity.Warning,                 // G1
    ["StateHasConcreteType"] = ValidationSeverity.Error,                   // G2
    ["NoSelfReference"] = ValidationSeverity.Error,                        // G3
    ["NoOrphanedElements"] = ValidationSeverity.Warning,                   // G4
    ["SourceTargetSameProcess"] = ValidationSeverity.Error,                // I3
};

// ---- Regelsatz: publizierte PURE-Regeln + B2 (Geometrie, mit def:-Helpern) ------
var pureBlocks = SplitRules(File.ReadAllText(Path.Combine(specDir, "vdi3682-pure-rules.ocl"))).ToList();
if (pureBlocks.Count != 26)
    throw new InvalidOperationException($"vdi3682-pure-rules.ocl: 26 Regeln erwartet, {pureBlocks.Count} gefunden (SplitRules-Drift?).");
var baseRules = pureBlocks
    .Select(b =>
    {
        var name = parser.ParseConstraint(b).Name ?? "?";
        return new OclRuleSpec(name, katalogSeverity.GetValueOrDefault(name, ValidationSeverity.Warning), "Katalog", b);
    })
    .ToList();
baseRules.Add(new OclRuleSpec("ProcessOperatorWithinSystemLimit", ValidationSeverity.Error, "Katalog B2",
    "context FPD_ProcessOperator inv ProcessOperatorWithinSystemLimit: " +
    "let sl: FPD_SystemLimit = self.process.containedElement->select(e | e.oclIsKindOf(FPD_SystemLimit))->first() in " +
    "self.bounds.isWithin(sl.bounds)"));
var helpers = parser.ParseDefinitions(File.ReadAllText(Path.Combine(specDir, "vdi3682-helpers.ocl")));
var compiled = validator.Compile(baseRules, helpers);

var e1Rule = new OclRuleSpec("CharacteristicCategoryPresent", ValidationSeverity.Warning, "Katalog E1 (Phase 2)",
    "context FPD_Characteristic inv CharacteristicCategoryPresent: self.category->notEmpty()");
var compiledMitE1 = validator.Compile(baseRules.Append(e1Rule), helpers);

// ---- Baseline -------------------------------------------------------------------
var baselineDoc = CAEXDocument.LoadFromFile(amlPath);
var baseline = Validate(baselineDoc, compiled);
var baselineKeys = baseline.Select(Key).ToHashSet();

// ---- Demos ------------------------------------------------------------------------
var demos = new List<Demo>
{
    new("A2 — fehlende Systemgrenze",
        "Die Systemgrenze des Prozesses \\texttt{TestProcess} wird gelöscht.",
        new[] { "SystemLimitCardinality" },
        doc => Child(Process(doc, "TestProcess"), "SystemLimit_TestProcess").Remove()),

    new("A2 — doppelte Systemgrenze",
        "Eine zweite Systemgrenze wird in \\texttt{TestProcess} eingefügt.",
        new[] { "SystemLimitCardinality" },
        doc => {
            var sl = Process(doc, "TestProcess").InternalElement.Append("SL_Doppelt");
            sl.ID = "demo-sl2";
            sl.RefBaseSystemUnitPath = "VDI_FPD_SystemUnitClassLib/FPD_SystemLimit";
            AddIdentification(sl, "Doppelte Systemgrenze", "demo-sl2");
        }),

    new("A3 — zu wenige Zustände",
        "Der Eingangszustand \\texttt{Input} wird gelöscht; \\texttt{TestProcess} behält nur einen Zustand.",
        new[] { "StateMinimumCardinality" },
        doc => Child(Process(doc, "TestProcess"), "Input").Remove()),

    new("A4 — kein Prozessoperator",
        "Der einzige Prozessoperator \\texttt{Step} wird aus \\texttt{TestProcess} gelöscht (inkl.\\ Folgebefunde der verwaisten Flüsse).",
        new[] { "ProcessOperatorMinimumCardinality" },
        doc => Child(Process(doc, "TestProcess"), "Step").Remove()),

    new("C1/C2 — Fluss Zustand--Zustand",
        "Der Fluss \\texttt{Input\\_to\\_Step} wird auf den Eingang von \\texttt{Output} umverdrahtet: " +
        "er verläuft jetzt von Zustand zu Zustand. Zwei Regeln feuern gemeinsam.",
        new[] { "FlowEndpointsTyped", "NoStateToStateFlow" },
        doc => Link(doc, "TestProcess", "Input_to_Step").RefPartnerSideB =
               Link(doc, "TestProcess", "Step_to_Output").RefPartnerSideB),

    new("C5 — doppelte Verbindung",
        "Zwischen \\texttt{Input} und \\texttt{Step} wird über neue Schnittstellen ein zweiter Fluss gleichen Typs angelegt.",
        new[] { "NoDuplicateConnections" },
        doc => {
            var p = Process(doc, "TestProcess");
            var quelle = Child(p, "Input"); var ziel = Child(p, "Step");
            var outIf = quelle.ExternalInterface.Append("demo_out");
            outIf.ID = "demo-if-out"; outIf.RefBaseClassPath = "VDI_FPD_InterfaceClassLib/FPD_FlowOut";
            var inIf = ziel.ExternalInterface.Append("demo_in");
            inIf.ID = "demo-if-in"; inIf.RefBaseClassPath = "VDI_FPD_InterfaceClassLib/FPD_FlowIn";
            var l = p.InternalLink.Append("Input_to_Step_Duplikat");
            l.RefPartnerSideA = outIf.ID; l.RefPartnerSideB = inIf.ID;
        }),

    new("C6 — Fluss gegen die Richtung",
        "Das Ziel von \\texttt{Input\\_to\\_Step} wird auf eine \\emph{ausgehende} Schnittstelle (FlowOut) des Prozessoperators gelegt.",
        new[] { "FlowDirected" },
        doc => Link(doc, "TestProcess", "Input_to_Step").RefPartnerSideB =
               Link(doc, "TestProcess", "Step_to_Output").RefPartnerSideA),

    new("D1 — doppelte eindeutige Idents",
        "Im Grundzustand kollidieren bereits die \\emph{leeren} \\texttt{uniqueIdent}s der Systemgrenzen " +
        "(siehe Baseline) --- für die Demo werden zunächst alle Idents repariert; anschließend erhalten " +
        "\\texttt{Input} und \\texttt{Output} gezielt denselben Wert.",
        new[] { "UniqueIdentifiers" },
        doc => {
            SetIdent(Child(Process(doc, "TestProcess"), "Input"), "uniqueIdent", "DUPLICATE-ID");
            SetIdent(Child(Process(doc, "TestProcess"), "Output"), "uniqueIdent", "DUPLICATE-ID");
        },
        Vorbereitung: RepariereLeereIdents),

    new("D5 — unbenannte Technische Ressource",
        "Elementname und Kurzname der Technischen Ressource werden geleert.",
        new[] { "TechnicalResourceNamed" },
        doc => {
            var tr = Child(Process(doc, "TestProcess"), "TR");
            SetIdent(tr, "shortName", "");
            tr.Name = "";
        }),

    new("F1 — hängende Objektreferenz",
        "Im Grundzustand lösen bereits mehrere Boundary-State-\\texttt{refObj}s nicht auf (siehe Baseline; " +
        "die \\texttt{uniqueIdent}s sind ungepflegt) --- für die Demo werden zunächst alle \\texttt{refObj}s " +
        "geleert; anschließend zeigt das \\texttt{refObj} von \\texttt{Output} gezielt ins Leere.",
        new[] { "RefObjResolvable" },
        doc => SetAttr(Child(Process(doc, "TestProcess"), "Output"), "refObj", "deadbeef-gibt-es-nicht"),
        Vorbereitung: LeereAlleRefObjs),

    new("G3 — Selbstreferenz",
        "Quelle und Ziel von \\texttt{Input\\_to\\_Step} werden auf dasselbe Element gelegt (Folgebefunde: auch die Endpunkt-Typisierung kippt).",
        new[] { "NoSelfReference" },
        doc => {
            var l = Link(doc, "TestProcess", "Input_to_Step");
            l.RefPartnerSideB = l.RefPartnerSideA;
        }),

    new("G4 — verwaistes Element",
        "Ein Produkt ohne jede Verbindung wird eingefügt (mit vollständiger Kennzeichnung, damit ausschließlich G4 feuert).",
        new[] { "NoOrphanedElements" },
        doc => {
            var p = Process(doc, "TestProcess").InternalElement.Append("Verwaist");
            p.ID = "demo-orphan";
            p.RefBaseSystemUnitPath = "VDI_FPD_SystemUnitClassLib/FPD_Product";
            AddIdentification(p, "Verwaistes Produkt", "demo-orphan");
        }),

    new("I3 — Fluss über Prozessgrenzen",
        "Das Ziel von \\texttt{Input\\_to\\_Step} wird in den \\emph{anderen} Prozess (\\texttt{Step}-Teilprozess) verlegt.",
        new[] { "SourceTargetSameProcess" },
        doc => Link(doc, "TestProcess", "Input_to_Step").RefPartnerSideB =
               Link(doc, "Step", "IntermediateProduct_to_Step3").RefPartnerSideB),

    new("B2 — Prozessoperator außerhalb der Systemgrenze (Geometrie)",
        "Die x-Koordinate des Prozessoperators wird auf 2000 gesetzt — weit außerhalb der Systemgrenze. " +
        "Die Regel nutzt die \\texttt{def:}-Hilfsoperation \\texttt{isWithin} aus \\texttt{vdi3682-helpers.ocl}.",
        new[] { "ProcessOperatorWithinSystemLimit" },
        doc => Child(Process(doc, "TestProcess"), "Step")
                 .Attribute["ViewInformation"]!.Attribute["position"]!.Attribute["x"]!.Value = "2000"),
};

// ---- Läufe ------------------------------------------------------------------------
var sb = new StringBuilder();
Kopf(sb);
BaselineAbschnitt(sb, baselineDoc, baseline);

var demoNr = 0;
foreach (var demo in demos)
{
    demoNr++;
    var doc = CAEXDocument.LoadFromFile(amlPath);

    // Lokale Baseline: ggf. nach einer Vorbereitungs-Reparatur neu bestimmen.
    var lokaleBaseline = baselineKeys;
    if (demo.Vorbereitung is not null)
    {
        demo.Vorbereitung(doc);
        lokaleBaseline = Validate(doc, compiled).Select(Key).ToHashSet();
    }

    // Namen VOR der Mutation einsammeln (eine Mutation kann Namen leeren oder
    // Elemente entfernen) und um danach hinzugekommene Elemente ergänzen.
    var namen = NameMap(doc);

    demo.Mutation(doc);
    foreach (var (id, name) in NameMap(doc))
        namen.TryAdd(id, name);

    var findings = Validate(doc, compiled);
    var delta = findings.Where(f => !lokaleBaseline.Contains(Key(f))).ToList();

    var geliefert = delta.Select(f => f.RuleId).ToHashSet();
    foreach (var erwartet in demo.Erwartet)
        if (!geliefert.Contains(erwartet))
            throw new InvalidOperationException(
                $"Demo '{demo.Titel}': erwartete Regel '{erwartet}' hat NICHT gefeuert. Geliefert: {string.Join(", ", geliefert)}");

    DemoAbschnitt(sb, demoNr, demo, delta, namen);
    Console.WriteLine($"Demo {demoNr:00} ok: {demo.Titel}  ->  {delta.Count} Befund(e)");
}

// ---- Diagnose-Demos -----------------------------------------------------------------
demoNr++;
{
    var doc = CAEXDocument.LoadFromFile(amlPath);
    var findings = Validate(doc, compiledMitE1);
    var diag = findings.Where(f => f.Message.Contains("unknown to the model binding")).ToList();
    if (diag.Count == 0) throw new InvalidOperationException("E1-Diagnose hat nicht gefeuert.");
    DemoAbschnitt(sb, demoNr,
        new Demo("Diagnose — unbekannter Kontexttyp (Phase-2-Regel)",
            "Die Merkmals-Regel E1 (\\texttt{context FPD\\_Characteristic}) wird mitgeladen. Das Binding kennt den Typ " +
            "noch nicht — statt eines stillen Bestehens entsteht ein Diagnose-Befund.",
            new[] { "CharacteristicCategoryPresent" }, _ => { }),
        diag, NameMap(doc));
    Console.WriteLine($"Demo {demoNr:00} ok: Diagnose unbekannter Kontexttyp");
}

demoNr++;
{
    var doc = CAEXDocument.LoadFromFile(amlPath);
    var po = Child(Process(doc, "TestProcess"), "Step");
    po.RefBaseSystemUnitPath = "FremdeLib/Unbekannt";
    foreach (var rr in po.RoleRequirements.ToList()) rr.Remove();
    var unklassifiziert = new CaexMetamodel(doc).UnclassifiedElements().ToList();
    if (unklassifiziert.Count == 0) throw new InvalidOperationException("UnclassifiedElements ist leer.");
    sb.AppendLine($"\\subsection*{{Demo {demoNr}: Diagnose --- untypisiertes Element}}");
    sb.AppendLine("\\textbf{Mutation:} Der SystemUnitClass-Pfad des Prozessoperators wird auf eine fremde " +
        "Bibliothek gesetzt und die Rollenzuordnung entfernt. Das Element ist damit für \\emph{alle} Regeln " +
        "unsichtbar --- der gefährlichste stille Fehlermodus. Die Engine stellt solche Elemente über " +
        "\\texttt{UnclassifiedElements()} bereit; das AML-Editor-Plugin meldet sie als Warnung.");
    sb.AppendLine();
    sb.AppendLine("\\textbf{Engine-Ausgabe:}");
    sb.AppendLine("\\begin{itemize}[nosep]");
    foreach (var ie in unklassifiziert)
        sb.AppendLine($"  \\item Untypisiertes Element \\texttt{{{Tex(ie.Name ?? ie.ID)}}} (ID \\texttt{{{Tex(ie.ID)}}})");
    sb.AppendLine("\\end{itemize}");
    sb.AppendLine();
    Console.WriteLine($"Demo {demoNr:00} ok: untypisiertes Element");
}

File.WriteAllText(texPath, sb.ToString(), new UTF8Encoding(false));
Console.WriteLine($"Geschrieben: {texPath}");
return 0;

// =============================================================================
// Hilfsfunktionen
// =============================================================================

static IEnumerable<string> SplitRules(string spec) =>
    spec.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(b => b.Trim()).Where(b => b.StartsWith("context"));

List<ValidationFinding> Validate(CAEXDocument doc, CompiledRuleSet rules) =>
    validator.Validate(new CaexMetamodel(doc), rules);

static string Key(ValidationFinding f) => $"{f.RuleId}|{f.TargetId}";

static InternalElementType Process(CAEXDocument doc, string name) =>
    doc.CAEXFile.InstanceHierarchy.SelectMany(ih => ih.InternalElement)
        .First(ie => ie.RefBaseSystemUnitPath?.EndsWith("/FPD_Process") == true && ie.Name == name);

static InternalElementType Child(InternalElementType parent, string name) =>
    parent.InternalElement.First(ie => ie.Name == name);

static InternalLinkType Link(CAEXDocument doc, string process, string name) =>
    Process(doc, process).InternalLink.First(l => l.Name == name);

static void SetIdent(InternalElementType ie, string feld, string wert)
{
    var ident = ie.Attribute["Identification"] ?? ie.Attribute.Append("Identification");
    var attr = ident.Attribute[feld] ?? ident.Attribute.Append(feld);
    attr.Value = wert;
}

static void SetAttr(InternalElementType ie, string name, string wert)
{
    var attr = ie.Attribute[name] ?? ie.Attribute.Append(name);
    attr.Value = wert;
}

/// <summary>Alle refObj-Werte leeren (Reparatur-Vorbereitung für F1 — die Baseline enthält hängende Referenzen).</summary>
static void LeereAlleRefObjs(CAEXDocument doc)
{
    void Walk(IEnumerable<InternalElementType> ies)
    {
        foreach (var ie in ies)
        {
            var attr = ie.Attribute["refObj"];
            if (attr is not null && !string.IsNullOrEmpty(attr.Value)) attr.Value = "";
            Walk(ie.InternalElement);
        }
    }
    foreach (var ih in doc.CAEXFile.InstanceHierarchy) Walk(ih.InternalElement);
}

/// <summary>Alle leeren uniqueIdents im Dokument eindeutig befüllen (Reparatur-Vorbereitung für D1).</summary>
static void RepariereLeereIdents(CAEXDocument doc)
{
    var n = 0;
    void Walk(IEnumerable<InternalElementType> ies)
    {
        foreach (var ie in ies)
        {
            var uid = ie.Attribute["Identification"]?.Attribute["uniqueIdent"];
            if (uid is not null && string.IsNullOrEmpty(uid.Value)) uid.Value = $"repariert-{n++}";
            Walk(ie.InternalElement);
        }
    }
    foreach (var ih in doc.CAEXFile.InstanceHierarchy) Walk(ih.InternalElement);
}

static void AddIdentification(InternalElementType ie, string langname, string uid)
{
    SetIdent(ie, "uniqueIdent", uid);
    SetIdent(ie, "longName", langname);
    SetIdent(ie, "shortName", langname);
    SetIdent(ie, "versionNumber", "1");
    SetIdent(ie, "revisionNumber", "1");
}

static Dictionary<string, string> NameMap(CAEXDocument doc)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    void Walk(IEnumerable<InternalElementType> ies)
    {
        foreach (var ie in ies)
        {
            if (!string.IsNullOrEmpty(ie.ID) && !string.IsNullOrEmpty(ie.Name))
            { map[ie.ID] = ie.Name; map[ie.ID.Trim('{', '}')] = ie.Name; }
            Walk(ie.InternalElement);
        }
    }
    foreach (var ih in doc.CAEXFile.InstanceHierarchy) Walk(ih.InternalElement);
    return map;
}

static string Aufloesen(string? text, Dictionary<string, string> namen)
{
    if (string.IsNullOrEmpty(text)) return "";
    // Längste Keys zuerst ({guid} vor guid) und case-insensitiv (CAEX-IDs sind es auch).
    foreach (var (id, name) in namen.OrderByDescending(kv => kv.Key.Length))
        text = System.Text.RegularExpressions.Regex.Replace(
            text, System.Text.RegularExpressions.Regex.Escape(id), name,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return text;
}

static string Tex(string? s)
{
    if (string.IsNullOrEmpty(s)) return "";
    // Backslash über Platzhalter, sonst würden die Braces von \textbackslash{}
    // anschließend mit-escaped (\textbackslash\{\}).
    return s.Replace("\\", "\x01")
            .Replace("&", "\\&").Replace("%", "\\%").Replace("#", "\\#")
            .Replace("$", "\\$").Replace("_", "\\_")
            .Replace("{", "\\{").Replace("}", "\\}")
            .Replace("\x01", "\\textbackslash{}")
            .Replace("\"", "\\grqq{}").Replace("→", "$\\rightarrow$");
}

static void Kopf(StringBuilder sb)
{
    sb.AppendLine("% =============================================================================");
    sb.AppendLine("% GENERIERT durch OclNet.RuleDemo aus echten Engine-Laeufen gegen test.aml.");
    sb.AppendLine("% NICHT von Hand editieren — Tool erneut ausfuehren.");
    sb.AppendLine("% Jede Demo ist verifiziert: feuert die erwartete Regel nicht, bricht der");
    sb.AppendLine("% Generator ab. Diese Beispiele KOENNEN nicht von der Engine abweichen.");
    sb.AppendLine("% =============================================================================");
    sb.AppendLine("\\chapter{So feuern die Regeln --- Demonstrationen}");
    sb.AppendLine("\\label{ch:regelfeuer}");
    sb.AppendLine();
    sb.AppendLine("Dieses Kapitel ist aus \\emph{echten Validierungsläufen} der Referenz-Engine");
    sb.AppendLine("generiert: Je Demo wird das Beispielmodell gezielt beschädigt, validiert und");
    sb.AppendLine("das \\emph{Delta} der Befunde gegenüber dem Grundzustand gezeigt --- wörtlich");
    sb.AppendLine("die Engine-Ausgabe (Element-IDs zur Lesbarkeit durch Namen ersetzt). Der");
    sb.AppendLine("Generator bricht ab, wenn eine erwartete Regel nicht feuert; die Beispiele");
    sb.AppendLine("können also nicht von der Implementierung abweichen.");
    sb.AppendLine();
}

void BaselineAbschnitt(StringBuilder sb, CAEXDocument doc, List<ValidationFinding> baseline)
{
    var namen = NameMap(doc);
    sb.AppendLine("\\section{Grundzustand des Beispielmodells}");
    sb.AppendLine();
    sb.AppendLine("Das Beispielmodell (\\texttt{test.aml}: Teilprozess \\texttt{Step} mit drei");
    sb.AppendLine("Prozessoperatoren, Hauptprozess \\texttt{TestProcess} mit Operator, zwei");
    sb.AppendLine("Produkten und Technischer Ressource) ist \\emph{absichtlich nicht perfekt} ---");
    sb.AppendLine("schon der Grundzustand liefert Befunde, die in den Demos als Baseline");
    sb.AppendLine("abgezogen werden:");
    sb.AppendLine();
    sb.AppendLine("\\begin{longtable}{@{}llrl@{}}");
    sb.AppendLine("\\toprule \\textbf{Regel} & \\textbf{Severity} & \\textbf{Anzahl} & \\textbf{Beispielziel} \\\\ \\midrule");
    foreach (var g in baseline.GroupBy(f => f.RuleId).OrderBy(g => g.Key))
    {
        var ziel = Aufloesen(g.First().TargetId, namen);
        sb.AppendLine($"{Tex(g.Key)} & {g.First().Severity} & {g.Count()} & {Tex(ziel)} \\\\");
    }
    sb.AppendLine("\\bottomrule");
    sb.AppendLine("\\end{longtable}");
    sb.AppendLine();
}

void DemoAbschnitt(StringBuilder sb, int nr, Demo demo, List<ValidationFinding> delta, Dictionary<string, string> namen)
{
    sb.AppendLine($"\\subsection*{{Demo {nr}: {demo.Titel}}}");
    sb.AppendLine($"\\textbf{{Mutation:}} {demo.Beschreibung}");
    sb.AppendLine();
    sb.AppendLine($"\\textbf{{Erwartet:}} {string.Join(", ", demo.Erwartet.Select(e => $"\\texttt{{{Tex(e)}}}"))}" +
                  " --- \\textbf{neue Befunde der Engine:}");
    sb.AppendLine("\\begin{itemize}[nosep]");
    foreach (var f in delta.Take(10))
    {
        var hervor = demo.Erwartet.Contains(f.RuleId) ? "\\textbf" : "\\textnormal";
        // Das "[RuleId] "-Präfix der Engine-Message ist im Item redundant (die ID steht davor).
        var message = f.Message.StartsWith($"[{f.RuleId}] ") ? f.Message[($"[{f.RuleId}] ".Length)..] : f.Message;
        sb.AppendLine($"  \\item {hervor}{{[{f.Severity}] \\texttt{{{Tex(f.RuleId)}}}}} --- {Tex(Aufloesen(message, namen))}");
    }
    if (delta.Count > 10) sb.AppendLine($"  \\item \\textit{{\\dots{{}} und {delta.Count - 10} weitere Folgebefunde}}");
    sb.AppendLine("\\end{itemize}");
    sb.AppendLine();
}

/// <param name="Vorbereitung">Optionale Reparatur VOR der lokalen Baseline — nötig, wenn die
/// Zielregel im Grundzustand bereits feuert (z.B. D1 wegen leerer uniqueIdents) und das Delta
/// sonst leer bliebe.</param>
record Demo(string Titel, string Beschreibung, string[] Erwartet, Action<CAEXDocument> Mutation,
            Action<CAEXDocument>? Vorbereitung = null);
