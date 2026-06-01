using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Serialization;

public sealed class SerializationComponentModule : IFlowNodeModule
{
    public SerializationComponentModule()
    {
        Registrations =
        [
            new FlowNodeRegistration(
                SerializationComponentTypes.JsonParse,
                SerializationNodeFactory.CreateJsonParse),
            new FlowNodeRegistration(
                SerializationComponentTypes.JsonStringify,
                SerializationNodeFactory.CreateJsonStringify),
            new FlowNodeRegistration(
                SerializationComponentTypes.TextEncode,
                SerializationNodeFactory.CreateTextEncode),
            new FlowNodeRegistration(
                SerializationComponentTypes.TextDecode,
                SerializationNodeFactory.CreateTextDecode),
            new FlowNodeRegistration(
                SerializationComponentTypes.Base64Encode,
                SerializationNodeFactory.CreateBase64Encode),
            new FlowNodeRegistration(
                SerializationComponentTypes.Base64Decode,
                SerializationNodeFactory.CreateBase64Decode)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
