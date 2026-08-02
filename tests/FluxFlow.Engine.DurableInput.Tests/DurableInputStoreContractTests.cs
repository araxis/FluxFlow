namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Runs the reusable provider contract against the executable test-only store.
/// Product providers inherit the same contract in their own integration tests.
/// </summary>
public sealed class DurableInputStoreContractTests : DurableInputStoreConformanceTests
{
    protected override ValueTask<DurableInputStoreTestContext> CreateStoreAsync()
        => ValueTask.FromResult(DurableInputStoreTestContext.Create(new DurableInputTestStore()));
}
