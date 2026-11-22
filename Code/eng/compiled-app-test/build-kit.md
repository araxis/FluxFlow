# FluxFlow standalone build kit

Extract the ZIP to an empty folder on Windows x64. Open PowerShell in that folder with the .NET 11 SDK (11.0.100 or a later stable 11.0 feature band). The SDK builds both targets. The manifest pins the tool version, and `revision.txt` identifies the source used to build the kit.

```powershell
dotnet tool restore --configfile ./feed/NuGet.Config
dotnet fluxflow publish ./orders/application.json --framework net10.0 --feed ./feed --output ./out/net10/Orders.exe
dotnet fluxflow publish ./orders/application.json --framework net11.0 --feed ./feed --output ./out/net11/Orders.exe
```

The local feed supplies the FluxFlow CLI, family mappings and runtime packages. The first build needs access to nuget.org for external dependencies and any required SDK packs. For your own workflow, replace `./orders/application.json` with its configuration export and keep its relative input files alongside the export.

Copy either EXE to a Windows x64 PC with the matching .NET runtime. Run it, wait for both order records, and press Enter to stop. Launch it again to repeat. Each launch should log B-23 at 2950 cents, followed by C-42 at 1199 cents, at Information level in category `Unsaved.Orders`. The EXE contains the order data and expression.
