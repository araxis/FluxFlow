using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Filtering;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.Reducing;
using FluxFlow.Configuration.Components.Resilience;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class RemainingComponentConfigurationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("changeFilter", "csharp", "headers.key", 3)]
    [InlineData("changeFilter", "jsonata", "$headers.key", 3)]
    [InlineData("deduplicate", "csharp", "headers.key", 2)]
    [InlineData("deduplicate", "jsonata", "$headers.key", 2)]
    public async Task Filters_use_message_context_and_keep_original_messages(string type, string engine, string expression, int count)
    {
        using var config = Config(type, ("Engine", engine), ("KeySelector", expression),
            ("IgnoreCase", "true"), ("Retention", "00:00:10"));
        var services = Services();
        services.AddFluxFlowApplication(config);
        config["Workflows:main:node:KeySelector"] = "invalid after registration";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        AssertPorts(provider);
        await app.StartAsync();
        var input = Input(provider);
        var sent = new List<FlowMessage>();
        foreach (var key in new[] { "A", "a", "B", "A" })
        {
            var message = FlowMessage.Create((FlowValue)key, headers: new Dictionary<string, FlowValue> { ["key"] = key });
            sent.Add(message);
            await input.SendAsync(message);
        }
        var marker = FlowMessage.Create(new FlowError("marker", "marker", "test"));
        await input.SendAsync(marker);
        var received = Capture(provider);
        for (var i = 0; i < count; i++)
            (await received.ReceiveAsync(Deadline)).ShouldBeSameAs(sent[i == 0 ? 0 : i + 1]);
        (await received.ReceiveAsync(Deadline)).ShouldBeSameAs(marker);
        var errors = Capture(provider, "errors");
        await app.StopAsync().WaitAsync(Deadline);
        received.Count.ShouldBe(0);
        (await errors.ReceiveAsync(Deadline)).ShouldBeSameAs(marker);
    }

    [Fact]
    public async Task Deduplicate_uses_host_clock_for_retention()
    {
        using var config = Config("deduplicate", ("Engine", "csharp"), ("KeySelector", "value"),
            ("Retention", "00:00:05"), ("MaxKeys", "1"));
        var time = new FakeTimeProvider();
        var services = Services();
        services.AddSingleton<TimeProvider>(time);
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var input = Input(provider);
        var received = Capture(provider);
        foreach (var key in new[] { "A", "B", "A" })
        {
            await input.SendAsync(FlowMessage.Create((FlowValue)key));
            (await received.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe(key);
        }
        time.Advance(TimeSpan.FromSeconds(5));
        await input.SendAsync(FlowMessage.Create((FlowValue)"A"));
        (await received.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("A");
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData("csharp", "state + input", "previous == current")]
    [InlineData("jsonata", "$state + $input", "$previous = $current")]
    public async Task Reducer_compiles_value_callbacks_and_only_emits_changed_state(string engine, string reduce, string equal)
    {
        using var config = Config("reducer", ("Engine", engine), ("InitialState", "0"), ("Reduce", reduce), ("AreEqual", equal));
        var services = Services();
        services.AddFluxFlowApplication(config);
        config["Workflows:main:node:InitialState"] = "1000";
        config["Workflows:main:node:Reduce"] = "invalid after registration";
        for (var build = 0; build < 2; build++)
        {
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            AssertPorts(provider);
            await app.StartAsync();
            var input = Input(provider);
            var first = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(2)), headers: new Dictionary<string, FlowValue> { ["site"] = "SE" });
            await input.SendAsync(first);
            await input.SendAsync(FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(0))));
            await input.SendAsync(FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(3))));
            var received = Capture(provider);
            var result = await received.ReceiveAsync(Deadline);
            result.Value.Json.GetInt32().ShouldBe(2);
            result.TraceId.ShouldBe(first.TraceId);
            result.CausationId.ShouldBe(first.MessageId);
            result.Headers["site"].ShouldBeSameAs(first.Headers["site"]);
            (await received.ReceiveAsync(Deadline)).Value.Json.GetInt32().ShouldBe(5);
            await app.StopAsync().WaitAsync(Deadline);
            received.Count.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("csharp", "message.IsError && value.Code == \"again\" && headers.site == \"SE\" && messageId != null")]
    [InlineData("jsonata", "Code = 'again' and $headers.site = 'SE' and $exists($messageId)")]
    public async Task Retry_registers_request_reply_links_and_preserves_outer_correlation(string? engine, string? predicate)
    {
        using var config = Config("retry", ("MaxRetryAttempts", "1"), ("Delay", "00:00:00"),
            ("AttemptTimeout", "00:00:05"), ("Engine", engine), ("ShouldRetry", predicate),
            ("Request", "worker.Input"), ("Reply", "worker.Output"));
        config["Workflows:main:worker:Type"] = "worker";
        var attempts = new List<FlowMessage>();
        var services = Services();
        services.AddKeyedSingleton<ComponentRegistration>("worker", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            registrations.AddComponentFactory(address, _ => new Responder(message =>
            {
                attempts.Add(message);
                return message.WithValue(attempts.Count == 1
                    ? new FlowError("again", "retry me", "test", isTransient: predicate is null)
                    : (FlowValue)"done");
            }))
                .AddInputPort<Responder>(address, "Input", node => node.Input)
                .AddOutputPort<Responder>(address, "Output", node => node.Output);
        });
        services.AddFluxFlowApplication(config);
        attempts.ShouldBeEmpty();
        config["Workflows:main:node:MaxRetryAttempts"] = "0";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        AssertPorts(provider, requestReply: true);
        await app.StartAsync();
        var original = FlowMessage.Create((FlowValue)"original", CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["site"] = "SE" });
        await Input(provider).SendAsync(original);
        var result = await Capture(provider).ReceiveAsync(Deadline);
        result.Value.Json.GetString().ShouldBe("done");
        result.TraceId.ShouldBe(original.TraceId);
        result.CorrelationId.ShouldBe(original.CorrelationId);
        attempts.Count.ShouldBe(2);
        attempts[0].CorrelationId.ShouldNotBe(attempts[1].CorrelationId);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimit_resolves_deferred_shared_resource_and_releases_lease_after_reply(bool local)
    {
        using var config = Config("rateLimit", ("Limiter", "limits"), ("ResponseTimeout", "00:00:05"),
            ("Request", "requests.Input"));
        var resourcePath = (local ? "Workflows:main:" : "") + "Resources:limits:Type";
        config[resourcePath] = "test.limiter";
        config["Workflows:main:requests:Type"] = "capture";
        var constructions = 0;
        var services = Services();
        services.AddKeyedSingleton<ResourceRegistration>("test.limiter", (registrations, address, _) =>
            registrations.AddFluxFlowResource<RateLimiter>(address, _ =>
            {
                constructions++;
                return new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 2 });
            }));
        services.AddFluxFlowApplication(config);
        constructions.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        constructions.ShouldBe(0);
        await using var app = await provider.BuildApplicationAsync();
        constructions.ShouldBe(1);
        AssertPorts(provider, requestReply: true);
        var limiter = provider.GetWorkflowServices("main").GetRequiredFlowResource<RateLimiter>("limits", "main");
        await app.StartAsync();
        var input = Input(provider);
        var requests = Capture(provider, "requests");
        await input.SendAsync(FlowMessage.Create((FlowValue)"first"));
        var first = await requests.ReceiveAsync(Deadline);
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(0);
        await input.SendAsync(FlowMessage.Create((FlowValue)"second"));
        await provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Reply").SendAsync(first.WithValue((FlowValue)"done"));
        (await Capture(provider).ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("done");
        var second = await requests.ReceiveAsync(Deadline);
        await provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Reply").SendAsync(second.WithValue((FlowValue)"done"));
        await Capture(provider).ReceiveAsync(Deadline);
        await app.StopAsync().WaitAsync(Deadline);
        if (!local) limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(1);
        await app.DisposeAsync();
        if (local) Should.Throw<ObjectDisposedException>(() => limiter.AttemptAcquire());
        else { using var lease = limiter.AttemptAcquire(); lease.IsAcquired.ShouldBeTrue(); }
        await provider.DisposeAsync();
        Should.Throw<ObjectDisposedException>(() => limiter.AttemptAcquire());
    }

    [Theory]
    [InlineData("changeFilter", "Engine", null)]
    [InlineData("changeFilter", "KeySelector", null)]
    [InlineData("changeFilter", "OutputCapacity", "0")]
    [InlineData("deduplicate", "Retention", "00:00:00")]
    [InlineData("deduplicate", "MaxKeys", "0")]
    [InlineData("reducer", "InitialState", null)]
    [InlineData("reducer", "Reduce", null)]
    [InlineData("reducer", "AreEqual", null)]
    [InlineData("rateLimit", "Limiter", null)]
    [InlineData("rateLimit", "ResponseTimeout", "00:00:00")]
    [InlineData("rateLimit", "ResponseTimeout", "60.00:00:00")]
    [InlineData("retry", "AttemptTimeout", "00:00:00")]
    [InlineData("retry", "MaxRetryAttempts", "-1")]
    [InlineData("retry", "Delay", "-00:00:01")]
    [InlineData("retry", "BackoffType", "invalid")]
    [InlineData("retry", "MaxDelay", "-00:00:01")]
    public void Invalid_settings_reject_the_entire_registration(string type, string property, string? value)
    {
        using var config = Valid(type);
        config["Workflows:main:node:" + property] = value;
        var services = Services();
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
        error.ToString().ShouldContain("main.node");
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData("changeFilter")]
    [InlineData("deduplicate")]
    [InlineData("reducer")]
    [InlineData("retry")]
    public async Task Expressions_compile_at_build_once_and_support_file_sources(string type)
    {
        var path = Path.Combine(Path.GetTempPath(), "component-expression-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            using var config = Valid(type);
            var property = type == "reducer" ? "Reduce" : type == "retry" ? "ShouldRetry" : "KeySelector";
            config["Workflows:main:node:Engine"] = "counted";
            config["Workflows:main:node:" + property] = path;
            var engine = new CountedEngine();
            var services = Services();
            services.AddKeyedSingleton<IFlowExpressionEngine>("counted", engine);
            services.AddFluxFlowApplication(config);
            engine.Compiles.ShouldBe(0);
            await File.WriteAllTextAsync(path, "test");
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            var count = type == "reducer" ? 2 : 1;
            engine.Compiles.ShouldBe(count);
            await File.WriteAllTextAsync(path, "changed");
            (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
            engine.Compiles.ShouldBe(count);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("changeFilter")]
    [InlineData("deduplicate")]
    [InlineData("reducer")]
    [InlineData("retry")]
    public async Task Unknown_engine_is_rejected_before_start(string type)
    {
        using var config = Valid(type);
        config["Workflows:main:node:Engine"] = "missing";
        if (type == "retry") config["Workflows:main:node:ShouldRetry"] = "true";
        var services = Services();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
        error.ToString().ShouldContain("missing");
    }

    private static ConfigurationRoot Valid(string type) => type switch
    {
        "changeFilter" or "deduplicate" => Config(type, ("Engine", "csharp"), ("KeySelector", "value"), ("Retention", "00:00:05")),
        "reducer" => Config(type, ("Engine", "csharp"), ("InitialState", "0"), ("Reduce", "state + input"), ("AreEqual", "previous == current")),
        "rateLimit" => Config(type, ("Limiter", "limits")),
        _ => Config(type)
    };

    private static ConfigurationRoot Config(string type, params (string Name, string? Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = type,
            ["Workflows:main:node:Output"] = "sink.Input",
            ["Workflows:main:node:Error"] = "errors.Input",
            ["Workflows:main:node:Events"] = "events.Input",
            ["Workflows:main:sink:Type"] = "capture",
            ["Workflows:main:errors:Type"] = "capture",
            ["Workflows:main:events:Type"] = "capture"
        };
        foreach (var (name, value) in settings) values["Workflows:main:node:" + name] = value;
        return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection().AddChangeFilter().AddDeduplicate().AddReducer().AddRateLimit().AddRetry()
            .AddCSharpExpressionEngine().AddJsonataExpressionEngine();
        services.AddKeyedSingleton<ComponentRegistration>("capture", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => new OutputProbe())
                .AddInputPort<OutputProbe>(section.GetComponentAddress(), "Input", node => node.Input));
        return services;
    }

    private static ITargetBlock<FlowMessage> Input(IServiceProvider provider)
        => provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input");
    private static BufferBlock<FlowMessage> Capture(IServiceProvider provider, string name = "sink")
        => ((OutputProbe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main." + name)).Messages;
    private static void AssertPorts(IServiceProvider provider, bool requestReply = false)
    {
        Input(provider).ShouldNotBeNull();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output").ShouldNotBeNull();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Events").ShouldNotBeNull();
        if (!requestReply) return;
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Request").ShouldNotBeNull();
        var reply = provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Reply");
        provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node").TerminalInputs.ShouldContain(reply);
    }

    private sealed class Responder : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }
        public Responder(Func<FlowMessage, FlowMessage> respond)
        {
            Output = new BroadcastBlock<FlowMessage>(static message => message);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this);
            var input = new TransformBlock<FlowMessage, FlowMessage>(respond);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }
    }

    private sealed class CountedEngine : IFlowExpressionEngine
    {
        public string Name => "counted";
        public int Compiles { get; private set; }
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public Func<FlowMapContext, T> Compile<T>(string expression)
        {
            Compiles++;
            return _ => default!;
        }
    }
}
