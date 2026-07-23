using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed class SessionOptionsTests
{
    [Fact]
    public void SessionStoreContext_normalizes_blank_values_and_null_clock()
    {
        var context = new SessionStoreContext
        {
            StoreName = " ",
            SessionId = "\t",
            Clock = null!
        };

        context.StoreName.ShouldBeNull();
        context.SessionId.ShouldBeNull();
        context.Clock.ShouldBe(TimeProvider.System);
    }

    [Fact]
    public async Task SessionStoreLease_disposes_only_owned_store()
    {
        var sharedStore = new EmptySessionStore();
        var ownedStore = new EmptySessionStore();

        await SessionStoreLease.Shared(sharedStore).DisposeAsync();
        await SessionStoreLease.Owned(ownedStore).DisposeAsync();

        sharedStore.DisposeCount.ShouldBe(0);
        ownedStore.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Service_registration_registers_keyed_store_and_factory()
    {
        var store = new EmptySessionStore();
        var factory = new EmptySessionStoreFactory(store);
        var services = new ServiceCollection()
            .AddFluxFlowSessionStore("sessions", store)
            .AddFluxFlowSessionStoreFactory("sessions-factory", factory);

        await using var provider = services.BuildServiceProvider();
        var resolvedStore = provider.GetRequiredKeyedService<ISessionStore>("sessions");
        var resolvedFactory = provider.GetRequiredKeyedService<ISessionStoreFactory>("sessions-factory");
        await using var lease = await resolvedFactory.OpenAsync(new SessionStoreContext
        {
            StoreName = "sessions"
        });

        resolvedStore.ShouldBeSameAs(store);
        resolvedFactory.ShouldBeSameAs(factory);
        lease.Store.ShouldBeSameAs(store);
        lease.OwnsStore.ShouldBeFalse();
    }

    [Fact]
    public async Task Service_registration_trims_keyed_store_and_factory_names()
    {
        var store = new EmptySessionStore();
        var factory = new EmptySessionStoreFactory(store);
        var services = new ServiceCollection()
            .AddFluxFlowSessionStore(" sessions ", store)
            .AddFluxFlowSessionStoreFactory(" sessions-factory ", factory);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ISessionStore>("sessions").ShouldBeSameAs(store);
        provider.GetRequiredKeyedService<ISessionStoreFactory>("sessions-factory").ShouldBeSameAs(factory);
    }

    [Fact]
    public void Service_registration_rejects_null_store_instances()
    {
        var services = new ServiceCollection();

        var storeException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSessionStore(
                "sessions",
                (ISessionStore)null!));
        var factoryException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSessionStoreFactory(
                "sessions-factory",
                (ISessionStoreFactory)null!));

        storeException.ParamName.ShouldBe("store");
        factoryException.ParamName.ShouldBe("storeFactory");
    }

    [Fact]
    public async Task Service_registration_rejects_null_provider_results()
    {
        var services = new ServiceCollection()
            .AddFluxFlowSessionStore(
                "sessions",
                static _ => null!)
            .AddFluxFlowSessionStoreFactory(
                "sessions-factory",
                static _ => null!);

        await using var provider = services.BuildServiceProvider();

        var storeException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISessionStore>("sessions"));
        var factoryException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISessionStoreFactory>("sessions-factory"));

        storeException.Message.ShouldBe("Session store provider returned null.");
        factoryException.Message.ShouldBe("Session store factory provider returned null.");
    }

    [Fact]
    public void Recorder_options_normalize_text_and_copy_tags()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "north"
        };

        var options = new SessionRecorderOptions
        {
            SessionId = " session-1 ",
            SessionName = " sample ",
            Notes = " note ",
            Tags = tags,
            BoundedCapacity = 4
        };
        tags["tenant"] = "changed";
        tags["new"] = "value";

        options.SessionId.ShouldBe("session-1");
        options.SessionName.ShouldBe("sample");
        options.Notes.ShouldBe("note");
        options.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        options.Tags["tenant"].ShouldBe("north");
        options.Tags.ContainsKey("new").ShouldBeFalse();
        options.BoundedCapacity.ShouldBe(4);
    }

    [Fact]
    public void Replay_options_normalize_text_and_validate_values()
    {
        var options = new SessionReplayOptions
        {
            SessionId = " session-1 ",
            Mode = SessionReplayMode.FixedInterval,
            BoundedCapacity = 4,
            StartSequence = 1,
            MaxMessages = 10,
            FixedIntervalMilliseconds = 0,
            SpeedMultiplier = 2
        };

        options.SessionId.ShouldBe("session-1");
        options.Mode.ShouldBe(SessionReplayMode.FixedInterval);
        options.BoundedCapacity.ShouldBe(4);
        options.StartSequence.ShouldBe(1);
        options.MaxMessages.ShouldBe(10);
        options.FixedIntervalMilliseconds.ShouldBe(0);
        options.SpeedMultiplier.ShouldBe(2);

        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { Mode = (SessionReplayMode)999 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { BoundedCapacity = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { StartSequence = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { MaxMessages = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { FixedIntervalMilliseconds = -1 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayOptions { SpeedMultiplier = 0 });
    }

    [Fact]
    public void Query_options_normalize_text_copy_tags_and_validate_values()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "demo"
        };

        var options = new SessionQueryOptions
        {
            SessionName = " exact ",
            NamePrefix = " pre ",
            Tags = tags,
            Limit = 10,
            BoundedCapacity = 4
        };
        tags["kind"] = "changed";
        tags["new"] = "value";

        options.SessionName.ShouldBe("exact");
        options.NamePrefix.ShouldBe("pre");
        options.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        options.Tags["kind"].ShouldBe("demo");
        options.Tags.ContainsKey("new").ShouldBeFalse();
        options.Limit.ShouldBe(10);
        options.BoundedCapacity.ShouldBe(4);

        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionQueryOptions { Limit = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionQueryOptions { BoundedCapacity = 0 });
    }

    [Fact]
    public void Options_treat_blank_optional_text_and_null_tags_as_absent()
    {
        var recorder = new SessionRecorderOptions
        {
            SessionId = "\t",
            SessionName = "\r\n",
            Notes = " ",
            Tags = null!
        };
        var replay = new SessionReplayOptions
        {
            SessionId = "\t"
        };
        var query = new SessionQueryOptions
        {
            SessionName = "\t",
            NamePrefix = "\r\n",
            Tags = null!
        };

        recorder.SessionId.ShouldBeNull();
        recorder.SessionName.ShouldBeNull();
        recorder.Notes.ShouldBeNull();
        recorder.Tags.ShouldBeEmpty();
        recorder.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        replay.SessionId.ShouldBeNull();
        query.SessionName.ShouldBeNull();
        query.NamePrefix.ShouldBeNull();
        query.Tags.ShouldBeEmpty();
        query.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    private sealed class EmptySessionStore : ISessionStore, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public Task<SessionMetadata?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionMetadata> StartSessionAsync(
            SessionStartRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionRecord> AppendMessageAsync(
            SessionAppendRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionMetadata> CompleteSessionAsync(
            SessionCompleteRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionMetadata>> QuerySessionsAsync(
            SessionQueryRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<SessionRecord> ReadMessagesAsync(
            SessionReadRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptySessionStoreFactory(ISessionStore store) : ISessionStoreFactory
    {
        public ValueTask<SessionStoreLease> OpenAsync(
            SessionStoreContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SessionStoreLease.Shared(store));
        }
    }
}
