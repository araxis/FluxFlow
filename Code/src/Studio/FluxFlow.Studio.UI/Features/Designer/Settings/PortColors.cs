namespace FluxFlow.Studio.UI.Features.Designer.Settings;

public sealed record PortColors(string? Input = null, string? Output = null, string? Events = null, string? Error = null)
{
    public static readonly string[] Roles = ["Input", "Output", "Events", "Error"];

    public string? Get(string role) => role switch
    {
        "Input" => Input, "Output" => Output, "Events" => Events, "Error" => Error,
        _ => throw new ArgumentException("Unknown port role.", nameof(role))
    };

    public PortColors With(string role, string? color)
    {
        ValidateColor(color);
        return role switch
        {
            "Input" => this with { Input = color }, "Output" => this with { Output = color },
            "Events" => this with { Events = color }, "Error" => this with { Error = color },
            _ => throw new ArgumentException("Unknown port role.", nameof(role))
        };
    }

    public void Validate()
    {
        foreach (var role in Roles) ValidateColor(Get(role));
    }

    public PortColors Resolve(PortColors defaults) => new(Input ?? defaults.Input, Output ?? defaults.Output,
        Events ?? defaults.Events, Error ?? defaults.Error);

    public static void ValidateColor(string? color)
    {
        if (color is not null && (color.Length is not (4 or 7 or 9) || color[0] != '#' ||
            color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF")))
            throw new ArgumentException("Colors must be hexadecimal, for example #087f80.");
    }

    public string ToStyle(PortColors defaults) => string.Join(";", Roles.Select(role =>
        (Role: role, Color: Get(role) ?? defaults.Get(role))).Where(item => item.Color is not null)
        .Select(item => $"--studio-port-{item.Role.ToLowerInvariant()}:{item.Color}"));
}
