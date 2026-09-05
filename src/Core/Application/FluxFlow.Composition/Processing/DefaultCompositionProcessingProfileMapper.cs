namespace FluxFlow.Composition;

public sealed class DefaultCompositionProcessingProfileMapper : ICompositionProcessingProfileMapper
{
    public CompositionProcessingSettings Map(CompositionProcessingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Mode == CompositionProcessingMode.Sequential &&
            profile.Order == CompositionProcessingOrder.Relaxed)
        {
            throw new InvalidOperationException(
                "A sequential processing profile cannot relax ordering.");
        }

        return new CompositionProcessingSettings(
            profile.Buffer switch
            {
                CompositionProcessingBuffer.Small => 32,
                CompositionProcessingBuffer.Standard => 128,
                CompositionProcessingBuffer.Large => 512,
                _ => throw new ArgumentOutOfRangeException(nameof(profile), "Unknown processing buffer.")
            },
            profile.Mode == CompositionProcessingMode.Parallel ? 4 : 1,
            profile.Order == CompositionProcessingOrder.Preserve);
    }
}
