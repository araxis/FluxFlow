using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Options;

internal static class StorageOptionValidation
{
    public static string? NormalizeCollection(string? value)
        => StorageContractNormalization.NormalizeOptional(value);

    public static StorageWriteMode ValidateMode(StorageWriteMode value)
        => Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Storage write mode is not supported.");

    public static int ValidateBoundedCapacity(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "boundedCapacity must be greater than zero.");

    public static int ValidateOffset(int value)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Offset cannot be negative.");

    public static int ValidateLimit(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Limit must be greater than zero.");
}
