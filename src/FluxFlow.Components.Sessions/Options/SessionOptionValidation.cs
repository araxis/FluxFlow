using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.Components.Sessions.Options;

internal static class SessionOptionValidation
{
    public static string? Normalize(string? value)
        => SessionContractNormalization.NormalizeOptional(value);

    public static Dictionary<string, string> CopyMap(Dictionary<string, string>? source)
        => SessionContractNormalization.CopyMap(source);

    public static int ValidateBoundedCapacity(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "boundedCapacity must be greater than zero.");

    public static int ValidateLimit(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "limit must be greater than zero.");

    public static long? ValidateStartSequence(long? value)
        => value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "startSequence must be greater than zero when set.");

    public static int? ValidateMaxMessages(int? value)
        => value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "maxMessages must be greater than zero when set.");

    public static double ValidateFixedIntervalMilliseconds(double value)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "fixedIntervalMilliseconds must be zero or greater.");

    public static double ValidateSpeedMultiplier(double value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "speedMultiplier must be greater than zero.");

    public static SessionReplayMode ValidateReplayMode(SessionReplayMode value)
        => Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "mode must be a supported session replay mode.");
}
