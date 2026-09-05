namespace FluxFlow.Components.Payloads.Composition;

public static partial class PayloadsComponentDefinition
{
    public static class Options
    {
        public const string MaxInputBytes = "maxInputBytes";
        public const string MaxPreviewBytes = "maxPreviewBytes";
        public const string MaxFormattedChars = "maxFormattedChars";
        public const string DetectBase64 = "detectBase64";
        public const string FormatJson = "formatJson";
        public const string FormatXml = "formatXml";
        public const string BoundedCapacity = "boundedCapacity";
    }

    public static class Types { public const string Inspect = "payload.inspect"; }

    public static class Ports
    {
        public const string Input = "Input";
        public const string Output = "Output";
        public const string Events = "Events";
    }

    public static class Resources { public const string Clock = "clock"; }
}
