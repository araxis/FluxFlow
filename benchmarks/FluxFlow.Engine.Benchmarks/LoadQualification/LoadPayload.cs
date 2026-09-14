using System.Text.Json;
using FluxFlow.Data;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

/// <summary>Materializes the shared canonical value before any workload is measured.</summary>
internal static class LoadPayload
{
    internal static FlowValue Create(LoadOptions options)
    {
        options.Validate();
        if (options.PayloadKind == "json")
            return ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { data = new string('x', options.PayloadBytes) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var bytes = new byte[options.PayloadBytes];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index * 31 + 7);
        return ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(FlowContent.FromBytes(bytes, "application/octet-stream"), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }
}
