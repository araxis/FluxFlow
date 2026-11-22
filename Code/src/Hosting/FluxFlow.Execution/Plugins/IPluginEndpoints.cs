using Microsoft.AspNetCore.Routing;

namespace FluxFlow.Execution.Plugins;

public interface IPluginEndpoints
{
    void Map(IEndpointRouteBuilder routes);
}
