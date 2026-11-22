namespace FluxFlow.Environments;

public sealed record EnvironmentProviderDescription(string Name, string ResourceType, string Title, string Image)
{
    public string StartupTitle { get; init; } = "Startup files";
    public IReadOnlyList<ServiceStartupFile> StartupFiles { get; init; } = [];
    public string AccessTitle { get; init; } = "Broker access";
    public string? AccessHelp { get; init; }
    public IReadOnlyList<ServiceVariable> Variables { get; init; } = [];
    public ServiceAccessFields? Access { get; init; }
    public ServiceDocument? Document { get; init; }
}

public sealed record ServiceVariable(string Key, string Label, string Description, string DefaultValue = "")
{
    public string Read(ResourceBinding binding) => binding.Variables.GetValueOrDefault(Key, DefaultValue);

    public ResourceBinding Write(ResourceBinding binding, string? value)
    {
        if (binding.Profile?.SetupLocked == true) return binding;
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var exists = binding.Variables.TryGetValue(Key, out var previous);
        if (normalized is null ? !exists : normalized == previous) return binding;

        var variables = new Dictionary<string, string>(binding.Variables, StringComparer.Ordinal);
        if (normalized is null) variables.Remove(Key);
        else variables[Key] = normalized;
        return binding with { Variables = variables };
    }
}

public sealed record ServiceAccessFields(string ReadLabel, string WriteLabel, string PermissionHelp, int Lines, string SourceHelp)
{
    public IServiceAccessEdits? Edits { get; init; }

    public BrokerAccess Read(ResourceBinding binding, string provider) =>
        binding.BrokerAccess.TryGetValue(provider, out var saved) ? Copy(saved) : new();

    // The rows are immutable, but a draft must not share its editable list with a saved binding.
    private static BrokerAccess Copy(BrokerAccess access) => access with { Users = [.. access.Users] };

    public ResourceBinding Write(ResourceBinding binding, string provider, BrokerAccess value)
    {
        if (binding.Profile?.SetupLocked == true || Edits is null) return binding;
        var previous = binding.BrokerAccess.GetValueOrDefault(provider) ?? new();
        // Record equality compares the user list by reference; an unchanged edit may supply a new list.
        if ((previous with { Users = value.Users }) == value && previous.Users.SequenceEqual(value.Users)) return binding;
        return binding with { BrokerAccess = new(binding.BrokerAccess, StringComparer.Ordinal) { [provider] = Copy(value) } };
    }
}

/// <summary>Edits return a new access configuration and leave the saved input unchanged. Add appends the new user.</summary>
public interface IServiceAccessEdits
{
    BrokerAccess Add(BrokerAccess access);
    BrokerAccess Replace(BrokerAccess access, int index, BrokerUser user);
    BrokerAccess Remove(BrokerAccess access, int index);
    BrokerAccess SelectClient(BrokerAccess access, string username);
    BrokerAccess SetCredentials(BrokerAccess access, string username, string password);
}

public sealed record ServiceStartupFile(string Label, string Path, string Help, Func<ResourceBinding, string> DefaultContent,
    ServiceDocumentKind? Format = null, bool GeneratedWithUsers = false)
{
    public ServiceTextPreview? Preview { get; init; }
    public ServiceDirectiveSchema? Directives { get; init; }
    public ServiceNameList? Names { get; init; }
    public ServiceCollectionSchema? Templates { get; init; }

    public bool HasOverride(ResourceBinding binding) => binding.Files.ContainsKey(Path);
    public string Read(ResourceBinding binding) => binding.Files.TryGetValue(Path, out var saved) ? saved : DefaultContent(binding);
    public bool IsGenerated(ResourceBinding binding, string? provider = null) =>
        GeneratedWithUsers && binding.BrokerAccess.GetValueOrDefault(provider ?? binding.Provider)?.Enabled == true;

    public ResourceBinding Write(ResourceBinding binding, string text, bool import = false, string? provider = null)
    {
        if (binding.Profile?.SetupLocked == true || IsGenerated(binding, provider)) return binding;
        // An explicit import pins the text even when it matches the current provider default.
        var previous = import ? binding.Files.GetValueOrDefault(Path) : Read(binding);
        if (text == previous) return binding;
        if (Format == ServiceDocumentKind.ObjectCollections) ServiceCollectionSchema.ValidateSource(text);
        return binding with { Files = new(binding.Files, StringComparer.Ordinal) { [Path] = text } };
    }

    public ResourceBinding Reset(ResourceBinding binding)
    {
        if (binding.Profile?.SetupLocked == true || !HasOverride(binding)) return binding;
        var files = new Dictionary<string, string>(binding.Files, StringComparer.Ordinal);
        files.Remove(Path);
        return binding with { Files = files };
    }
}
/// <summary>Provider-defined fields and validation for ordered directive lines.</summary>
public sealed record ServiceDirectiveSchema(string Items, string Help, Func<DocumentPair> Create,
    Func<DocumentPair, IReadOnlyList<DocumentField>> Fields, Action<DocumentPair> Validate);

/// <summary>A display-only projection. The source text remains the editable document.</summary>
public sealed record ServiceTextPreview(string Label, Func<string, IReadOnlyList<string>> Read);

public enum ServiceDocumentKind { Variables, Files, KeyValueLines, AtomList, ObjectCollections, DirectiveLines, TextPreview, NameList = AtomList }
