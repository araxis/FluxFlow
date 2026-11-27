using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Configuration.Sources;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, null);

    public static int Run(string[] args, string? defaultFeed)
    {
        var command = defaultFeed is null ? "dotnet fluxflow" : "fluxflow";
        var mappings = defaultFeed is null ? "<mapping.nupkg> [...]" : "[<mapping.nupkg> ...]";
        var generateUsage = $"Usage: {command} generate <application.json> <App.cs> [--framework net10.0|net11.0] [--runtime win-x64|linux-x64] {mappings}";
        var feedOption = defaultFeed is null ? "--feed <folder>" : "[--feed <folder>]";
        var publishUsage = $"Usage: {command} publish <application.json> --framework net10.0|net11.0 [--runtime win-x64|linux-x64] {feedOption} --output <executable>";
        if (args is ["--help" or "-h"])
        {
            Console.WriteLine(generateUsage);
            Console.WriteLine(publishUsage);
            Console.WriteLine(ServiceCommand.Usage(command));
            return 0;
        }
        if (args is ["publish", ..]) return PublishCommand.RunAsync(args, defaultFeed, publishUsage).GetAwaiter().GetResult();
        if (args is ["service", ..]) return ServiceCommand.Run(args, command, defaultFeed);
        if (args is ["generate", "--help" or "-h"])
        {
            Console.WriteLine(generateUsage);
            return 0;
        }
        if (args.Length < (defaultFeed is null ? 4 : 3) || args[0] != "generate")
        {
            Console.Error.WriteLine(generateUsage);
            return 2;
        }

        try
        {
            var configPath = Path.GetFullPath(args[1]);
            var outputPath = Path.GetFullPath(args[2]);
            if (Path.GetExtension(outputPath) != ".cs")
                throw new ArgumentException("The generated file must have a .cs extension.");
            string? framework = null;
            var firstPackage = 3;
            if (args.ElementAtOrDefault(firstPackage) == "--framework")
            {
                if (args.ElementAtOrDefault(4) is not ("net10.0" or "net11.0")) throw new ArgumentException(generateUsage);
                framework = args[4];
                firstPackage = 5;
            }
            string? runtime = null;
            if (args.ElementAtOrDefault(firstPackage) == "--runtime")
            {
                if (args.ElementAtOrDefault(firstPackage + 1) is not ("win-x64" or "linux-x64"))
                    throw new ArgumentException(generateUsage);
                runtime = args[firstPackage + 1];
                firstPackage += 2;
            }
            var packages = firstPackage < args.Length ? args.Skip(firstPackage)
                : defaultFeed is not null ? Directory.GetFiles(defaultFeed, "*.nupkg").Order(StringComparer.Ordinal)
                : throw new ArgumentException(generateUsage);
            var source = GenerateFromFile(configPath, packages, framework, runtime);
            File.WriteAllText(outputPath, source);
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or FormatException or JsonException or System.Xml.XmlException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static string GenerateFromFile(string configPath, IEnumerable<string> mappingFiles, string? framework, string? runtime = null)
        => CreateGenerator(mappingFiles, framework, runtime)(configPath);

    internal static Func<string, string> CreateGenerator(IEnumerable<string> mappingFiles, string? framework, string? runtime)
    {
        var catalog = ReadMappings(mappingFiles, out var packages);
        return configPath =>
        {
            using var stream = File.OpenRead(configPath);
            using var config = JsonDocument.Parse(stream);
            return Generate(config.RootElement, Path.GetDirectoryName(configPath)!, catalog, packages, framework, runtime);
        };
    }

    private static Dictionary<(string Type, bool Resource), PackageMapping> ReadMappings(IEnumerable<string> packages, out ILookup<string, string> versions)
    {
        var catalog = new Dictionary<(string Type, bool Resource), PackageMapping>();
        var identities = new List<(string Id, string Version)>();
        foreach (var path in packages)
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("fluxflow/csharp-mapping.json");
            using var spec = archive.Entries.Single(e => e.FullName == e.Name && e.Name.EndsWith(".nuspec")).Open();
            var document = XDocument.Load(spec);
            var ns = document.Root!.Name.Namespace;
            var metadata = document.Root.Element(ns + "metadata");
            var id = (string?)metadata?.Element(ns + "id");
            var version = (string?)metadata?.Element(ns + "version");
            if (id is null || version is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9._-]+\z") ||
                !Regex.IsMatch(version, @"\A[0-9][A-Za-z0-9.+-]*\z"))
                throw new InvalidDataException($"Invalid package or C# mapping in '{path}'.");
            identities.Add((id, version));
            if (entry is null) continue;
            if (archive.Entries.Count(item => item.FullName == entry.FullName) != 1)
                throw new InvalidDataException($"Multiple C# mappings in '{path}'.");
            using var manifest = entry.Open();
            using var declarations = JsonDocument.Parse(manifest);
            var root = declarations.RootElement;
            Mapping?[] mappings = root.ValueKind == JsonValueKind.Array
                ? root.Deserialize<Mapping?[]>(JsonSerializerOptions.Web)!
                : new Mapping?[] { root.Deserialize<Mapping>(JsonSerializerOptions.Web) };
            if (mappings.Length == 0) throw new InvalidDataException($"Empty C# mapping in '{path}'.");
            foreach (var mapping in mappings)
            {
                if (mapping is null || !Identifier(mapping.Method) || string.IsNullOrEmpty(mapping.Namespace) ||
                    !mapping.Namespace.Split('.').All(Identifier) || string.IsNullOrWhiteSpace(mapping.Type) ||
                    mapping.Arguments is null || mapping.SettingsType is not null && !QualifiedIdentifier(mapping.SettingsType) ||
                    mapping.Arguments.Any(a => !ValidArgument(a)) || mapping.HostMethod is not null && !Identifier(mapping.HostMethod) ||
                    mapping.Platform is not null && !Regex.IsMatch(mapping.Platform, @"\Awindows[0-9]+(\.[0-9]+){1,3}\z") ||
                    mapping.ConfigureServices is not null && !QualifiedIdentifier(mapping.ConfigureServices) ||
                    mapping.Inputs is null || mapping.Outputs is null ||
                    mapping.Alternatives?.Any(v => v is null || !Identifier(v.Method) || string.IsNullOrWhiteSpace(v.WhenPresent) ||
                        v.Arguments is null || v.Arguments.Any(a => !ValidArgument(a))) == true)
                    throw new InvalidDataException($"Invalid C# mapping in '{path}'.");
                if (!catalog.TryAdd((mapping.Type, mapping.Resource), new(mapping, id, version)))
                    throw new InvalidDataException($"Select one runtime package for component '{mapping.Type}'.");
            }
        }
        versions = identities.Distinct().ToLookup(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);
        return catalog;
    }

    private static bool Identifier(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z");
    private static bool ValidArgument(Argument? arg) => arg is not null && Identifier(arg.Name)
        && (arg.ConstructorParameter is null || Identifier(arg.ConstructorParameter))
        && (arg.Kind == "objectFactory" ? arg.Factory is not null && QualifiedIdentifier(arg.Factory) : arg.Factory is null)
        && (arg.Kind == "flags"
            ? arg.EnumType is not null && QualifiedIdentifier(arg.EnumType) && arg.Flags is { Count: > 0 }
                && arg.Flags.All(f => Identifier(f.Key) && f.Value > 0)
            : arg.EnumType is null || QualifiedIdentifier(arg.EnumType) && arg.Values is { Length: > 0 } && arg.Values.All(Identifier))
        && (arg.Kind is not ("object" or "objectArray" or "oneOrManyObjects") || arg.SettingsType is not null && QualifiedIdentifier(arg.SettingsType)
            && arg.Fields is not null && arg.Fields.All(ValidArgument) && (arg.ScalarField is null || arg.Fields.Any(f => f.Name == arg.ScalarField)));

    private static bool QualifiedIdentifier(string value) => value.Split('.').All(Identifier);
    private static string Literal(string value) => JsonSerializer.Serialize(value);

    private static string Generate(JsonElement config, string directory, Dictionary<(string Type, bool Resource), PackageMapping> catalog,
        ILookup<string, string> packages, string? framework, string? runtime)
    {
        var root = Fields(config);
        if (!root.Remove("Workflows", out var workflows) || Fields(workflows).Count == 0)
            throw new InvalidDataException("Expected at least one workflow.");
        var resources = new Dictionary<string, ResourceNode>(StringComparer.Ordinal);
        if (root.Remove("Resources", out var appResources)) ReadResources(resources, appResources, null, []);
        if (root.Count != 0) throw new InvalidDataException($"Unsupported application setting '{root.Keys.First()}'.");
        foreach (var workflow in workflows.EnumerateObject())
            if (Fields(workflow.Value).TryGetValue("Resources", out var local)) ReadResources(resources, local, workflow.Name, []);
        using var configStream = new MemoryStream(Encoding.UTF8.GetBytes(config.GetRawText()));
        using var sections = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(configStream).Build();
        var used = new HashSet<PackageMapping>();
        var sourcePorts = new HashSet<PortAddress>();
        var inputPorts = new HashSet<PortAddress>();
        var links = new List<CompiledLink>();
        var registrations = new List<string>();
        var workflowBuilders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var workflow in workflows.EnumerateObject())
        {
            RequireAddressSegment(workflow.Name, "workflow");
            var create = $"new WorkflowBuilder(services, {Literal(workflow.Name)})";
            if (Fields(workflow.Value).ContainsKey("Resources"))
            {
                var variable = "workflow" + workflowBuilders.Count.ToString(CultureInfo.InvariantCulture);
                registrations.Add($"var {variable} = {create};");
                workflowBuilders.Add(workflow.Name, variable);
            }
            else workflowBuilders.Add(workflow.Name, create);
        }
        foreach (var resource in resources.Values)
        {
            if (!catalog.TryGetValue((resource.Type, true), out var owner))
                throw new InvalidDataException($"No C# mapping for resource '{resource.Address}' ({resource.Type}).");
            if (owner.Mapping.WorkflowOnly && ApplicationAddress.Parse(resource.Address).WorkflowName is null)
                throw new InvalidDataException($"Resource '{resource.Address}' ({resource.Type}) must belong to a workflow.");
            var call = ComponentCall(resource.Address, resource.Settings, owner.Mapping, directory, [],
                SelectAlternative(resource.Address, resource.Settings, owner.Mapping));
            if (resource.Settings.Count != 0)
                throw new InvalidDataException($"Unsupported setting '{resource.Address}.{resource.Settings.Keys.First()}'.");
            used.Add(owner);
            registrations.Add("services" + call + ";");
        }
        foreach (var workflow in workflows.EnumerateObject())
        {
            var nodes = Fields(workflow.Value, StringComparer.Ordinal);
            if (nodes.Keys.All(k => k.Equals("Resources", StringComparison.OrdinalIgnoreCase)) && nodes.Count != 0) continue;
            registrations.Add(workflowBuilders[workflow.Name]);
            foreach (var (name, node) in nodes)
            {
                if (name.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;
                RequireAddressSegment(name, "component");
                var settings = Fields(node);
                if (!settings.Remove("Type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !catalog.TryGetValue((type.GetString()!, false), out var owner))
                    throw new InvalidDataException($"No C# mapping for '{workflow.Name}.{name}' ({(type.ValueKind == JsonValueKind.Undefined ? "missing Type" : type.ToString())}).");
                used.Add(owner);
                var address = ComponentAddress.Create(workflow.Name, name);
                var section = sections.GetSection($"Workflows:{workflow.Name}:{name}");
                var outputs = owner.Mapping.Outputs.ToList();
                var variant = SelectAlternative(name, settings, owner.Mapping);
                foreach (var arg in variant?.Arguments ?? owner.Mapping.Arguments)
                {
                    if (arg.ComponentType is { } componentType && settings.TryGetValue(arg.Name, out var component))
                    {
                        if (component.ValueKind != JsonValueKind.String || !nodes.TryGetValue(component.GetString()!, out var target) ||
                            !Fields(target).TryGetValue("Type", out var targetType) || targetType.GetString() != componentType)
                            throw new InvalidDataException($"'{address}.{arg.Name}' requires a {componentType} in this workflow.");
                    }
                    if (arg.ResourceType is not null && settings.TryGetValue(arg.Name, out var reference))
                    {
                        if (reference.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reference.GetString()))
                            throw new InvalidDataException($"Expected a resource reference for '{address}.{arg.Name}'.");
                        var paths = ApplicationAddress.ResolveResourcePaths(reference.GetString()!.Trim(), workflow.Name);
                        if ((paths[0].WorkflowName is { } scope && scope != workflow.Name) ||
                            paths.Select(path => resources.GetValueOrDefault(path.Value)).FirstOrDefault(resource => resource is not null)?.Type != arg.ResourceType)
                            throw new InvalidDataException($"'{address}.{arg.Name}' requires a {arg.ResourceType} resource in scope: {reference}.");
                    }
                }
                registrations.Add("    " + ComponentCall(name, settings, owner.Mapping, directory, outputs, variant));
                foreach (var output in outputs)
                {
                    var port = address.Port(output);
                    sourcePorts.Add(port);
                    AddLinks(output, port, input: false);
                }
                foreach (var input in owner.Mapping.Inputs)
                {
                    var port = address.Port(input);
                    inputPorts.Add(port);
                    AddLinks(input, port, input: true);
                }
                if (outputs.Contains("Output")) AddLinks("Error", address.Port("Output"), input: false, errorsOnly: true);
                if (settings.Count != 0)
                    throw new InvalidDataException($"Unsupported setting '{name}.{settings.Keys.First()}'.");

                void AddLinks(string property, PortAddress port, bool input, bool errorsOnly = false)
                {
                    if (!settings.Remove(property)) return;
                    foreach (var link in OneOrManyPortReferences.Read(section.GetSection(property), address))
                        links.Add(new(input ? link.Address : port, input ? port : link.Address, link.Condition, link.Engine, errorsOnly));
                }
            }
            registrations[^1] += ";";
        }
        var uniqueLinks = links.Distinct().ToArray();
        foreach (var link in uniqueLinks)
        {
            if (!sourcePorts.Contains(link.From))
                throw new InvalidDataException($"Unknown output '{link.From}' linked to '{link.To}'.");
            if (!inputPorts.Contains(link.To))
                throw new InvalidDataException($"Unknown input '{link.To}' linked from '{link.From}'.");
            if (link.Condition is not null && link.Engine != "jsonata")
                throw new InvalidDataException($"Link '{link.From}' to '{link.To}' requires Engine 'jsonata' for its condition.");
        }
        var typedLinks = uniqueLinks.Any(link => link.ErrorsOnly || link.Condition is not null) || workflows.EnumerateObject().Count() > 1;

        var lines = new List<string> {
            "#:property PublishAot=false", "#:property PublishTrimmed=false", "#:property PublishSingleFile=true",
            "#:property SelfContained=false", "#:property DebugType=embedded"
        };
        var versions = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Microsoft.Extensions.Logging.Console"] = "10.0.13" };
        versions[runtime == "linux-x64" ? "Microsoft.Extensions.Hosting" : "Microsoft.Extensions.Hosting.WindowsServices"] = "10.0.13";
        foreach (var owner in used)
        {
            if (versions.TryGetValue(owner.PackageId, out var version) &&
                !string.Equals(version, owner.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Conflicting versions of '{owner.PackageId}': {version} and {owner.Version}.");
            versions[owner.PackageId] = owner.Version;
        }
        if (typedLinks && uniqueLinks.Length > 0) RequirePackage("FluxFlow.Configuration");
        var hasConditions = uniqueLinks.Any(link => link.Condition is not null);
        if (hasConditions) RequirePackage("FluxFlow.Expressions.Jsonata");
        var platforms = used.Select(v => v.Mapping.Platform).OfType<string>().Distinct().ToArray();
        if (runtime == "linux-x64" && platforms.Length != 0)
        {
            var mapping = used.First(v => v.Mapping.Platform is not null).Mapping;
            throw new InvalidDataException($"'{mapping.Type}' requires {mapping.Platform} and cannot publish for {runtime}.");
        }
        if (platforms.Length > 1) throw new InvalidDataException("The selected families require different target platforms.");
        if (framework is not null || platforms.Length != 0)
            lines.Add("#:property TargetFramework=" + (framework ?? "net10.0") + (platforms.Length == 0 ? "" : "-" + platforms[0]));
        lines.AddRange(versions.Select(p => $"#:package {p.Key}@{p.Value}"));
        lines.Add("using FluxFlow.Application;");
        lines.Add("using Microsoft.Extensions.DependencyInjection;");
        lines.Add("using Microsoft.Extensions.Logging;");
        lines.Add("using Microsoft.Extensions.Hosting;");
        lines.AddRange(used.Select(v => v.Mapping.Namespace).Distinct().Order(StringComparer.Ordinal).Select(n => $"using {n};"));
        if (typedLinks && uniqueLinks.Length > 0)
            lines.AddRange(["using FluxFlow.Application.Addressing;", "using FluxFlow.Configuration.Application.Links;"]);
        if (hasConditions) lines.Add("using FluxFlow.Expressions.Jsonata;");
        var hostMethods = used.Select(v => v.Mapping.HostMethod).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray();
        lines.AddRange(["", "var drainGrace = new HostOptions().ShutdownTimeout;", "var builder = new HostBuilder();",
            "// Allow a second shutdown interval for cleanup after the work deadline.",
            "builder.ConfigureHostOptions(options => options.ShutdownTimeout = drainGrace + drainGrace);"]);
        if (runtime != "linux-x64") lines.Add("builder.UseWindowsService();");
        foreach (var method in hostMethods) lines.Add($"builder.{method}();");
        lines.AddRange(["builder.ConfigureServices(services =>", "{"]);
        var registrationStart = lines.Count;
        lines.Add("services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));");
        foreach (var registration in used.Select(v => v.Mapping.ConfigureServices).OfType<string>().Distinct().Order(StringComparer.Ordinal))
            lines.Add($"{registration}(services);");
        if (hasConditions) lines.Add("services.AddJsonataExpressionEngine();");
        lines.AddRange(registrations);
        if (uniqueLinks.Length > 0 && !typedLinks)
        {
            lines[^1] = lines[^1].TrimEnd(';');
            foreach (var link in uniqueLinks)
                lines.Add($"    .Link({Literal(Local(link.From))}, {Literal(Local(link.To))})");
            lines[^1] += ";";
        }
        else
        {
            foreach (var link in uniqueLinks)
            {
                var method = link.ErrorsOnly ? "AddErrorLink" : "AddOutputLink";
                lines.Add($"services.{method}(PortAddress.Parse({Literal(link.From.Value)}), " +
                    $"new PortReference(PortAddress.Parse({Literal(link.To.Value)}), {JsonSerializer.Serialize(link.Condition)}, {JsonSerializer.Serialize(link.Engine)}));");
            }
        }
        lines.Add("services.AddFluxFlow();");
        for (var i = registrationStart; i < lines.Count; i++) lines[i] = "    " + lines[i];
        lines.AddRange(["});", ""]);
        lines.AddRange(["var host = builder.Build();", "Action<int>? setServiceExit = null;"]);
        if (runtime != "linux-x64")
            lines.Add("""
                if (OperatingSystem.IsWindows() &&
                    host.Services.GetService<IHostLifetime>() is Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceLifetime windowsService)
                    setServiceExit = code => windowsService.ExitCode = code;
                """);
        lines.Add(CompiledApplicationLifetime.Source);
        lines.Add("");
        return string.Join('\n', lines);

        static string Local(PortAddress port) => $"{port.ComponentAddress!.ComponentName}.{port.PortName}";

        void RequirePackage(string id)
        {
            var available = packages[id].ToArray();
            if (available.Length != 1) throw new InvalidDataException($"Select exactly one '{id}' package for compiled links.");
            versions[id] = available[0];
        }
    }

    private static Variant? SelectAlternative(string name, Dictionary<string, JsonElement> settings, Mapping mapping)
    {
        var alternatives = mapping.Alternatives?.Where(v => settings.ContainsKey(v.WhenPresent)).ToArray();
        if (alternatives is { Length: > 1 })
            throw new InvalidDataException($"Conflicting settings for '{name}': {string.Join(", ", alternatives.Select(v => v.WhenPresent))}.");
        return alternatives?.FirstOrDefault();
    }

    private static string ComponentCall(string name, Dictionary<string, JsonElement> settings, Mapping mapping,
        string directory, List<string> outputs, Variant? variant = null)
    {
        if (mapping.RequiredSettings is { } required)
            foreach (var (key, expected) in required)
                if (!settings.Remove(key, out var actual) || actual.ValueKind != JsonValueKind.String || actual.GetString() != expected)
                    throw new InvalidDataException($"'{name}.{key}' must be '{expected}' for this C# mapping.");
        var arguments = new List<string>();
        var constructor = new List<string>();
        foreach (var arg in variant?.Arguments ?? mapping.Arguments)
            if (arg.Requires is not null && settings.ContainsKey(arg.Name) && !settings.ContainsKey(arg.Requires))
                throw new InvalidDataException($"'{name}.{arg.Name}' requires '{arg.Requires}'.");
        foreach (var arg in variant?.Arguments ?? mapping.Arguments)
        {
            if (settings.Remove(arg.Name, out var value))
            {
                if (arg.Kind == "namedExpressions")
                {
                    var entries = Fields(value);
                    if (entries.Count == 0) throw new InvalidDataException($"'{name}.{arg.Name}' requires at least one named expression.");
                    var reserved = mapping.Inputs.Concat(outputs).Concat(mapping.Arguments.Select(a => a.Name))
                        .Concat(mapping.RequiredSettings?.Keys ?? Enumerable.Empty<string>()).Append("Error").Append("Type");
                    foreach (var port in entries.Keys)
                    {
                        RequireAddressSegment(port, "output");
                        if (reserved.Contains(port, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException($"'{name}.{port}' conflicts with a component setting or port.");
                    }
                    outputs.AddRange(entries.Keys);
                }
                var literal = SettingValue(arg, value, directory, $"{name}.{arg.Name}", mapping.RequiredSettings?.GetValueOrDefault("Engine"));
                if (arg.ConstructorParameter is { } parameter) constructor.Add($"{parameter}: {literal}");
                else arguments.Add(mapping.SettingsType is null ? literal : $"{arg.Name} = {literal}");
            }
            else if (arg.Optional)
            {
                if (mapping.SettingsType is null) arguments.Add("null");
            }
            else throw new InvalidDataException($"Missing '{name}.{arg.Name}'.");
        }
        if (mapping.InactiveDefaults is { } defaults)
            foreach (var (key, expected) in defaults)
                if (settings.TryGetValue(key, out var actual) && bool.TryParse(actual.ToString(), out var flag) && flag == expected)
                    settings.Remove(key);
        if (mapping.SettingsType is { } type)
            arguments = [NewObject(type, constructor, arguments)];
        arguments.Insert(0, Literal(name));
        return $".{variant?.Method ?? mapping.Method}({string.Join(", ", arguments)})";
    }

    private static string NewObject(string type, List<string> constructor, List<string> assignments)
    {
        var parameters = constructor.Count == 0 ? "" : $"({string.Join(", ", constructor)})";
        return $"new {type}{parameters} {{ {string.Join(", ", assignments)} }}";
    }

    private static void ReadResources(Dictionary<string, ResourceNode> resources, JsonElement group, string? workflow, string[] path)
    {
        var fields = Fields(group);
        if (path.Length > 0 && fields.Remove("Type", out var type))
        {
            var address = workflow is null ? ApplicationAddress.Resource(path) : ApplicationAddress.WorkflowResource(workflow, path);
            resources.Add(address.Value, new(address.Value, type.GetString()!, fields));
            return;
        }
        foreach (var (key, value) in fields)
        {
            if (path.Length == 0 && key.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Functions cannot be registered as a workflow resource.");
            var segment = path.Length == 0 && key.Equals("Connections", StringComparison.OrdinalIgnoreCase) ? "Connections" : key;
            ReadResources(resources, value, workflow, [.. path, segment]);
        }
    }

    private static string SettingValue(Argument arg, JsonElement value, string directory, string field, string? engine)
    {
        if (arg.Kind == "flags")
        {
            var flags = arg.Flags!;
            var text = value.ToString();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            {
                if (bits <= 0 || (bits & ~flags.Values.Aggregate(0, (a, b) => a | b)) != 0)
                    throw new InvalidDataException($"Unsupported flags for '{field}'.");
                return $"({arg.EnumType}){bits}";
            }
            var names = text.Split(',', StringSplitOptions.TrimEntries).Select(part =>
                flags.Keys.FirstOrDefault(k => k.Equals(part, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Unknown flag for '{field}'."));
            return string.Join(" | ", names.Select(n => $"{arg.EnumType}.{n}"));
        }
        if (arg.Values is not null) return ChoiceValue(arg, value, field);
        if (arg.Kind is "oneOrManyObjects" or "objectArray")
        {
            if (arg.Kind == "objectArray" && value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"'{field}' must be an array.");
            var item = arg with { Kind = "object" };
            var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];
            return "[" + string.Join(", ", items.Select((element, index) =>
            {
                if (element.ValueKind == JsonValueKind.String && arg.ScalarField is { } key)
                    element = JsonSerializer.SerializeToElement(new Dictionary<string, string?> { [key] = element.GetString() });
                return SettingValue(item, element, directory, $"{field}[{index}]", engine);
            })) + "]";
        }
        if (arg.Kind == "objectFactory")
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"'{field}' must be an object.");
            return $"{arg.Factory}({Literal(value.GetRawText())})";
        }
        if (arg.Kind == "object")
        {
            var fields = Fields(value);
            var assignments = new List<string>();
            var constructor = new List<string>();
            foreach (var child in arg.Fields!)
            {
                if (fields.Remove(child.Name, out var item))
                {
                    var literal = SettingValue(child, item, directory, field + "." + child.Name, engine);
                    if (child.ConstructorParameter is { } parameter) constructor.Add($"{parameter}: {literal}");
                    else assignments.Add($"{child.Name} = {literal}");
                }
                else if (!child.Optional) throw new InvalidDataException($"Missing '{field}.{child.Name}'.");
            }
            if (fields.Count > 0) throw new InvalidDataException($"Unsupported setting '{field}.{fields.Keys.First()}'.");
            return NewObject(arg.SettingsType!, constructor, assignments);
        }
        if (arg.Kind == "stringArray")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                throw new InvalidDataException($"'{field}' must be a non-empty array of strings.");
            return "[" + string.Join(", ", value.EnumerateArray().Select(v => ArgumentValue("nonBlankString", v, directory, field, null))) + "]";
        }
        if (arg.Kind == "environmentPaths")
        {
            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"'{field}' must be an array of file paths.");
            return "[" + string.Join(", ", value.EnumerateArray().Select((path, index) =>
                EnvironmentPath($"{field}.{index}", path))) + "]";
        }
        if (arg.Kind == "environmentPath") return EnvironmentPath(field, value);
        if (arg.Kind == "headerDictionary")
        {
            var entries = Fields(value).Select(p => $"[{Literal(p.Key)}] = {EnvironmentValue(field + "." + p.Key, p.Value)}");
            return "new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { " + string.Join(", ", entries) + " }";
        }
        if (arg.Kind == "environment") return EnvironmentValue(field, value);
        return ArgumentValue(arg.Kind, value, directory, field, arg.Engine ?? engine);
    }

    private static string EnvironmentPath(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"'{field}' requires a file path.");
        return EnvironmentValue(field, value);
    }

    private static string EnvironmentValue(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Expected text for '{field}'.");
        var path = field.StartsWith("Resources.", StringComparison.Ordinal) ? field : "Workflows." + field;
        var variable = path.Replace(".", "__", StringComparison.Ordinal);
        var fallback = value.GetString()!.Length == 0 ? "string.Empty" :
            $"throw new InvalidOperationException({Literal("Set environment variable " + variable + ".")})";
        return $"(Environment.GetEnvironmentVariable({Literal(variable)}) ?? {fallback})";
    }

    private static string ChoiceValue(Argument arg, JsonElement value, string field)
    {
        var options = arg.Values!;
        var text = value.ToString();
        var selected = options.FirstOrDefault(v => string.Equals(v, text,
            arg.EnumType is null ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
        if (arg.EnumType is not null && selected is null && int.TryParse(text, out var index) && (uint)index < options.Length)
            selected = options[index];
        if (selected is null) throw new InvalidDataException($"'{field}' must be one of {string.Join(", ", options)}.");
        return arg.EnumType is null ? Literal(selected) : $"{arg.EnumType}.{selected}";
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement value, StringComparer? comparer = null)
        => value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, comparer ?? StringComparer.OrdinalIgnoreCase);

    private static void RequireAddressSegment(string name, string role)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Contains('.'))
            throw new InvalidDataException($"Invalid {role} name '{name}': expected one address segment.");
    }

    private static string ArgumentValue(string kind, JsonElement value, string directory, string field, string? engine)
    {
        if (kind == "namedExpressions")
        {
            var entries = Fields(value).OrderBy(p => p.Key, ConfigurationKeyComparer.Instance);
            return "new Dictionary<string, string> { " + string.Join(", ", entries.Select(p =>
                $"[{Literal(p.Key)}] = {ArgumentValue("expression", p.Value, directory, field + "." + p.Key, engine)}")) + " }";
        }
        if (kind == "boolean")
        {
            if (bool.TryParse(value.ToString(), out var flag)) return flag ? "true" : "false";
            throw new InvalidDataException($"'{field}' must be true or false.");
        }
        if (kind == "positiveInt64")
        {
            if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
                return number.ToString(CultureInfo.InvariantCulture) + "L";
            throw new InvalidDataException($"'{field}' must be a positive 64-bit integer.");
        }
        if (kind == "uint32")
        {
            if (uint.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return number.ToString(CultureInfo.InvariantCulture) + "u";
            throw new InvalidDataException($"'{field}' must be an unsigned 32-bit integer.");
        }
        if (kind is "positiveInt32" or "nonNegativeInt32" or "positiveUInt16")
        {
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.String &&
                int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= (kind == "nonNegativeInt32" ? 0 : 1) &&
                (kind != "positiveUInt16" || result <= ushort.MaxValue))
                return result.ToString(CultureInfo.InvariantCulture);
            throw new InvalidDataException($"'{field}' must be an integer between {(kind == "nonNegativeInt32" ? 0 : 1)} and {(kind == "positiveUInt16" ? ushort.MaxValue : int.MaxValue)}.");
        }
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Expected text for '{field}'.");
        var text = value.GetString()!;
        switch (kind)
        {
            case "expression":
                var expression = text.Trim();
                if (ExpressionSource.IsFileReference(expression))
                {
                    var path = expression.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? expression[5..] : expression;
                    expression = "file:" + ResolveFile(path, directory, field);
                }
                return Literal(new ExpressionSource(expression).Read(directory, engine));
            case "endpointUri":
                if (!Uri.TryCreate(text, UriKind.Absolute, out var endpoint) || endpoint.Fragment.Length != 0)
                    throw new InvalidDataException($"'{field}' requires an absolute endpoint.");
                return endpoint.Query.Length == 0 && endpoint.UserInfo.Length == 0 ? Literal(text) : EnvironmentValue(field, value);
            case "httpUri":
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                    uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                    throw new InvalidDataException($"'{field}' requires an absolute HTTP(S) endpoint without embedded credentials or a fragment.");
                return uri.Query.Length == 0 ? Literal(text) : EnvironmentValue(field, value);
            case "guid" when Guid.TryParse(text, out var guid): return $"new Guid({Literal(guid.ToString("D"))})";
            case "text": return Literal(text);
            case "nonBlankString" when !string.IsNullOrWhiteSpace(text): return Literal(text);
            case "nonNegativeDuration" or "timeoutOrInfinite" or "timeout" or "timerInterval":
                if (!TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var time) ||
                    !(kind switch
                    {
                        "nonNegativeDuration" => time >= TimeSpan.Zero,
                        "timerInterval" => time.TotalMilliseconds is >= 1 and <= uint.MaxValue - 1,
                        _ => time.TotalMilliseconds is > 0 and <= uint.MaxValue - 1 || kind == "timeoutOrInfinite" && time == Timeout.InfiniteTimeSpan
                    })) throw new InvalidDataException($"Invalid duration for '{field}'.");
                return $"TimeSpan.FromTicks({time.Ticks.ToString(CultureInfo.InvariantCulture)}L)";
            case "duration" when TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var duration) && duration > TimeSpan.Zero:
                return $"TimeSpan.FromTicks({duration.Ticks.ToString(CultureInfo.InvariantCulture)}L)";
            case "instant" when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant):
                return $"new DateTimeOffset({instant.Ticks.ToString(CultureInfo.InvariantCulture)}L, TimeSpan.FromTicks({instant.Offset.Ticks.ToString(CultureInfo.InvariantCulture)}L))";
            case "jsonArray":
            case "jsonValue":
                var input = text.TrimStart();
                byte[] data;
                if (!JsonSource.IsFileReference(input))
                    data = Encoding.UTF8.GetBytes(input.StartsWith("inline:", StringComparison.OrdinalIgnoreCase) ? input[7..] : text);
                else
                {
                    var path = input.TrimEnd();
                    if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) path = path[5..];
                    data = File.ReadAllBytes(ResolveFile(path, directory, field));
                }
                using (var stream = new MemoryStream(data))
                using (var parsed = JsonDocument.Parse(stream))
                    if (kind == "jsonArray" && parsed.RootElement.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException($"Expected a JSON array for '{field}'.");
                return $"Convert.FromBase64String({Literal(Convert.ToBase64String(data))})";
            default: throw new InvalidDataException($"Invalid {kind} for '{field}'.");
        }
    }

    private static string ResolveFile(string path, string directory, string field)
    {
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.Contains(':')) throw new InvalidDataException($"Expected a relative file for '{field}'.");
        var full = Path.GetFullPath(path, directory);
        var relative = Path.GetRelativePath(directory, full);
        if (relative.Split(Path.DirectorySeparatorChar)[0] == "..") throw new InvalidDataException($"File for '{field}' is outside the export directory.");
        var current = directory;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Linked files are unsupported for '{field}'.");
        }
        return full;
    }

    private sealed record CompiledLink(PortAddress From, PortAddress To, string? Condition, string? Engine, bool ErrorsOnly);
    private sealed record ResourceNode(string Address, string Type, Dictionary<string, JsonElement> Settings);
    private sealed record Mapping(string Type, string Namespace, string Method, Argument[] Arguments, string[] Inputs,
        string[] Outputs, Dictionary<string, string>? RequiredSettings = null, Variant[]? Alternatives = null,
        string? SettingsType = null, bool Resource = false, string? HostMethod = null, bool WorkflowOnly = false, string? Platform = null, string? ConfigureServices = null, Dictionary<string, bool>? InactiveDefaults = null);
    private sealed record Variant(string WhenPresent, string Method, Argument[] Arguments);
    private sealed record Argument(string Name, string Kind, bool Optional, string? Engine = null, string? EnumType = null,
        string[]? Values = null, string? Requires = null, string? ResourceType = null, string? SettingsType = null, Argument[]? Fields = null, string? ScalarField = null, string? ConstructorParameter = null, string? ComponentType = null, Dictionary<string, int>? Flags = null, string? Factory = null);
    private sealed record PackageMapping(Mapping Mapping, string PackageId, string Version);
}
