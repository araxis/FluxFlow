using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http;

public sealed class HttpComponentModule : IFlowNodeModule
{
    public HttpComponentModule(HttpComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock;
        Registrations =
        [
            new FlowNodeRegistration(
                HttpComponentTypes.Client,
                context => HttpClientNodeFactory.Create(context, options, clock))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
