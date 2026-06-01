namespace FluxFlow.Components.Payloads.Contracts;

public enum PayloadKind
{
    Empty = 0,
    JsonObject = 1,
    JsonArray = 2,
    JsonScalar = 3,
    Xml = 4,
    Base64 = 5,
    Text = 6,
    Binary = 7
}
