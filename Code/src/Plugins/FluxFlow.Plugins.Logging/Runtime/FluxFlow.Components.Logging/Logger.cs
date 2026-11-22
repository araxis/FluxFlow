using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Components.Logging;

/// <summary>Processes one log request or an array, with one result per item. The supplied logger is borrowed.</summary>
public sealed class Logger : FlowNode
{
    private readonly ILogger _logger;
    public ITargetBlock<FlowMessage> Input { get; }

    public Logger(LoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        _logger = options.Logger;
        Output = new(static message => message, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> ProcessAsync(FlowMessage message)
    {
        if (message.Value is LogRequest || message.Value.Json.ValueKind != JsonValueKind.Array)
            return Write(message);

        var results = new List<FlowMessage>();
        foreach (var item in message.Value.Json.EnumerateArray())
        {
            Stopping.ThrowIfCancellationRequested();
            var request = message.WithValue(new JsonFlowValue(item));
            results.AddRange(await request.ProcessMessageAsync(this, Write, Output, Stopping).ConfigureAwait(false));
        }
        return results;
    }

    private IEnumerable<FlowMessage> Write(FlowMessage message)
    {
        Stopping.ThrowIfCancellationRequested();
        var request = message.Value as LogRequest ?? message.Value.Json.Deserialize<LogRequest>(LogRequest.JsonOptions)
            ?? throw new JsonException("Expected a log request object.");
        ArgumentNullException.ThrowIfNull(request.MessageTemplate);
        if (!Enum.IsDefined(request.LogLevel)) throw new ArgumentOutOfRangeException(nameof(request.LogLevel));
        var args = request.Args.Select(value => value is null ? null : ToLogValue(value.Json)).ToArray();
        ValidateTemplate(request.MessageTemplate, args.Length);
        if (_logger.IsEnabled(request.LogLevel))
        {
            var attributes = request.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value is null ? null : ToLogValue(pair.Value.Json), StringComparer.Ordinal);
            using var scope = _logger.BeginScope(attributes);
            _logger.Log(request.LogLevel, request.Exception, request.MessageTemplate, args);
        }
        return [message.WithValue(request)];
    }

    private static object? ToLogValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? (object)integer : value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ToLogValue).ToArray(),
        _ => value.GetRawText()
    };

    // Count arguments without replacing the standard logging formatter or its alignment/format handling.
    private static void ValidateTemplate(string template, int argumentCount)
    {
        var count = 0;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] is not ('{' or '}')) continue;
            if (i + 1 < template.Length && template[i + 1] == template[i]) { i++; continue; }
            if (template[i] == '}') throw new FormatException("Unmatched closing brace in MessageTemplate.");
            var end = template.IndexOf('}', i + 1);
            if (end < 0 || end == i + 1 || template.AsSpan(i + 1, end - i - 1).Contains('{'))
                throw new FormatException("Invalid placeholder in MessageTemplate.");
            count++;
            i = end;
        }
        if (count != argumentCount)
            throw new ArgumentException($"MessageTemplate has {count} placeholders but Args has {argumentCount} items.");
    }
}
