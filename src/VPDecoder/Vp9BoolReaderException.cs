namespace VPDecoder;

internal sealed class Vp9BoolReaderException : Exception
{
    public Vp9BoolReaderException(Vp9DecodeDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public Vp9DecodeDiagnostic Diagnostic { get; }
}
