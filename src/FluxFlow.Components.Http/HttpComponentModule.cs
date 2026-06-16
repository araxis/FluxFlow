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
                HttpClientNodeFactory.Create),
            new FlowNodeRegistration(
                HttpComponentTypes.Request,
                context => HttpRequestNodeFactory.Create(context, clock))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
