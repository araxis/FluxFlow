using FluxFlow.Components.Metrics.Contracts;

namespace FluxFlow.Components.Metrics.Options;

internal static class MetricsOptionValidation
{
    public static double ValidateRateWindowSeconds(double value)
        => double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "metrics.aggregate option 'rateWindowSeconds' must be a finite value greater than zero.");

    public static int ValidateBoundedCapacity(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "metrics.aggregate option 'boundedCapacity' must be greater than zero.");

    public static int ValidateMaxGroups(int value)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "metrics.aggregate option 'maxGroups' must be zero or greater.");

    public static string? NormalizeOptional(string? value)
        => MetricsContractNormalization.NormalizeOptional(value);
}
