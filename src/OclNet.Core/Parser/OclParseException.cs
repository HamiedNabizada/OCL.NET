using OclNet.Core.Ast;

namespace OclNet.Core.Parser;

/// <summary>
/// Thrown when OCL source text cannot be parsed. Carries the <see cref="Location"/>
/// of the first syntax error so callers can point the user at the offending
/// constraint line/column.
/// </summary>
public sealed class OclParseException : Exception
{
    public SourceLocation Location { get; }

    public OclParseException(string message, SourceLocation location)
        : base($"OCL parse error at {location}: {message}")
    {
        Location = location;
    }
}
