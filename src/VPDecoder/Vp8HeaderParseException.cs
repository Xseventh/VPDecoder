namespace VPDecoder;

internal sealed class Vp8HeaderParseException : Exception
{
    public Vp8HeaderParseException(Vp8DecodeDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public Vp8DecodeDiagnostic Diagnostic { get; }
}
