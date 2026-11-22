using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Logging;
using FluxFlow.Plugins.Sources;
using FluxFlow.Plugins.Transformation;

namespace FluxFlow.Environments.Tests;

internal static class RunTestPlugins
{
    public static InstalledPlugins WithBasicComponents(params IFluxFlowPlugin[] others) =>
        new([new SourcesPlugin(), new LoggingPlugin(), new TransformationPlugin(), new FluxFlow.Plugins.FlowControl.FlowControlPlugin(), .. others]);
}
