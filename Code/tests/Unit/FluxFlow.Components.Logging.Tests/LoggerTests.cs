using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Logging.Tests;

public sealed partial class LoggerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    {
        var target = new BufferBlock<FlowMessage>();
        source.LinkTo(target);
        return target;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_and_json_requests_preserve_template_values_scopes_and_lineage(bool json)
    {
        var log = new RecordingLogger();
        var args = new FlowValue?[] { "A12", new JsonFlowValue(JsonSerializer.SerializeToElement(42)), null };
        var attributes = new Dictionary<string, FlowValue?> { ["line"] = "west", ["active"] = new JsonFlowValue(JsonSerializer.SerializeToElement(true)) };
        var request = new LogRequest { LogLevel = LogLevel.Warning, MessageTemplate = "Order {Order} took {Elapsed:D4}; {Optional}", Args = args, Attributes = attributes };
        args[0] = "mutated";
        attributes["line"] = "mutated";
        request.Json.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["MessageTemplate", "LogLevel", "Args", "Attributes"]);
        request.Json.GetProperty("Attributes").EnumerateObject().Select(property => property.Name)
            .ShouldBe(["line", "active"]);
        FlowValue value = json ? new JsonFlowValue(request.Json) : request;
        var message = FlowMessage.Create(value, correlationId: CorrelationId.New());
        await using var node = new Logger(new() { Logger = log });
        var output = Capture(node.Output);
        await node.Input.SendAsync(message);
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        result.TraceId.ShouldBe(message.TraceId);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.CausationId.ShouldBe(message.MessageId);
        result.Value.ShouldBeOfType<LogRequest>();
        var entry = log.Entries.Single();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("Order A12 took 0042; (null)");
        entry.State["{OriginalFormat}"].ShouldBe(request.MessageTemplate);
        entry.State["Elapsed"].ShouldBeOfType<long>().ShouldBe(42);
        entry.State["Optional"].ShouldBeNull();
        entry.Attributes["line"].ShouldBe("west");
        entry.Attributes["active"].ShouldBe(true);
        log.Scopes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Array_errors_are_independent_and_later_requests_still_execute()
    {
        var log = new RecordingLogger();
        await using var node = new Logger(new() { Logger = log });
        var output = Capture(node.Output);
        var message = Message(new object?[]
        {
            new { logLevel = "Information", messageTemplate = "First {Id}", args = new[] { 1 } },
            null,
            new { logLevel = "Error", messageTemplate = "Missing {Argument}" },
            new { logLevel = "Debug", messageTemplate = "Last" }
        });
        await node.Input.SendAsync(message);
        var results = new List<FlowMessage>();
        for (var i = 0; i < 4; i++) results.Add(await output.ReceiveAsync(Deadline));
        results.Select(result => result.IsError).ShouldBe([false, true, true, false]);
        results.ShouldAllBe(result => result.TraceId == message.TraceId);
        results.Select(result => result.MessageId).Distinct().Count().ShouldBe(4);
        log.Entries.Select(entry => entry.Message).ShouldBe(["First 1", "Last"]);
    }

    [Fact]
    public async Task Empty_array_and_upstream_errors_do_not_log()
    {
        var log = new RecordingLogger();
        await using var node = new Logger(new() { Logger = log });
        var output = Capture(node.Output);
        await node.Input.SendAsync(Message(Array.Empty<object>()));
        var error = FlowMessage.Create(new FlowError("upstream", "failed", "test"));
        await node.Input.SendAsync(error);
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        output.Count.ShouldBe(0);
        log.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Literal {{braces}}", 0, false)]
    [InlineData("{{{Name}}}", 1, false)]
    [InlineData("{Value,8:D2}", 1, false)]
    [InlineData("Missing {Value}", 0, true)]
    [InlineData("Extra", 1, true)]
    [InlineData("{Unclosed", 0, true)]
    [InlineData("Unclosed}", 0, true)]
    [InlineData("{}", 1, true)]
    public async Task Templates_use_native_formatting_with_argument_validation(string template, int count, bool error)
    {
        var log = new RecordingLogger();
        await using var node = new Logger(new() { Logger = log });
        var output = Capture(node.Output);
        await node.Input.SendAsync(Message(new { logLevel = "Information", messageTemplate = template, args = Enumerable.Repeat(12, count).ToArray() }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBe(error);
        log.Entries.Count.ShouldBe(error ? 0 : 1);
    }

    [Fact]
    public async Task Provider_failures_become_errors_and_native_exceptions_are_passed_through()
    {
        var log = new RecordingLogger { FailNext = true };
        var exception = new InvalidOperationException("original");
        await using var node = new Logger(new() { Logger = log });
        var output = Capture(node.Output);
        var request = new LogRequest { LogLevel = LogLevel.Error, MessageTemplate = "Failure", Exception = exception };
        await node.Input.SendAsync(FlowMessage.Create(request));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await node.Input.SendAsync(FlowMessage.Create(request));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        log.Entries.Single().Exception.ShouldBeSameAs(exception);
        log.Scopes.ShouldBeEmpty();
        await node.DisposeAsync();
        log.Disposed.ShouldBeFalse();
    }

    private sealed record Entry(LogLevel Level, string Message, Dictionary<string, object?> State,
        Dictionary<string, object?> Attributes, Exception? Exception);

    private sealed class RecordingLogger : ILogger, IDisposable
    {
        public readonly ConcurrentQueue<Entry> Entries = new();
        public readonly List<object> Scopes = [];
        public bool FailNext;
        public bool Disposed;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return new Scope(() => Scopes.Remove(state));
        }
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (FailNext) { FailNext = false; throw new IOException("log provider failed"); }
            var attributes = Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary();
            Entries.Enqueue(new(level, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(), attributes, exception));
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
