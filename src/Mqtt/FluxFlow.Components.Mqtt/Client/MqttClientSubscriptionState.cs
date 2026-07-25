using FluxFlow.Components.Mqtt.Subscriptions;

namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientSubscriptionState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MqttSubscriptionDefinition> _named;
    private readonly Dictionary<string, MqttSubscriptionDefinition> _inline =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MqttTriggerRegistration> _triggers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _claims = new(StringComparer.Ordinal);

    internal MqttClientSubscriptionState(
        IReadOnlyDictionary<string, MqttSubscriptionDefinition> namedSubscriptions)
    {
        _named = new Dictionary<string, MqttSubscriptionDefinition>(
            namedSubscriptions,
            StringComparer.Ordinal);
    }

    internal MqttSubscriptionDefinition? Resolve(MqttSubscriptionTarget target)
    {
        lock (_gate)
            return ResolveUnsafe(target);
    }

    internal MqttTriggerRegistration AddTrigger(
        string clientName,
        MqttTriggerRegistrationOptions options,
        Func<MqttTriggerRegistration, ValueTask> dispose,
        ICollection<(string Identity, MqttSubscriptionDefinition Definition)> inlineToSubscribe)
    {
        lock (_gate)
        {
            if (_triggers.ContainsKey(options.TriggerId))
            {
                throw new InvalidOperationException(
                    $"MQTT trigger '{options.TriggerId}' is already registered for client '{clientName}'.");
            }

            foreach (var target in options.Subscriptions)
            {
                if (_claims.TryGetValue(target.Identity, out var owner) &&
                    !string.Equals(owner, options.TriggerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"MQTT subscription '{target.Identity}' is already claimed by trigger '{owner}'.");
                }

                var definition = ResolveUnsafe(target);
                if (definition is not null &&
                    FindFilterOwnerUnsafe(definition.TopicFilter) is { } filterOwner &&
                    !string.Equals(filterOwner, options.TriggerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"MQTT topic filter '{definition.TopicFilter}' is already claimed by trigger '{filterOwner}'.");
                }
            }

            var registration = new MqttTriggerRegistration(options, dispose);
            _triggers.Add(options.TriggerId, registration);
            foreach (var target in options.Subscriptions)
            {
                _claims[target.Identity] = options.TriggerId;
                if (target.Inline is not null)
                {
                    _inline[target.Identity] = target.Inline;
                    inlineToSubscribe.Add((target.Identity, target.Inline));
                }
            }

            return registration;
        }
    }

    internal IReadOnlyList<string> RemoveTrigger(MqttTriggerRegistration registration)
    {
        lock (_gate)
        {
            if (!_triggers.Remove(registration.Options.TriggerId))
                return [];

            var inlineIdentities = new List<string>();
            foreach (var target in registration.Options.Subscriptions)
            {
                _claims.Remove(target.Identity);
                if (target.Inline is not null)
                {
                    _inline.Remove(target.Identity);
                    inlineIdentities.Add(target.Identity);
                }
            }

            return inlineIdentities;
        }
    }

    internal MqttTriggerRegistration[] DetachAllTriggers()
    {
        lock (_gate)
        {
            var registrations = _triggers.Values.ToArray();
            _triggers.Clear();
            _claims.Clear();
            _inline.Clear();
            return registrations;
        }
    }

    internal MqttNamedSubscriptionDecision EvaluateNamedChange(
        string name,
        MqttSubscriptionDefinition subscription)
    {
        lock (_gate)
        {
            if (_named.TryGetValue(name, out var existing) && existing == subscription)
                return new MqttNamedSubscriptionDecision(existing, null);

            var claimedBy = _triggers.Values
                .Where(registration => registration.Options.Subscriptions.Any(
                    target => string.Equals(target.Name, name, StringComparison.Ordinal)))
                .Select(registration => registration.Options.TriggerId)
                .SingleOrDefault();
            var filterOwner = FindFilterOwnerUnsafe(subscription.TopicFilter);
            return new MqttNamedSubscriptionDecision(
                null,
                claimedBy is not null && filterOwner is not null &&
                    !string.Equals(filterOwner, claimedBy, StringComparison.Ordinal)
                    ? filterOwner
                    : null);
        }
    }

    internal void SetNamed(string name, MqttSubscriptionDefinition subscription)
    {
        lock (_gate)
            _named[name] = subscription;
    }

    internal bool ContainsNamed(string name)
    {
        lock (_gate)
            return _named.ContainsKey(name);
    }

    internal void RemoveNamed(string name)
    {
        lock (_gate)
            _named.Remove(name);
    }

    internal (string Identity, MqttSubscriptionDefinition Definition)[] DesiredSubscriptions()
    {
        lock (_gate)
        {
            return _named
                .Select(static item => ($"name:{item.Key}", item.Value))
                .Concat(_inline.Select(static item => (item.Key, item.Value)))
                .OrderBy(static item => item.Item1, StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal string[] DesiredIdentities()
    {
        lock (_gate)
        {
            return _named.Keys
                .Select(static name => $"name:{name}")
                .Concat(_inline.Keys)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal MqttTriggerDispatchTarget[] Match(string topic)
    {
        lock (_gate)
        {
            return _triggers.Values
                .Select(registration => new MqttTriggerDispatchTarget(
                    registration,
                    ResolveMatchesUnsafe(registration.Options, topic)))
                .Where(static item => item.Matches.Length > 0)
                .ToArray();
        }
    }

    private string[] ResolveMatchesUnsafe(
        MqttTriggerRegistrationOptions options,
        string topic)
        => options.Subscriptions
            .Select(target => (Target: target, Definition: ResolveUnsafe(target)))
            .Where(item => item.Definition is not null &&
                MqttTopicFilterMatcher.IsMatch(topic, item.Definition.TopicFilter))
            .Select(item => item.Target.Name ?? item.Definition!.TopicFilter)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private MqttSubscriptionDefinition? ResolveUnsafe(MqttSubscriptionTarget target)
        => target.Inline ??
            (target.Name is not null && _named.TryGetValue(target.Name, out var named)
                ? named
                : null);

    private string? FindFilterOwnerUnsafe(string topicFilter)
    {
        foreach (var registration in _triggers.Values)
        {
            foreach (var target in registration.Options.Subscriptions)
            {
                var definition = ResolveUnsafe(target);
                if (definition is not null &&
                    string.Equals(definition.TopicFilter, topicFilter, StringComparison.Ordinal))
                {
                    return registration.Options.TriggerId;
                }
            }
        }

        return null;
    }
}

internal sealed record MqttNamedSubscriptionDecision(
    MqttSubscriptionDefinition? Existing,
    string? ConflictOwner);

internal sealed record MqttTriggerDispatchTarget(
    MqttTriggerRegistration Registration,
    string[] Matches);
