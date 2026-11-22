using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluxFlow.Plugins;

namespace FluxFlow.Execution.Plugins;

/// <summary>Trusted local packages, selected once at host startup. A running host holds a shared store lock.</summary>
public sealed class InstalledPlugins : IDisposable
{
    // Contract 1 packages provide net8.0 and net10.0 assets. Newer hosts keep
    // using the compatible net10.0 payload, including its design dependencies.
    private const string BrowserFramework = "net10.0";
    private static string RuntimeFramework => Environment.Version.Major >= 10 ? "net10.0" : "net8.0";
    internal LocalPluginManager? Manager { get; private init; }
    private readonly FileStream? _lease;
    private readonly List<PackageContext> _contexts = [];
    public IReadOnlyList<IFluxFlowPlugin> Modules { get; private set; }
    public IReadOnlyDictionary<string, (Delegate Implementation, string? Signature)> Functions { get; private set; }
    private Dictionary<string, string> _packages { get; init; } = new(StringComparer.Ordinal);
    public string PackageDirectory(string id) => _packages.TryGetValue(id, out var path) ? path
        : throw new InvalidOperationException($"Install the package for '{id}' before publishing.");
    public string? StudioAssembly(string id, bool browser = false, string? dependency = null)
    {
        if (!_packages.TryGetValue(id, out var package)) return null;
        var tfm = browser ? BrowserFramework : RuntimeFramework;
        var module = Modules.First(m => m.Description.Id == id);
        if (module.Description.StudioEntry is null && module.Description.Design is null) return null;
        if (dependency is null) return module.Description.StudioEntry is null ? null : Path.Combine(package, "studio", tfm, id + ".Studio.dll");
        return module.Description.ClientAssemblies.Contains(dependency, StringComparer.Ordinal)
            ? Path.Combine(package, "lib", tfm, dependency + ".dll") : null;
    }
    public PluginDescription[] Descriptions => Modules.Select(module => module.Description).ToArray();
    public InstalledPlugins(params IFluxFlowPlugin[] modules)
    {
        Modules = Array.AsReadOnly(modules.ToArray());
        Functions = ReadFunctions();
    }
    private IReadOnlyDictionary<string, (Delegate Implementation, string? Signature)> ReadFunctions() =>
        Modules.SelectMany(module => module.Functions)
            .ToDictionary(function => function.Name, function => (function.Implementation, function.Signature), StringComparer.Ordinal);

    private static bool Incompatible(Exception error) =>
        error.GetBaseException() is TypeLoadException or MissingMemberException or FileLoadException or FileNotFoundException;

    internal void ConfigureHost(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        foreach (var module in Modules)
        {
            var before = services.ToArray();
            try { module.ConfigureHost(services); }
            catch (Exception error) when (Manager is not null && Incompatible(error))
            {
                services.Clear();
                foreach (var descriptor in before) services.Add(descriptor);
                Manager.LoadErrors[module.Description.Id] = error.GetBaseException().Message;
                Modules = Array.AsReadOnly(Modules.Where(m => m != module).ToArray());
                Functions = ReadFunctions();
                _packages.Remove(module.Description.Id);
            }
        }
    }
    private InstalledPlugins(IFluxFlowPlugin[] modules, FileStream lease, List<PackageContext> contexts) : this(modules) => (_lease, _contexts) = (lease, contexts);
    public void Dispose()
    {
        foreach (var context in _contexts) context.Unload();
        _contexts.Clear();
        _lease?.Dispose();
    }

    public static InstalledPlugins Load(string? directory = null)
    {
        var root = Path.GetFullPath(directory ?? Environment.GetEnvironmentVariable("FLUXFLOW_PLUGIN_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFlow", "Plugins"));
        Directory.CreateDirectory(root);
        var manager = new LocalPluginManager(root);
        manager.InstallBundledDefaults();
        manager.ApplyPending();
        var lockFile = Path.Combine(root, ".store.lock");
        var lease = OpenReadLease(lockFile);
        List<PackageContext> contexts = [];
        try
        {
            List<IFluxFlowPlugin> modules = [];
            Dictionary<string, string> packages = new(StringComparer.Ordinal);
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase), components = new(StringComparer.OrdinalIgnoreCase), connections = new(StringComparer.OrdinalIgnoreCase), functions = new(StringComparer.Ordinal);
            foreach (var (package, path) in Payloads(root))
            {
                var manifest = Path.Combine(path, "fluxflow-plugin.json");
                using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
                var entry = doc.RootElement;
                var id = entry.GetProperty("id").GetString()!;
                if (entry.GetProperty("contract").GetInt32() != 1 || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") ||
                    !string.Equals(package, id, StringComparison.OrdinalIgnoreCase) || !ids.Add(id))
                    throw new InvalidDataException($"Incompatible or duplicate plugin manifest in {path}.");
                var tfm = RuntimeFramework;
                var binaries = Path.Combine(path, "lib", tfm);
                var context = new PackageContext(binaries);
                contexts.Add(context);
                IFluxFlowPlugin module;
                try
                {
                    var assembly = context.ReadAssembly(Path.Combine(binaries, id + ".dll"));
                    var type = assembly.GetType(entry.GetProperty("entryType").GetString()!, throwOnError: true)!;
                    if (!typeof(IFluxFlowPlugin).IsAssignableFrom(type)) throw new InvalidDataException($"Invalid plugin entry in {id}.");
                    module = (IFluxFlowPlugin)Activator.CreateInstance(type)!;
                }
                catch (Exception error) when (Incompatible(error))
                {
                    manager.LoadErrors[id] = error.GetBaseException().Message;
                    continue;
                }
                if (module.Description.Id != id || module.Description.Components.Any(c => !components.Add(c.Palette.ComponentType)) ||
                    module.Description.Connections.Any(c => !connections.Add(c.Type)) ||
                    module.Functions.Any(f => !functions.Add(f.Name)))
                    throw new InvalidDataException($"Plugin identity or contribution collision in {id}.");
                if (module.Description.StudioEntry is not null || module.Description.Design is not null)
                {
                    var editor = Path.Combine(path, "studio");
                    if (module.Description.StudioEntry is not null && (!File.Exists(Path.Combine(editor, tfm, id + ".Studio.dll")) || !File.Exists(Path.Combine(editor, BrowserFramework, id + ".Studio.dll")))) throw new InvalidDataException($"Missing Studio assembly in {id}.");
                    foreach (var name in module.Description.ClientAssemblies)
                    {
                        if (!ValidClientAssembly(module.Description, name) ||
                            !File.Exists(Path.Combine(binaries, name + ".dll")) ||
                            !File.Exists(Path.Combine(path, "lib", BrowserFramework, name + ".dll")))
                            throw new InvalidDataException($"Invalid or missing Studio dependency '{name}' in {id}.");
                    }
                }
                packages.Add(id, path);
                modules.Add(module);
            }
            return new InstalledPlugins(modules.ToArray(), lease, contexts) { Manager = manager, _packages = packages };
        }
        catch
        {
            foreach (var context in contexts) context.Unload();
            lease.Dispose();
            throw;
        }
    }

    private static bool ValidClientAssembly(PluginDescription plugin, string name)
    {
        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\z")) return false;
        if (name.StartsWith(plugin.Id + ".", StringComparison.Ordinal)) return true;
        // Third-party managed libraries may supply portable validation. Entry points and
        // FluxFlow/framework assemblies are never supplied under another owner's name.
        return plugin.Design is { } design && name != design.Assembly && design.Dependencies.Contains(name, StringComparer.Ordinal)
            && name.Split('.')[0] is not ("FluxFlow" or "System" or "Microsoft" or "netstandard" or "mscorlib");
    }

    // A companion may have just promoted the last pending entry and still hold the write lock.
    private static FileStream OpenReadLease(string path)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read); }
            catch (IOException e) when ((e.HResult & 0xffff) is (11 or 32 or 33) && wait.Elapsed < TimeSpan.FromSeconds(10))
            { Thread.Sleep(50); }
        }
    }

    private static IEnumerable<(string Id, string Path)> Payloads(string root)
    {
        var active = Path.Combine(root, "active");
        if (Directory.Exists(active))
            foreach (var folder in Directory.EnumerateDirectories(active).Order(StringComparer.Ordinal))
                yield return (Path.GetFileName(folder), folder);

        // Read original NuGet installations in place. Opening a project never migrates its store.
        foreach (var receipt in Directory.EnumerateFiles(root, "*.installed").Order(StringComparer.Ordinal))
        {
            var package = Path.GetFileNameWithoutExtension(receipt);
            var version = File.ReadAllText(receipt).Trim();
            if (!Regex.IsMatch(package, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") ||
                !Regex.IsMatch(version, @"\A[0-9]+(\.[0-9]+){2}(-[A-Za-z0-9.-]+)?\z"))
                throw new InvalidDataException($"Invalid installation receipt: {receipt}.");
            var path = Path.Combine(root, "packages", package.ToLowerInvariant(), version.ToLowerInvariant());
            yield return (package, path);
        }
    }

    // Share the host's contract and runtime types. Private managed helpers belong beside the entry DLL.
    private sealed class PackageContext(string binaries) : AssemblyLoadContext(isCollectible: true)
    {
        // Do not leave the installed DLL mapped on Windows after the host releases its lease.
        internal Assembly ReadAssembly(string path)
        {
            using var file = File.OpenRead(path);
            return LoadFromStream(file);
        }

        protected override nint LoadUnmanagedDll(string name)
        {
            if (Path.GetFileName(name) != name) return 0;
            var folder = Path.Combine(binaries, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
            foreach (var candidate in new[] { name, name + ".so", name + ".dylib", "lib" + name + ".so", "lib" + name + ".dylib" })
                if (File.Exists(Path.Combine(folder, candidate))) return LoadUnmanagedDllFromPath(Path.Combine(folder, candidate));
            return 0;
        }

        protected override Assembly? Load(AssemblyName name)
        {
            try { return Default.LoadFromAssemblyName(name); }
            catch (FileNotFoundException)
            {
                var candidate = Path.Combine(binaries, name.Name + ".dll");
                if (!File.Exists(candidate))
                    candidate = Path.Combine(binaries, OperatingSystem.IsWindows() ? "win" : "unix", name.Name + ".dll");
                return File.Exists(candidate) ? ReadAssembly(candidate) : null;
            }
        }
    }
}
