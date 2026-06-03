namespace VPDecoder;

internal sealed class Vp8BoolReaderException : Exception
{
    public Vp8BoolReaderException(Vp8DecodeDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public Vp8DecodeDiagnostic Diagnostic { get; }
}
