using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed class SessionOptionsTests
{
    [Fact]
    public async Task UseStore_rejects_null_lease_from_delegate()
    {
        var options = new SessionComponentOptions()
            .UseStore((_, _) => ValueTask.FromResult<SessionStoreLease>(null!));

        var act = async () => await options.StoreFactory.OpenAsync(new SessionStoreContext());

        var exception = await act.ShouldThrowAsync<InvalidOperationException>();
        exception.Message.ShouldBe("Session store factory delegate returned a null lease.");
    }

    [Fact]
    public async Task UseSharedStore_rejects_null_store_from_delegate()
    {
        var options = new SessionComponentOptions()
            .UseSharedStore(_ => null!);

        var act = async () => await options.StoreFactory.OpenAsync(new SessionStoreContext());

        var exception = await act.ShouldThrowAsync<InvalidOperationException>();
        exception.Message.ShouldBe("Shared session store factory returned null.");
    }

    [Fact]
    public async Task UseStore_rejects_null_context_before_invoking_delegate()
    {
        var invoked = false;
        var options = new SessionComponentOptions()
            .UseStore((_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(SessionStoreLease.Shared(new EmptySessionStore()));
            });

        var act = async () => await options.StoreFactory.OpenAsync(null!);

        await act.ShouldThrowAsync<ArgumentNullException>();
        invoked.ShouldBeFalse();
    }

    [Fact]
    public async Task UseStore_receives_normalized_context_values()
    {
        SessionStoreContext? received = null;
        var options = new SessionComponentOptions()
            .UseStore((context, _) =>
            {
                received = context;
                return ValueTask.FromResult(SessionStoreLease.Shared(new EmptySessionStore()));
            });

        await using var lease = await options.StoreFactory.OpenAsync(new SessionStoreContext
        {
            StoreName = " tenant-a ",
            SessionId = " session-1 "
        });

        received.ShouldNotBeNull();
        received.StoreName.ShouldBe("tenant-a");
        received.SessionId.ShouldBe("session-1");
    }

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
    public void Recorder_options_normalize_text_and_copy_tags()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "north"
        };

        var options = new SessionRecorderOptions
        {
            Store = " store ",
            SessionId = " session-1 ",
            Name = " sample ",
            Notes = " note ",
            Tags = tags,
            BoundedCapacity = 4
        };
        tags["tenant"] = "changed";
        tags["new"] = "value";

        options.Store.ShouldBe("store");
        options.SessionId.ShouldBe("session-1");
        options.Name.ShouldBe("sample");
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
            Store = " store ",
            SessionId = " session-1 ",
            Mode = SessionReplayMode.FixedInterval,
            BoundedCapacity = 4,
            StartSequence = 1,
            MaxMessages = 10,
            FixedIntervalMilliseconds = 0,
            SpeedMultiplier = 2
        };

        options.Store.ShouldBe("store");
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
            Store = " store ",
            Name = " exact ",
            NamePrefix = " pre ",
            Tags = tags,
            Limit = 10,
            BoundedCapacity = 4
        };
        tags["kind"] = "changed";
        tags["new"] = "value";

        options.Store.ShouldBe("store");
        options.Name.ShouldBe("exact");
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
            Store = " ",
            SessionId = "\t",
            Name = "\r\n",
            Notes = " ",
            Tags = null!
        };
        var replay = new SessionReplayOptions
        {
            Store = " ",
            SessionId = "\t"
        };
        var query = new SessionQueryOptions
        {
            Store = " ",
            Name = "\t",
            NamePrefix = "\r\n",
            Tags = null!
        };

        recorder.Store.ShouldBeNull();
        recorder.SessionId.ShouldBeNull();
        recorder.Name.ShouldBeNull();
        recorder.Notes.ShouldBeNull();
        recorder.Tags.ShouldBeEmpty();
        recorder.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        replay.Store.ShouldBeNull();
        replay.SessionId.ShouldBeNull();
        query.Store.ShouldBeNull();
        query.Name.ShouldBeNull();
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
}
