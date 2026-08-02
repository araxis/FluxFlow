namespace FluxFlow.Composition.Authoring;

internal sealed class AuthoringScope
{
    public bool IsBuilt { get; private set; }

    public void EnsureMutable()
    {
        if (IsBuilt)
        {
            throw new InvalidOperationException(
                "The application definition has already been built and cannot be changed.");
        }
    }

    public void Complete() => IsBuilt = true;
}
