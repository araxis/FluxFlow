using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new Exception("The Core consumer must run with JSON reflection disabled.");
if (JsonFlowValue.Null.Json.ValueKind != JsonValueKind.Null)
    throw new Exception("The null value did not preserve JSON null.");

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
await using var node = new Increment();
var received = new BufferBlock<FlowMessage>();
using var link = node.Output.LinkTo(received, new DataflowLinkOptions { PropagateCompletion = true });
var payload = new NumberValue(41);
var input = FlowMessage.Create(payload, new CorrelationId("standalone"));
if (!await node.Input.SendAsync(input, deadline.Token))
    throw new Exception("Input rejected.");
var result = await received.ReceiveAsync(deadline.Token);
if (result.Value is not NumberValue { Number: 42 } || result.CausationId != input.MessageId || result.TraceId != input.TraceId || result.CorrelationId != input.CorrelationId || payload.JsonBuilds != 0)
    throw new Exception("Standalone processing did not preserve typed values and lineage.");
var headers = new Dictionary<string, FlowValue>
{
    ["source"] = "consumer"
};
var enriched = result.WithHeaders(headers);
headers["source"] = "changed";
if (enriched.MessageId != result.MessageId || enriched.Timestamp != result.Timestamp || !ReferenceEquals(enriched.Value, result.Value) || enriched.Headers["source"].Json.GetString() != "consumer")
    throw new Exception("Header enrichment changed identity or failed to snapshot headers.");
var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(enriched, FlowJsonContext.Default.FlowMessage), FlowJsonContext.Default.FlowMessage)!;
if (restored.MessageId != enriched.MessageId || restored.Value.Json.GetInt32() != 42)
    throw new Exception("Envelope JSON round-trip failed.");
var details = new NumberValue(5);
var error = new FlowError("busy", "Try later.", "consumer", details: details);
if (!ReferenceEquals(error.Details, details) || details.JsonBuilds != 0)
    throw new Exception("Error details lost typed identity or forced JSON conversion.");
var restoredError = JsonSerializer.Deserialize(error.Json, FlowJsonContext.Default.FlowError)!;
if (restoredError.Details!.Json.GetInt32() != 5 || details.JsonBuilds != 1)
    throw new Exception("Error details JSON round-trip failed.");
await using var requests = new FlowRequestReply();
var eventDetails = new NumberValue(7);
var eventValue = new FlowEvent
{
    Name = "processed",
    Details = eventDetails,
    Attributes = new Dictionary<string, object?>
    {
        ["text"] = "quoted \"text\"\n",
        ["count"] = 42,
        ["active"] = true,
        ["nothing"] = null,
        ["payload"] = JsonSerializer.SerializeToElement(new Reading(23), ConsumerJsonContext.Default.Reading)
    },
    Dimensions = new Dictionary<string, object?> { ["elapsed"] = TimeSpan.FromSeconds(5) },
    Measurements = new Dictionary<string, double> { ["duration"] = 1.25 }
};
var eventMessage = eventValue.ToMessage(input.CorrelationId);
if (!ReferenceEquals(eventMessage.Value, eventValue) || !ReferenceEquals(eventMessage.ToFlowEvent(), eventValue) || eventDetails.JsonBuilds != 0)
    throw new Exception("Event publication lost typed identity or forced JSON conversion.");
var restoredEvent = JsonSerializer.Deserialize(JsonSerializer.Serialize<FlowMessage>(eventMessage, FlowJsonContext.Default.FlowMessage), FlowJsonContext.Default.FlowMessage)!;
if (restoredEvent.ToFlowEvent().Details.Json.GetInt32() != 7 || restoredEvent.CorrelationId != input.CorrelationId || eventDetails.JsonBuilds != 1)
    throw new Exception("Event envelope JSON round-trip failed.");
var eventJson = restoredEvent.Value.Json;
var attributes = eventJson.GetProperty("Attributes");
if (attributes.GetProperty("text").GetString() != "quoted \"text\"\n"
    || attributes.GetProperty("count").GetInt32() != 42
    || !attributes.GetProperty("active").GetBoolean()
    || attributes.GetProperty("nothing").ValueKind != JsonValueKind.Null
    || attributes.GetProperty("payload").GetProperty("Temperature").GetInt32() != 23
    || eventJson.GetProperty("Dimensions").GetProperty("elapsed").GetString() != "00:00:05"
    || eventJson.GetProperty("Measurements").GetProperty("duration").GetDouble() != 1.25)
    throw new Exception("Event JSON lost scalar types, custom payload data or measurements.");

var options = new JsonSerializerOptions()
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(ConsumerJsonContext.Default, FlowJsonContext.Default),
    Converters = { new FlowMessageJsonConverter<ReadingValue>() }
};
var reading = new Reading(19);
if (JsonSerializer.Deserialize<Reading>(JsonSerializer.Serialize(reading, options), options) != reading
    || JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize(enriched, options), options)!.MessageId != enriched.MessageId)
    throw new Exception("Host and Core generated contexts did not compose.");

var typedMessage = FlowMessage.Create(new ReadingValue(19), correlationId: new("typed"), headers: headers);
var typedJson = JsonSerializer.Serialize(typedMessage, options);
var restoredTyped = JsonSerializer.Deserialize<FlowMessage<ReadingValue>>(typedJson, options)!;
if (restoredTyped.Value.Temperature != 19 || restoredTyped.MessageId != typedMessage.MessageId
    || restoredTyped.CorrelationId != typedMessage.CorrelationId
    || typedJson != JsonSerializer.Serialize<FlowMessage>(typedMessage, FlowJsonContext.Default.FlowMessage))
    throw new Exception("Explicit typed converter changed payload, identity or envelope format.");

var failingInput = FlowMessage.Create(new NumberValue(-1), new CorrelationId("failure"));
if (!await node.Input.SendAsync(failingInput, deadline.Token))
    throw new Exception("Failing input rejected.");
var failed = await received.ReceiveAsync(deadline.Token);
var restoredFailure = JsonSerializer.Deserialize(JsonSerializer.Serialize(failed, FlowJsonContext.Default.FlowMessage), FlowJsonContext.Default.FlowMessage)!;
if (!restoredFailure.IsError || restoredFailure.Error is not { Code: "node.processing_failed", Message: "Processing expired.", IsTransient: true }
    || restoredFailure.Error.Details!.Json.GetProperty("exceptionType").GetString() != typeof(TimeoutException).FullName
    || restoredFailure.CausationId != failingInput.MessageId || restoredFailure.CorrelationId != failingInput.CorrelationId)
    throw new Exception("Processing failure did not preserve error details and lineage.");
var reply = await requests.RequestAsync("Reply", input, (message, _) =>
{
    if (!requests.Reply("Reply", message))
        throw new Exception("Reply did not match.");
    return ValueTask.CompletedTask;
}, TimeSpan.FromSeconds(5), deadline.Token);
if (!ReferenceEquals(reply.Value, payload) || requests.PendingCount != 0)
    throw new Exception("Component request/reply failed.");
node.Complete();
await node.Completion.WaitAsync(deadline.Token);
await received.Completion.WaitAsync(deadline.Token);
Console.WriteLine("Standalone Core processing, lineage, reflection-disabled JSON and request/reply passed.");
sealed class NumberValue(int number) : FlowValue
{
    public int Number { get; } = number;
    public int JsonBuilds { get; private set; }

    protected override JsonElement CreateJson()
    {
        JsonBuilds++;
        return JsonSerializer.SerializeToElement(Number, ConsumerJsonContext.Default.Int32);
    }
}

sealed class Increment : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public Increment()
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, Transform, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Transform(FlowMessage message)
    {
        var number = ((NumberValue)message.Value).Number;
        if (number < 0) throw new TimeoutException("Processing expired.");
        return [message.WithValue(new NumberValue(number + 1))];
    }
}

sealed record Reading(int Temperature);

sealed class ReadingValue(int temperature) : FlowValue
{
    public int Temperature { get; } = temperature;
    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, ConsumerJsonContext.Default.ReadingValue);
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Reading))]
[JsonSerializable(typeof(ReadingValue))]
[JsonSerializable(typeof(FlowMessage<ReadingValue>))]
internal partial class ConsumerJsonContext : JsonSerializerContext;
