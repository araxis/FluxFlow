using System.Text.Json;

namespace FluxFlow.Engine.Definitions;

public sealed record DashboardDefinition
{
    private DashboardLayoutDefinition? _layout = new();
    private Dictionary<string, DashboardWidgetDefinition>? _widgets = [];

    public DashboardLayoutDefinition Layout
    {
        get => _layout ??= new();
        init => _layout = value ?? new DashboardLayoutDefinition();
    }

    public Dictionary<string, DashboardWidgetDefinition> Widgets
    {
        get => _widgets ??= [];
        init => _widgets = value ?? [];
    }
}

public sealed record DashboardLayoutDefinition
{
    private List<DashboardGridTrackDefinition>? _columns = [DashboardGridTrackDefinition.Star()];
    private List<DashboardGridTrackDefinition>? _rows = [DashboardGridTrackDefinition.Star()];
    private List<double>? _columnPadding = [];
    private List<double>? _rowPadding = [];
    private Dictionary<string, DashboardCellDefinition>? _cells = [];

    public List<DashboardGridTrackDefinition> Columns
    {
        get => _columns ??= [];
        init => _columns = value ?? [];
    }

    public List<DashboardGridTrackDefinition> Rows
    {
        get => _rows ??= [];
        init => _rows = value ?? [];
    }

    public List<double> ColumnPadding
    {
        get => _columnPadding ??= [];
        init => _columnPadding = value ?? [];
    }

    public List<double> RowPadding
    {
        get => _rowPadding ??= [];
        init => _rowPadding = value ?? [];
    }

    public Dictionary<string, DashboardCellDefinition> Cells
    {
        get => _cells ??= [];
        init => _cells = value ?? [];
    }
}

public sealed record DashboardGridTrackDefinition
{
    public DashboardGridTrackUnit Unit { get; init; } = DashboardGridTrackUnit.Star;
    public double Value { get; init; } = 1;

    public static DashboardGridTrackDefinition Fixed(double value)
        => new()
        {
            Unit = DashboardGridTrackUnit.Fixed,
            Value = value
        };

    public static DashboardGridTrackDefinition Percent(double value)
        => new()
        {
            Unit = DashboardGridTrackUnit.Percent,
            Value = value
        };

    public static DashboardGridTrackDefinition Star(double value = 1)
        => new()
        {
            Unit = DashboardGridTrackUnit.Star,
            Value = value
        };

    public static DashboardGridTrackDefinition Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Dashboard grid track size cannot be empty.");
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith('*'))
        {
            var coefficient = trimmed[..^1].Trim();
            return Star(ParsePositiveDouble(string.IsNullOrWhiteSpace(coefficient) ? "1" : coefficient, "star track"));
        }

        if (trimmed.EndsWith('%'))
        {
            return Percent(ParsePositiveDouble(trimmed[..^1], "percent track"));
        }

        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            return Fixed(ParsePositiveDouble(trimmed[..^2], "fixed track"));
        }

        return Fixed(ParsePositiveDouble(trimmed, "fixed track"));
    }

    public string ToSizeString()
        => Unit switch
        {
            DashboardGridTrackUnit.Fixed => Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            DashboardGridTrackUnit.Percent => $"{Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}%",
            DashboardGridTrackUnit.Star => Value == 1 ? "*" : $"{Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}*",
            _ => throw new InvalidOperationException($"Unsupported dashboard grid track unit '{Unit}'.")
        };

    private static double ParsePositiveDouble(string value, string label)
    {
        if (!double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed <= 0 ||
            double.IsNaN(parsed) ||
            double.IsInfinity(parsed))
        {
            throw new FormatException($"Dashboard grid {label} size must be a positive finite number.");
        }

        return parsed;
    }
}

public enum DashboardGridTrackUnit
{
    Fixed,
    Percent,
    Star
}

public sealed record DashboardCellDefinition
{
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public string? Widget { get; init; }
}

public sealed record DashboardWidgetDefinition
{
    private Dictionary<string, JsonElement>? _configuration = [];

    public string Type { get; init; } = string.Empty;

    public Dictionary<string, JsonElement> Configuration
    {
        get => _configuration ??= [];
        init => _configuration = value ?? [];
    }
}
