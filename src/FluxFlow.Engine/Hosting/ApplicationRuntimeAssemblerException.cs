using FluxFlow.Composition.Links;

namespace FluxFlow.Engine.Hosting;

public sealed class ApplicationRuntimeAssemblerException : Exception
{
    public ApplicationRuntimeAssemblerException(string message)
        : base(message)
    {
        LinkDiagnostics = [];
    }

    public ApplicationRuntimeAssemblerException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        LinkDiagnostics = [];
    }

    public ApplicationRuntimeAssemblerException(
        IReadOnlyList<ApplicationLinkDiagnostic> linkDiagnostics)
        : base(CreateMessage(linkDiagnostics))
    {
        ArgumentNullException.ThrowIfNull(linkDiagnostics);
        LinkDiagnostics = linkDiagnostics.ToArray();
    }

    public IReadOnlyList<ApplicationLinkDiagnostic> LinkDiagnostics { get; }

    private static string CreateMessage(IReadOnlyList<ApplicationLinkDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Count == 0
            ? "Canonical application link compilation failed."
            : $"Canonical application link compilation failed: {diagnostics[0].Message}";
    }
}
