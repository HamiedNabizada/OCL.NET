using System.Diagnostics;
using Aml.Engine.CAEX;
using OclNet.Caex;
using OclNet.Core.Validation;
using OclNet.Core.Parser;
using Xunit;
using Xunit.Abstractions;

namespace OclNet.Caex.Tests;

/// <summary>
/// Performance acceptance: 50 rules over a ~100-InternalElement model validate in
/// well under 500 ms once compiled (AST caching). Builds a synthetic CAEX model in
/// memory and times a warm <see cref="OclValidator.Validate(IOclModel, CompiledRuleSet)"/>.
/// </summary>
public class PerformanceTests
{
    private readonly ITestOutputHelper _output;
    public PerformanceTests(ITestOutputHelper output) => _output = output;

    private const string Suc = "VDI_FPD_SystemUnitClassLib/";

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

            for (var i = 0; i < elementsPerProcess; i++)
            {
                var type = i % 2 == 0 ? "FPD_Product" : "FPD_ProcessOperator";
                var e = process.InternalElement.Append($"E{p}_{i}");
                e.ID = $"id-{id++}";
                e.RefBaseSystemUnitPath = Suc + type;
                var ident = e.Attribute.Append("Identification");
                ident.Attribute.Append("longName").Value = $"name{p}_{i}";
            }
        }
        return doc;
    }

    private static List<OclRuleSpec> FiftyRules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-pure-rules.ocl");
        var blocks = File.ReadAllText(path).Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(b => b.TrimStart().StartsWith("context"))
            .ToList();
        return Enumerable.Range(0, 50)
            .Select(i => new OclRuleSpec($"R{i}", ValidationSeverity.Warning, "perf", blocks[i % blocks.Count]))
            .ToList();
    }

    [Fact]
    public void Fifty_rules_over_hundred_elements_validate_under_500ms()
    {
        var doc = BuildModel(processes: 5, elementsPerProcess: 19); // 5 * (1 + 1 + 19) ≈ 105 IEs
        var model = new CaexMetamodel(doc);
        var validator = new OclValidator();
        var rules = FiftyRules();
        var helpers = new OclParser().ParseDefinitions(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "vdi3682-helpers.ocl")));

        var compiled = validator.Compile(rules, helpers);
        validator.Validate(model, compiled); // warm up (JIT)

        var sw = Stopwatch.StartNew();
        validator.Validate(model, compiled);
        sw.Stop();

        _output.WriteLine($"50 rules / ~105 IEs: {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 500, $"validation took {sw.ElapsedMilliseconds} ms (budget 500 ms)");
    }
}
