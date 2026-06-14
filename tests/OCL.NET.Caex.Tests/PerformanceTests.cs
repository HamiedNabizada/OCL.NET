using System.Diagnostics;
using Aml.Engine.CAEX;
using OCL.NET.Caex;
using OCL.NET.Core.Validation;
using OCL.NET.Core.Parser;
using Xunit;
using Xunit.Abstractions;

namespace OCL.NET.Caex.Tests;

/// <summary>
/// Performance acceptance: 50 rules over a ~100-InternalElement model validate in
/// well under 500 ms once compiled (AST caching). The synthetic model includes
/// typed flows (interfaces + InternalLinks) so the connection-context rules and the
/// O(n²) uniqueness rule do real work — a benchmark over an unconnected model would
/// measure a fraction of the load. Sanity assertions guard against the benchmark
/// degenerating into parse-error generation.
/// </summary>
public class PerformanceTests
{
    private readonly ITestOutputHelper _output;
    public PerformanceTests(ITestOutputHelper output) => _output = output;

    private const string Suc = "VDI_FPD_SystemUnitClassLib/";
    private const string Icl = "VDI_FPD_InterfaceClassLib/";

    private static CAEXDocument BuildModel(int processes, int elementsPerProcess)
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = doc.CAEXFile.InstanceHierarchy.Append("PerfIH");
        var id = 0;

        for (var p = 0; p < processes; p++)
        {
            var process = ih.InternalElement.Append($"P{p}");
            process.ID = $"id-{id++}";
            process.RefBaseSystemUnitPath = Suc + "FPD_Process";

            var sl = process.InternalElement.Append($"SL{p}");
            sl.ID = $"id-{id++}";
            sl.RefBaseSystemUnitPath = Suc + "FPD_SystemLimit";

            var elements = new List<InternalElementType>();
            for (var i = 0; i < elementsPerProcess; i++)
            {
                var type = i % 2 == 0 ? "FPD_Product" : "FPD_ProcessOperator";
                var e = process.InternalElement.Append($"E{p}_{i}");
                e.ID = $"id-{id++}";
                e.RefBaseSystemUnitPath = Suc + type;
                var ident = e.Attribute.Append("Identification");
                ident.Attribute.Append("longName").Value = $"name{p}_{i}";
                ident.Attribute.Append("uniqueIdent").Value = e.ID;
                elements.Add(e);
            }

            // Chain State→PO→State→… with typed flow interfaces so connection rules
            // (C1/C2/C6/G1/G3/I3/C5) enumerate and evaluate real links.
            for (var i = 0; i + 1 < elements.Count; i++)
            {
                var outIf = elements[i].ExternalInterface.Append($"out{p}_{i}");
                outIf.ID = $"if-{id++}";
                outIf.RefBaseClassPath = Icl + "FPD_FlowOut";

                var inIf = elements[i + 1].ExternalInterface.Append($"in{p}_{i}");
                inIf.ID = $"if-{id++}";
                inIf.RefBaseClassPath = Icl + "FPD_FlowIn";

                var link = process.InternalLink.Append($"L{p}_{i}");
                link.RefPartnerSideA = outIf.ID;
                link.RefPartnerSideB = inIf.ID;
            }
        }
        return doc;
    }

    private static List<OclRuleSpec> FiftyRules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-pure-rules.ocl");
        var blocks = File.ReadAllText(path).Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Where(b => b.StartsWith("context"))
            .ToList();
        return Enumerable.Range(0, 50)
            .Select(i => new OclRuleSpec($"R{i}", ValidationSeverity.Warning, "perf", blocks[i % blocks.Count]))
            .ToList();
    }

    [Fact]
    public void Fifty_rules_over_hundred_elements_validate_under_500ms()
    {
        var doc = BuildModel(processes: 5, elementsPerProcess: 19); // 5 * (1 + 1 + 19) ≈ 105 IEs + 90 links
        var model = new CaexMetamodel(doc);
        var validator = new OclValidator();
        var rules = FiftyRules();
        var helpers = new OclParser().ParseDefinitions(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-helpers.ocl")));

        var compiled = validator.Compile(rules, helpers);
        Assert.All(compiled.Rules, r => Assert.Null(r.ParseError)); // a parse-error benchmark would be fake

        // The connection rules must see real links — otherwise the benchmark idles.
        Assert.True(model.InstancesOf("FPD_Flow").Count() >= 80, "synthetic model lost its links");

        var findings = validator.Validate(model, compiled); // warm up (JIT)
        Assert.DoesNotContain(findings, f => f.Message.Contains("evaluation error"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("unknown to the model binding"));
        Assert.NotEmpty(findings); // SLs lack Identification → LongNameMandatory fires

        // Best of three — a wall-clock benchmark on a shared machine must not flake
        // on a single cold-start outlier; the budget itself stays the acceptance bar.
        var best = long.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var sw = Stopwatch.StartNew();
            validator.Validate(model, compiled);
            sw.Stop();
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }

        _output.WriteLine($"50 rules / ~105 IEs / ~90 links: best {best} ms, {findings.Count} findings");
        Assert.True(best < 500, $"validation took {best} ms (budget 500 ms)");
    }
}
