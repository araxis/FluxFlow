namespace FluxFlow.Authoring;

/// <summary>Reads the embedded README beside a component description.</summary>
public static class ComponentDocumentation
{
    public static string Read(Type descriptionType)
    {
        using var stream = descriptionType.Assembly.GetManifestResourceStream($"{descriptionType.Namespace}.README.md");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
