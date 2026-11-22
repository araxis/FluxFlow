using System.Text.Json;
using System.Text.Json.Nodes;

using FluxFlow.Environments;

namespace FluxFlow.Plugins.Http.WireMock;

// Patch only fields changed in the form; retain all other mapping options.
public sealed class WireMockEndpointDraft : IServiceDocumentItem
{
    private readonly JsonObject _mapping;
    private readonly string _method;
    private readonly string _path;
    private readonly int _status;
    private readonly string _body;
    private readonly bool _jsonBody;
    private readonly KeyValuePair<string, string>[] _headers;
    public string Method { get; set; }
    public string Path { get; set; }
    public int Status { get; set; }
    public string Body { get; set; }
    public bool JsonBody { get; set; }
    public List<DocumentPair> Headers { get; }
    public bool CanEdit { get; }
    public string Title => Path.Length == 0 ? "Advanced request" : Path;
    public string Summary => $"{Method} · {Status}";
    public IReadOnlyList<DocumentSection> Sections { get; }

    public WireMockEndpointDraft(JsonObject mapping)
    {
        _mapping = mapping;
        var request = (JsonObject)mapping["Request"]!;
        var response = (JsonObject)mapping["Response"]!;
        var methods = request["Methods"] as JsonArray;
        Method = _method = methods is { Count: 1 } ? Text(methods[0]) ?? "*" : "*";
        Path = _path = Text(request["Path"]) ?? "";
        Status = _status = response["StatusCode"] is JsonValue status && status.TryGetValue<int>(out var code) ? code : 200;
        JsonBody = _jsonBody = response.ContainsKey("BodyAsJson");
        Body = _body = JsonBody ? response["BodyAsJson"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null" : Text(response["Body"]) ?? "";
        var headers = response["Headers"] as JsonObject;
        Headers = headers?.Select(item => new DocumentPair { Name = item.Key, Value = Text(item.Value) ?? "" }).ToList() ?? [];
        _headers = Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).ToArray();
        CanEdit = (request["Path"] is null || Text(request["Path"]) is not null)
            && (!request.ContainsKey("Methods") || methods is { Count: 1 } && Text(methods[0]) is not null)
            && (!response.ContainsKey("StatusCode") || response["StatusCode"] is JsonValue s && s.TryGetValue<int>(out _))
            && (!response.ContainsKey("Headers") || headers is not null && headers.All(h => Text(h.Value) is not null))
            && (!response.ContainsKey("Body") || Text(response["Body"]) is not null)
            && !(response.ContainsKey("Body") && response.ContainsKey("BodyAsJson"))
            && !response.ContainsKey("BodyAsFile") && !response.ContainsKey("BodyAsBytes") && !response.ContainsKey("BodyFromBase64");
        Sections = [
            new("Request", [
                new DocumentChoice("Method", () => Method, v => Method = v, ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "*"]),
                new DocumentText("Path", () => Path, v => Path = v, "/api/orders")]),
            new("Response", [
                new DocumentInteger("Status code", () => Status, v => Status = v, 100, 599),
                new DocumentChoice("Body format", () => JsonBody ? "JSON" : "Text", v => JsonBody = v == "JSON", ["JSON", "Text"])],
                new("Headers", "response header", Headers),
                new("Response body", () => JsonBody ? "json" : "plaintext", () => Body, v => Body = v))
        ];
    }

    public JsonObject Apply()
    {
        var result = (JsonObject)_mapping.DeepClone();
        if (!CanEdit) return result;
        var request = (JsonObject)result["Request"]!;
        var response = (JsonObject)result["Response"]!;
        if (Method != _method)
        {
            if (Method == "*") request.Remove("Methods");
            else
            {
                if (string.IsNullOrWhiteSpace(Method) || Method.Any(c => !char.IsAsciiLetter(c))) throw new ArgumentException("Enter a valid HTTP method.");
                request["Methods"] = new JsonArray(JsonValue.Create(Method));
            }
        }
        if (Path != _path)
        {
            if (!Path.StartsWith('/') || Path.Any(char.IsWhiteSpace) || Path.Contains('?') || Path.Contains('#')) throw new ArgumentException("Path must start with / and exclude query strings and fragments.");
            request["Path"] = Path;
        }
        if (Status != _status)
        {
            if (Status is < 100 or > 599) throw new ArgumentException("Response status must be between 100 and 599.");
            response["StatusCode"] = Status;
        }
        if (Body != _body || JsonBody != _jsonBody)
        {
            var body = JsonBody ? JsonNode.Parse(Body) : JsonValue.Create(Body);
            response.Remove(JsonBody ? "Body" : "BodyAsJson");
            response[JsonBody ? "BodyAsJson" : "Body"] = body;
        }
        if (!Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).SequenceEqual(_headers))
        {
            var headers = new JsonObject();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in Headers)
            {
                if (string.IsNullOrWhiteSpace(header.Name) || header.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c)) || !names.Add(header.Name) || header.Value.Any(c => c is '\r' or '\n'))
                    throw new ArgumentException("Header names must be valid and unique; values cannot contain line breaks.");
                headers[header.Name] = header.Value;
            }
            response["Headers"] = headers;
        }
        return result;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
