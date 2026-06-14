using Aml.Engine.CAEX;

namespace OCL.NET.Caex;

/// <summary>
/// VDI 3682 / FPD type registry. Resolves the OCL type of the CAEX object kinds an
/// FPD model uses:
/// <list type="bullet">
///   <item><b>InternalElement</b> — concrete type is the last segment of its
///   <c>RefBaseSystemUnitPath</c> (<c>…/FPD_Product</c> → <c>FPD_Product</c>); when
///   that is absent or unknown, the element's <c>RoleRequirements</c> are consulted
///   (role-typed AML files are legitimate — SUC references are optional in CAEX);</item>
///   <item><b>ExternalInterface</b> — likewise from <c>RefBaseClassPath</c>
///   (<c>…/FPD_FlowOut</c>);</item>
///   <item><b>InternalLink</b> (connection) — the flow type is read from its
///   A-side interface (an <c>FPD_FlowOut</c> interface ⇒ an <c>FPD_Flow</c> link).</item>
/// </list>
/// The abstract supertypes (<c>FPD_State</c>, <c>FPD_Object</c>, <c>FPD_Connection</c>)
/// have no instances of their own and are answered through the hierarchy. Constants
/// mirror the standard's FPD libraries; kept here so the engine stays self-contained
/// (see <see cref="ICaexTypeRegistry"/> for the swap seam).
/// </summary>
public sealed class FpdTypeRegistry : ICaexTypeRegistry
{
    public const string SystemUnitClassLib = "VDI_FPD_SystemUnitClassLib";
    public const string InterfaceClassLib = "VDI_FPD_InterfaceClassLib";

    /// <summary>The FPD diagram-interchange bounds attribute type (suffix of its RefAttributeType path).</summary>
    public const string BoundsAttributeTypeSuffix = "/FPD_Bounds";

    /// <summary>The conventional name of the bounds attribute as emitted by the FPB mapper.</summary>
    public const string BoundsAttributeName = "ViewInformation";

    public string ProjectTypeName => "FPD_Project";
    public string ProcessTypeName => "FPD_Process";
    public string BoundsTypeName => "Bounds";

    // Subtype → direct supertype. Roots map to null.
    private static readonly Dictionary<string, string?> Hierarchy = new(StringComparer.Ordinal)
    {
        // Model root (the document/project scope — not an InternalElement)
        ["FPD_Project"] = null,
        // Elements
        ["FPD_Object"] = null,
        ["FPD_State"] = "FPD_Object",
        ["FPD_Product"] = "FPD_State",
        ["FPD_Energy"] = "FPD_State",
        ["FPD_Information"] = "FPD_State",
        ["FPD_ProcessOperator"] = "FPD_Object",
        ["FPD_TechnicalResource"] = "FPD_Object",
        ["FPD_SystemLimit"] = "FPD_Object",
        ["FPD_Process"] = null,
        // Connections
        ["FPD_Connection"] = null,
        ["FPD_Flow"] = "FPD_Connection",
        ["FPD_ParallelFlow"] = "FPD_Connection",
        ["FPD_AlternativeFlow"] = "FPD_Connection",
        ["FPD_Usage"] = "FPD_Connection",
        // Interface classes (flow direction markers)
        ["FPD_FlowOut"] = null,
        ["FPD_FlowIn"] = null,
        ["FPD_ParallelFlowOut"] = null,
        ["FPD_ParallelFlowIn"] = null,
        ["FPD_AlternativeFlowOut"] = null,
        ["FPD_AlternativeFlowIn"] = null,
        // The geometry box exposed via self.bounds (context of the def: helpers)
        ["Bounds"] = null,
    };

    // A connection's flow type, keyed by its A-side (outgoing) interface class.
    private static readonly Dictionary<string, string> FlowTypeByOutInterface = new(StringComparer.Ordinal)
    {
        ["FPD_FlowOut"] = "FPD_Flow",
        ["FPD_ParallelFlowOut"] = "FPD_ParallelFlow",
        ["FPD_AlternativeFlowOut"] = "FPD_AlternativeFlow",
        ["FPD_Usage"] = "FPD_Usage",
    };

    public bool KnowsType(string oclTypeName) => Hierarchy.ContainsKey(oclTypeName);

    public string TypeOf(object caexObject) => caexObject switch
    {
        InternalElementType ie => ElementType(ie),
        ExternalInterfaceType iface => Known(LocalName(iface.RefBaseClassPath)),
        InternalLinkType link => FlowType(link),
        _ => "",
    };

    public bool IsTypeOf(object caexObject, string oclTypeName) => TypeOf(caexObject) == oclTypeName;

    public bool IsKindOf(object caexObject, string oclTypeName)
    {
        string? current = TypeOf(caexObject);
        while (!string.IsNullOrEmpty(current))
        {
            if (current == oclTypeName) return true;
            current = Hierarchy.TryGetValue(current, out var parent) ? parent : null;
        }
        return false;
    }

    public bool IsBoundsAttribute(AttributeType attribute) =>
        attribute.RefAttributeType?.EndsWith(BoundsAttributeTypeSuffix, StringComparison.Ordinal) == true ||
        string.Equals(attribute.Name, BoundsAttributeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>SUC path first; RoleRequirements as fallback for role-typed AML files.</summary>
    private static string ElementType(InternalElementType ie)
    {
        var bySuc = Known(LocalName(ie.RefBaseSystemUnitPath));
        if (bySuc.Length > 0) return bySuc;

        foreach (var rr in ie.RoleRequirements)
        {
            var byRole = Known(LocalName(rr.RefBaseRoleClassPath));
            if (byRole.Length > 0) return byRole;
        }
        return "";
    }

    private static string FlowType(InternalLinkType link)
    {
        var outInterface = LocalName((link.AInterface as ExternalInterfaceType)?.RefBaseClassPath);
        return FlowTypeByOutInterface.TryGetValue(outInterface, out var flow) ? flow : "";
    }

    private static string Known(string local) => Hierarchy.ContainsKey(local) ? local : "";

    private static string LocalName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}
