using FluxFlow.Components.Designer;
using FluxFlow.Composition.Links;

namespace FluxFlow.DesignerHost;

/// <summary>
/// Maps metadata validation errors and application-link diagnostics into the shared
/// <see cref="ValidationMessageModel"/> shape so a status view renders one list.
/// Both sources report build-blocking problems, so everything maps to
/// <see cref="ValidationSeverity.Error"/>.
/// </summary>
public static class ValidationMessageMapper
{
    public static ValidationMessageModel FromMetadataError(
        DesignerMetadataValidationError error,
        string? componentType = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ValidationMessageModel
        {
            Severity = ValidationSeverity.Error,
            Source = ValidationSource.Metadata,
            Message = string.IsNullOrWhiteSpace(error.Path)
                ? error.Message
                : $"{error.Path}: {error.Message}",
            ComponentType = componentType
        };
    }

    public static ValidationMessageModel FromLinkDiagnostic(ApplicationLinkDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new ValidationMessageModel
        {
            Severity = ValidationSeverity.Error,
            Source = ValidationSource.Composition,
            Message = diagnostic.Message,
            ComponentName = diagnostic.ComponentName
        };
    }

    public static IReadOnlyList<ValidationMessageModel> FromLinkDiagnostics(
        IEnumerable<ApplicationLinkDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Select(FromLinkDiagnostic).ToArray();
    }
}
