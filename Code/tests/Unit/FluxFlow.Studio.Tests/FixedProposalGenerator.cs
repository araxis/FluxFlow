using FluxFlow.Application.Addressing;
using FluxFlow.Conversations;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

internal sealed class FixedProposalGenerator : IProposalGenerator
{
    public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result());

    public static ProposalResult Result() => new()
    {
        Edits =
        [
            new AddComponentEdit(Address("main.sample-source"), "source", new Dictionary<string, System.Text.Json.JsonElement>
            { ["Values"] = Json("\"[{\\\"Reading\\\":7},{\\\"Reading\\\":12}]\"") }),
            new AddComponentEdit(Address("main.sample-mapper"), "mapper", new Dictionary<string, System.Text.Json.JsonElement>
            { ["Engine"] = Json("\"jsonata\"") }),
            new ConfigureComponentEdit(Address("main.sample-mapper"), new Dictionary<string, System.Text.Json.JsonElement?>
            { ["Expression"] = Json("\"{\\\"Reading\\\": Reading * 2}\"") }),
            new ConnectPortsEdit(PortAddress.Parse("main.sample-source.Output"), PortAddress.Parse("main.sample-mapper.Input"))
        ]
    };
}
