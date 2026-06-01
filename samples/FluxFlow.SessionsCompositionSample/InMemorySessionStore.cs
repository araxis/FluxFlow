using FluxFlow.Components.Sessions.Contracts;
using System.Runtime.CompilerServices;

namespace FluxFlow.SessionsCompositionSample;

internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public Task<SessionMetadata?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_gate)
        {
            return Task.FromResult(_sessions.TryGetValue(sessionId, out var state)
                ? state.Metadata
                : null);
        }
    }

    public Task<SessionMetadata> StartSessionAsync(
        SessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();
        var metadata = new SessionMetadata
        {
            SessionId = sessionId,
            Name = request.Name,
            StartedAt = request.StartedAt,
            Notes = request.Notes,
            Tags = CopyDictionary(request.Tags)
        };

        lock (_gate)
        {
            _sessions[sessionId] = new SessionState(metadata);
        }

        return Task.FromResult(metadata);
    }

    public Task<SessionRecord> AppendMessageAsync(
        SessionAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = new SessionRecord
        {
            SessionId = request.Session.SessionId,
            Sequence = request.Sequence,
            Timestamp = request.Timestamp,
            Type = request.Input.Type,
            Name = request.Input.Name,
            Payload = request.Input.Payload,
            ContentType = request.Input.ContentType,
            Attributes = CopyDictionary(request.Input.Attributes)
        };

        lock (_gate)
        {
            if (!_sessions.TryGetValue(request.Session.SessionId, out var state))
            {
                throw new InvalidOperationException(
                    $"Session '{request.Session.SessionId}' was not started.");
            }

            state.Records.Add(record);
        }

        return Task.FromResult(record);
    }

    public Task<SessionMetadata> CompleteSessionAsync(
        SessionCompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_sessions.TryGetValue(request.Session.SessionId, out var state))
            {
                throw new InvalidOperationException(
                    $"Session '{request.Session.SessionId}' was not started.");
            }

            state.Metadata = request.Session with
            {
                EndedAt = request.EndedAt,
                MessageCount = request.MessageCount
            };
            return Task.FromResult(state.Metadata);
        }
    }

    public async IAsyncEnumerable<SessionRecord> ReadMessagesAsync(
        SessionReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<SessionRecord> records;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(request.SessionId, out var state))
            {
                records = [];
            }
            else
            {
                IEnumerable<SessionRecord> query = state.Records
                    .OrderBy(record => record.Sequence);
                if (request.StartSequence.HasValue)
                {
                    query = query.Where(record => record.Sequence >= request.StartSequence.Value);
                }

                if (request.MaxMessages.HasValue)
                {
                    query = query.Take(request.MaxMessages.Value);
                }

                records = query
                    .Select(CopyRecord)
                    .ToList();
            }
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return record;
        }
    }

    private static SessionRecord CopyRecord(SessionRecord record)
        => record with
        {
            Attributes = CopyDictionary(record.Attributes)
        };

    private static Dictionary<string, string> CopyDictionary(Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private sealed class SessionState(SessionMetadata metadata)
    {
        public SessionMetadata Metadata { get; set; } = metadata;
        public List<SessionRecord> Records { get; } = [];
    }
}
