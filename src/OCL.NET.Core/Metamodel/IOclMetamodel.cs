using OCL.NET.Core.Values;

namespace OCL.NET.Core.Metamodel;

/// <summary>
/// The seam between the metamodel-agnostic OCL core and a concrete model
/// representation. Everything the interpreter needs to know about *what the model
/// elements are* flows through this interface — type identity, type hierarchy,
/// and property navigation.
///
/// The example implementation binds to CAEX / Aml.Engine (project
/// <c>OCL.NET.Caex</c>), mapping OCL type names onto <c>SupportedRoleClass</c> /
/// <c>RefBaseSystemUnitPath</c> and OCL navigation like <c>self.a.b</c> onto the
/// CAEX attribute hierarchy. Keeping this an interface is what makes the engine
/// reusable across domains: a different binding (EMF/Ecore, plain POCOs, …) is a
/// different implementation, not a fork of the core.
///
/// Implementations MUST compare element identity stably (e.g. by AML ID), because
/// some model APIs — Aml.Engine in particular — hand out non-reference-stable
/// wrapper instances for the same underlying element.
/// </summary>
public interface IOclMetamodel
{
    /// <summary><c>e.oclIsKindOf(T)</c> — true if <paramref name="element"/> is an instance of <paramref name="typeName"/> or a subtype.</summary>
    bool IsKindOf(object element, string typeName);

    /// <summary><c>e.oclIsTypeOf(T)</c> — true if <paramref name="element"/> is exactly an instance of <paramref name="typeName"/> (no subtypes).</summary>
    bool IsTypeOf(object element, string typeName);

    /// <summary><c>e.oclType()</c> — the most specific OCL type name of <paramref name="element"/>.</summary>
    string TypeOf(object element);

    /// <summary>
    /// Whether <paramref name="typeName"/> names a type of this metamodel. Lets the
    /// interpreter evaluate a bare type name as a type value (e.g. the elements of
    /// <c>Sequence{FPD_Product, FPD_Energy}</c>, later passed to <c>oclIsKindOf(t)</c>
    /// through an iterator variable). The default knows no types, which keeps the
    /// previous behaviour for bindings that do not implement it.
    /// </summary>
    bool KnowsType(string typeName) => false;

    /// <summary>
    /// Whether <see cref="KnowsType"/> lists every type of this metamodel. Then a type
    /// operation with a name it does not know (a misspelt type in a rule) is undefined
    /// (invalid) instead of quietly false, so the rule fails loud. The default keeps
    /// the previous behaviour for bindings without such a list.
    /// </summary>
    bool DeclaresAllTypes => false;

    /// <summary>
    /// <c>element.propertyName</c> — navigate a property. Returns <see cref="OclValue.Void"/>
    /// for an absent single-valued property, an empty collection for an absent
    /// multi-valued one, never a CLR null.
    /// </summary>
    OclValue GetProperty(object element, string propertyName);
}
