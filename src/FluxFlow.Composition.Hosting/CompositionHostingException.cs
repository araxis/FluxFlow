using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

public sealed class CompositionHostingException : Exception
{
    public CompositionHostingException(
        string message,
        IReadOnlyList<CompositionDiagnostic> diagnostics,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<CompositionDiagnostic> Diagnostics { get; }
}
