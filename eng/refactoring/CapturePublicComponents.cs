#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2)
    throw new ArgumentException("Expected repository root and baseline directory.");

var root = Path.GetFullPath(args[0]);
var baseline = Path.GetFullPath(Path.Combine(root, args[1]));
if (!baseline.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("The baseline directory must be inside the repository.");
var output = Path.Combine(baseline, "public-components.json");
if (File.Exists(output))
    throw new IOException("Public component inventory already exists; use a fresh baseline directory.");

var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseline, "migration-inventory.json")));
var releaseDirectory = Path.Combine(root,
    "tests/Unit/Infrastructure/Release/FluxFlow.Release.Tests/bin/Release/net10.0");
var assemblyPaths = inventory.RootElement.EnumerateArray().ToDictionary(
    package => package.GetProperty("PackageId").GetString()!,
    package => Path.Combine(root,
        Path.GetDirectoryName(package.GetProperty("CurrentProject").GetString())!,
        "bin/Release/net10.0", package.GetProperty("PackageId").GetString()! + ".dll"));
var resolvers = assemblyPaths.Values.Select(path => new AssemblyDependencyResolver(path)).ToArray();
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    if (assemblyPaths.TryGetValue(name.Name!, out var projectPath))
        return context.LoadFromAssemblyPath(projectPath);
    var path = resolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null);
    var releasePath = Path.Combine(releaseDirectory, name.Name + ".dll");
    if (path is null && File.Exists(releasePath))
        path = releasePath;
    return path is null ? null : context.LoadFromAssemblyPath(path);
};

var entries = new List<object>();
var assemblyCount = 0;
foreach (var package in inventory.RootElement.EnumerateArray())
{
    var id = package.GetProperty("PackageId").GetString()!;
    var assemblyPath = assemblyPaths[id];
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException("Build the complete Release solution before capture.", assemblyPath);
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    assemblyCount++;
    foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
    {
        if (!type.IsClass || type.IsAbstract ||
            !type.GetInterfaces().Any(contract => contract.FullName == "FluxFlow.Nodes.IFlowNode"))
            continue;

        var ports = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType.GetInterfaces()
                .Append(property.PropertyType)
                .Any(contract => contract.FullName == "System.Threading.Tasks.Dataflow.IDataflowBlock"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new { property.Name, Type = property.PropertyType.ToString() })
            .ToArray();
        entries.Add(new
        {
            PackageId = id,
            Type = type.FullName,
            Generic = type.ContainsGenericParameters,
            BaseType = type.BaseType?.ToString(),
            IsSource = type.GetInterfaces().Any(contract => contract.FullName == "FluxFlow.Nodes.IFlowSource"),
            Constructors = type.GetConstructors().Select(constructor => constructor.ToString()).Order().ToArray(),
            Ports = ports,
            Targets = package.GetProperty("Targets").EnumerateArray().Select(target => target.GetString()).ToArray(),
            Status = "pending",
            Verification = "Existing tests are a baseline, not proof of migration. Bindings, signals and dynamically exposed ports require family-specific checks."
        });
    }
}

var report = new
{
    CapturedUtc = DateTimeOffset.UtcNow,
    Assemblies = assemblyCount,
    PublicConcreteComponents = entries.Count,
    Method = "Reflection over the fresh Release source assemblies with dependency resolution from their build outputs. No constructors, registration methods or property getters invoked.",
    Components = entries
};
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Captured {entries.Count} public concrete components across {assemblyCount} package assemblies.");
