using System.Text.Json.Serialization;
using FluxFlow.Studio.UI.Features.Designer.Settings;

namespace FluxFlow.Studio.UI.Features.Designer.Projects;

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(StudioProject))]
[JsonSerializable(typeof(StudioSettings))]
public partial class StudioJsonContext : JsonSerializerContext;
