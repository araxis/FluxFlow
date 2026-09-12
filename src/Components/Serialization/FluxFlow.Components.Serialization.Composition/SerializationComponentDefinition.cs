using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Composition;

public static partial class SerializationComponentDefinition
{
    public static class Options
    {
        public const string BoundedCapacity = "boundedCapacity";
        public const string DefaultEncoding = "defaultEncoding";
        public const string MaxInputBytes = "maxInputBytes";
        public const string MaxOutputBytes = "maxOutputBytes";
        public const string WriteIndented = "writeIndented";
        public const string AllowTrailingCommas = "allowTrailingCommas";
        public const string SkipComments = "skipComments";
    }

    public static class Types
    {
        public const string JsonParse = "json.parse";
        public const string JsonStringify = "json.stringify";
        public const string TextEncode = "text.encode";
        public const string TextDecode = "text.decode";
        public const string Base64Encode = "base64.encode";
        public const string Base64Decode = "base64.decode";
    }

    public static class Ports
    {
        public const string Input = FlowPorts.InputName;
        public const string Output = FlowPorts.OutputName;
        public const string Events = FlowPorts.EventsName;
    }
    public static class Resources { public const string Clock = "clock"; }
}
