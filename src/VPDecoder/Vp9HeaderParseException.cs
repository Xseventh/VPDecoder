namespace VPDecoder;

internal sealed class Vp9HeaderParseException : Exception
{
    public Vp9HeaderParseException(Vp9DecodeDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public Vp9DecodeDiagnostic Diagnostic { get; }
}
