# Studio desktop host

The first Windows desktop host renders `FluxFlow.Studio.UI` in WPF BlazorWebView.
It runs Razor components in the native .NET process, without WebAssembly or a
localhost HTTP server. Open `Code/FluxFlow.sln` and select this startup project.

## Run

```powershell
dotnet run --project Code/src/Studio/FluxFlow.Studio.Desktop -f net11.0-windows10.0.17763.0
```

Use `net8.0-windows10.0.17763.0` or `net10.0-windows10.0.17763.0` for an older host. Windows 10 or newer and
the Microsoft Edge WebView2 Runtime are required. This is a development host;
an installer and automatic prerequisite checks are not included yet.

## Native boundaries

- Open, Import and attachments use the Windows Open File dialog.
- Save writes to the opened file; Save As and first Save use the Windows Save File
  dialog. Export creates a separate engine JSON file through that same boundary.
- Cancel does not replace the document. Writes use a temporary file alongside the
  destination before replacement, preserving the previous file if writing fails.
- Studio settings autosave to `%LOCALAPPDATA%/FluxFlow/Studio/settings.json`.
  WebView2 browser data is kept in the adjacent `WebView2` folder. Browser Studio
  preferences remain separate. The editor retains the shared configuration,
  project serialization and Undo/Redo implementation.

## Execution

The host registers the existing local runner through DI. It starts without Docker.
Existing service endpoints need no Docker integration; Docker Desktop must be
available only when a project requests managed containers. Studio does not install
or start Docker Desktop. Run and Stop use the same preparation and cleanup APIs as
the local web companion. Closing the window disposes the editor scope and stops the
host, cancelling active preparation and cleaning up owned environments.

Execution retains the existing MQTT-to-HTTP slice and its supported registrations.
This host does not expand execution support to every component in the palette.
Remote runner selection, pairing, native microphone capture, installers and other
desktop platforms remain separate work. A future remote adapter can implement
`IEnvironmentRunner` without moving platform file access out of this desktop host.
