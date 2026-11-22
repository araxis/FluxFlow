using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Components.DataGeneration;

public sealed record DataGeneratorConfiguration : ComponentConfiguration
{
    public ExpressionSource? Expression { get; set; }
    public string Locale { get; set; } = "en";
    public int OutputCapacity { get; set; } = 128;

    public static string? LocaleError(string? locale) => string.IsNullOrEmpty(locale) || Bogus.Database.LocaleResourceExists(locale)
        ? null : "Enter a supported Bogus locale, for example en or sv, or clear to use en.";

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Expression))]
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Expression);
        ArgumentException.ThrowIfNullOrWhiteSpace(Locale);
        if (LocaleError(Locale) is { } error) throw new ArgumentException(error, nameof(Locale));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }
}
