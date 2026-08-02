using Xunit;

namespace FluxFlow.Release.Tests;

[CollectionDefinition(ReleaseProcessCollection.Name)]
public sealed class ReleaseProcessCollection
{
    public const string Name = "Release process owners";
}
