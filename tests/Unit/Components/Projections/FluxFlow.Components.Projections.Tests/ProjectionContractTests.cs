using System.Text.Json;
using FluxFlow.Components.Projections.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Projections.Tests;

public sealed class ProjectionContractTests
{
    public static TheoryData<string> ContractNames =>
    [
        nameof(EventFilter),
        nameof(EventSummary),
        nameof(EventProjectionSnapshot),
        nameof(ProjectionEvent)
    ];

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Projection_contracts_create_ordinal_read_only_attribute_snapshots(
        string contractName)
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tenant"] = "north"
        };
        var contract = CreateContract(contractName, source);

        source["Tenant"] = "changed";
        source["Later"] = "ignored";

        var attributes = GetAttributes(contract);
        contract.GetType().GetProperty(nameof(EventFilter.Attributes))!
            .PropertyType.ShouldBe(typeof(IReadOnlyDictionary<string, string>));
        attributes.Count.ShouldBe(1);
        attributes["Tenant"].ShouldBe("north");
        attributes.ContainsKey("tenant").ShouldBeFalse();
        attributes.ContainsKey("Later").ShouldBeFalse();

        var mutable = (IDictionary<string, string>)attributes;
        Should.Throw<NotSupportedException>(() => mutable["Tenant"] = "changed");
        Should.Throw<NotSupportedException>(() => mutable.Add("Later", "ignored"));
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Projection_contracts_normalize_null_attributes_to_empty_read_only_maps(
        string contractName)
    {
        var contract = CreateContract(contractName, attributes: null);

        var attributes = GetAttributes(contract);
        attributes.ShouldNotBeNull();
        attributes.ShouldBeEmpty();

        var mutable = (IDictionary<string, string>)attributes;
        Should.Throw<NotSupportedException>(() => mutable.Add("Tenant", "north"));
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Projection_contracts_preserve_attribute_behavior_through_json_round_trips(
        string contractName)
    {
        var contract = CreateContract(
            contractName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tenant"] = "north"
            });

        var json = JsonSerializer.Serialize(contract, contract.GetType());
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("Attributes")
            .GetProperty("Tenant")
            .GetString()
            .ShouldBe("north");

        var roundTrip = JsonSerializer.Deserialize(json, contract.GetType());
        roundTrip.ShouldNotBeNull();
        var attributes = GetAttributes(roundTrip);
        attributes.Count.ShouldBe(1);
        attributes["Tenant"].ShouldBe("north");
        attributes.ContainsKey("tenant").ShouldBeFalse();

        var mutable = (IDictionary<string, string>)attributes;
        Should.Throw<NotSupportedException>(() => mutable["Tenant"] = "changed");
    }

    private static object CreateContract(
        string contractName,
        Dictionary<string, string>? attributes)
        => contractName switch
        {
            nameof(EventFilter) => new EventFilter
            {
                Attributes = attributes!
            },
            nameof(EventSummary) => new EventSummary
            {
                Timestamp = DateTimeOffset.Parse("2026-07-28T10:00:00Z"),
                Type = "order.created",
                Source = "orders",
                Attributes = attributes!
            },
            nameof(EventProjectionSnapshot) => new EventProjectionSnapshot
            {
                Timestamp = DateTimeOffset.Parse("2026-07-28T10:00:00Z"),
                ObservedCount = 1,
                MatchedCount = 1,
                CurrentRate = 0.1d,
                Attributes = attributes!
            },
            nameof(ProjectionEvent) => new ProjectionEvent
            {
                Timestamp = DateTimeOffset.Parse("2026-07-28T10:00:00Z"),
                Type = "order.created",
                Source = "orders",
                Attributes = attributes!
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contractName), contractName, null)
        };

    private static IReadOnlyDictionary<string, string> GetAttributes(object contract)
        => contract switch
        {
            EventFilter filter => filter.Attributes,
            EventSummary summary => summary.Attributes,
            EventProjectionSnapshot snapshot => snapshot.Attributes,
            ProjectionEvent projectionEvent => projectionEvent.Attributes,
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract.GetType(), null)
        };
}
