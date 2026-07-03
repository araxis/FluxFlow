using FluxFlow.Components.Sessions.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed class SessionContractTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SessionRecordInput_normalizes_text_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "input"
        };

        var input = new SessionRecordInput
        {
            Timestamp = Timestamp,
            Type = " event ",
            Name = " created ",
            ContentType = " application/json ",
            Attributes = attributes
        };
        attributes["kind"] = "changed";
        attributes["new"] = "value";

        input.Type.ShouldBe("event");
        input.Name.ShouldBe("created");
        input.ContentType.ShouldBe("application/json");
        input.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        input.Attributes["kind"].ShouldBe("input");
        input.Attributes.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void SessionRecord_normalizes_text_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "record"
        };

        var record = new SessionRecord
        {
            SessionId = " session-1 ",
            Sequence = 1,
            Timestamp = Timestamp,
            Type = " event ",
            Name = " saved ",
            ContentType = " text/plain ",
            Attributes = attributes
        };
        attributes["kind"] = "changed";

        record.SessionId.ShouldBe("session-1");
        record.Type.ShouldBe("event");
        record.Name.ShouldBe("saved");
        record.ContentType.ShouldBe("text/plain");
        record.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        record.Attributes["kind"].ShouldBe("record");
    }

    [Fact]
    public void SessionMetadata_and_start_request_normalize_text_and_copy_tags()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "north"
        };

        var metadata = new SessionMetadata
        {
            SessionId = " session-1 ",
            Name = " sample ",
            StartedAt = Timestamp,
            Notes = " note ",
            Tags = tags
        };
        var start = new SessionStartRequest
        {
            SessionId = " session-2 ",
            Name = " replay ",
            StartedAt = Timestamp,
            Notes = " start note ",
            Tags = tags
        };
        tags["tenant"] = "changed";

        metadata.SessionId.ShouldBe("session-1");
        metadata.Name.ShouldBe("sample");
        metadata.Notes.ShouldBe("note");
        metadata.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        metadata.Tags["tenant"].ShouldBe("north");
        start.SessionId.ShouldBe("session-2");
        start.Name.ShouldBe("replay");
        start.Notes.ShouldBe("start note");
        start.Tags["tenant"].ShouldBe("north");
    }

    [Fact]
    public void Query_request_and_read_request_normalize_text_and_copy_tags()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "demo"
        };

        var query = new SessionQueryRequest
        {
            Name = " exact ",
            NamePrefix = " pre ",
            Tags = tags,
            CorrelationId = " corr-1 "
        };
        var read = new SessionReadRequest
        {
            SessionId = " session-1 "
        };
        tags["kind"] = "changed";

        query.Name.ShouldBe("exact");
        query.NamePrefix.ShouldBe("pre");
        query.CorrelationId.ShouldBe("corr-1");
        query.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        query.Tags["kind"].ShouldBe("demo");
        read.SessionId.ShouldBe("session-1");
    }

    [Fact]
    public void Append_and_complete_requests_copy_nested_contracts()
    {
        var tags = new Dictionary<string, string> { ["tenant"] = "north" };
        var attributes = new Dictionary<string, string> { ["kind"] = "input" };
        var session = new SessionMetadata
        {
            SessionId = " session-1 ",
            StartedAt = Timestamp,
            Tags = tags
        };
        var input = new SessionRecordInput
        {
            Name = " message ",
            Attributes = attributes
        };

        var append = new SessionAppendRequest
        {
            Session = session,
            Input = input,
            Sequence = 1,
            Timestamp = Timestamp
        };
        var complete = new SessionCompleteRequest
        {
            Session = session,
            EndedAt = Timestamp,
            MessageCount = 1
        };
        session.Tags["tenant"] = "changed";
        input.Attributes["kind"] = "changed";

        append.Session.SessionId.ShouldBe("session-1");
        append.Session.Tags["tenant"].ShouldBe("north");
        append.Input.Name.ShouldBe("message");
        append.Input.Attributes["kind"].ShouldBe("input");
        complete.Session.Tags["tenant"].ShouldBe("north");
    }

    [Fact]
    public void Query_result_normalizes_text_and_deep_copies_sessions_and_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "query"
        };
        var session = new SessionMetadata
        {
            SessionId = " session-1 ",
            StartedAt = Timestamp,
            Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant"] = "north"
            }
        };
        var sessions = new List<SessionMetadata> { session };

        var result = new SessionQueryResult
        {
            Timestamp = Timestamp,
            Operation = " query ",
            Succeeded = true,
            Count = 1,
            Sessions = sessions,
            CorrelationId = " corr-1 ",
            Message = " complete ",
            Attributes = attributes
        };
        attributes["source"] = "changed";
        session.Tags["tenant"] = "changed";
        sessions.Add(session with { SessionId = "session-2" });

        result.Operation.ShouldBe("query");
        result.CorrelationId.ShouldBe("corr-1");
        result.Message.ShouldBe("complete");
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Attributes["source"].ShouldBe("query");
        result.Sessions.Count.ShouldBe(1);
        result.Sessions[0].SessionId.ShouldBe("session-1");
        result.Sessions[0].Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Sessions[0].Tags["tenant"].ShouldBe("north");
    }

    [Fact]
    public void Contracts_treat_blank_optional_text_and_null_maps_as_absent()
    {
        var input = new SessionRecordInput
        {
            Type = " ",
            Name = "\t",
            ContentType = "\r\n",
            Attributes = null!
        };
        var query = new SessionQueryRequest
        {
            Name = " ",
            NamePrefix = "\t",
            Tags = null!,
            CorrelationId = " "
        };
        var result = new SessionQueryResult
        {
            Timestamp = Timestamp,
            Operation = " ",
            Succeeded = true,
            Count = 0,
            Sessions = null!,
            Message = " ",
            Attributes = null!
        };

        input.Type.ShouldBeNull();
        input.Name.ShouldBeNull();
        input.ContentType.ShouldBeNull();
        input.Attributes.ShouldBeEmpty();
        input.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        query.Name.ShouldBeNull();
        query.NamePrefix.ShouldBeNull();
        query.CorrelationId.ShouldBeNull();
        query.Tags.ShouldBeEmpty();
        query.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Operation.ShouldBeEmpty();
        result.Message.ShouldBeNull();
        result.Sessions.ShouldBeEmpty();
        result.Attributes.ShouldBeEmpty();
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }
}
