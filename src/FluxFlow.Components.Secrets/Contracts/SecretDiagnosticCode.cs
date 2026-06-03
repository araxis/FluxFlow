namespace FluxFlow.Components.Secrets.Contracts;

public enum SecretDiagnosticCode
{
    InvalidSecret = 1,
    MissingSecret = 2,
    DuplicateSecret = 3,
    AmbiguousSecret = 4,
    KindMismatch = 5,
    AccessDenied = 6,
    ResolveFailed = 7,
    MissingSecretReference = 8
}
