using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Sessions.Nodes;

internal static class SessionNodeFactory
{
    public static RuntimeNode CreateRecorder(
        RuntimeNodeFactoryContext context,
        SessionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SessionsOptionsReader.ReadRecorderOptions(context.Definition);
        var store = componentOptions.StoreFactory.Create(new SessionStoreContext
        {
            Address = context.Address,
            NodeType = SessionsComponentTypes.Recorder,
            StoreName = Normalize(options.Store),
            SessionId = Normalize(options.SessionId)
        }) ?? throw new InvalidOperationException("session.recorder store factory returned null.");
        var node = new SessionRecorderNode(options, store, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(SessionsComponentPorts.Input, node.Input)
            .Output(SessionsComponentPorts.Output, node.Output)
            .Output(SessionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateReplay(
        RuntimeNodeFactoryContext context,
        SessionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SessionsOptionsReader.ReadReplayOptions(context.Definition);
        var store = componentOptions.StoreFactory.Create(new SessionStoreContext
        {
            Address = context.Address,
            NodeType = SessionsComponentTypes.Replay,
            StoreName = Normalize(options.Store),
            SessionId = Normalize(options.SessionId)
        }) ?? throw new InvalidOperationException("session.replay store factory returned null.");
        var node = new SessionReplayNode(options, store, componentOptions.Clock);

        return context.CreateNode(node)
            .Output(SessionsComponentPorts.Output, node.Output)
            .Output(SessionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateQuery(
        RuntimeNodeFactoryContext context,
        SessionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SessionsOptionsReader.ReadQueryOptions(context.Definition);
        var store = componentOptions.StoreFactory.Create(new SessionStoreContext
        {
            Address = context.Address,
            NodeType = SessionsComponentTypes.Query,
            StoreName = Normalize(options.Store)
        }) ?? throw new InvalidOperationException("session.query store factory returned null.");
        var node = new SessionQueryNode(options, store, componentOptions);

        return context.CreateNode(node)
            .Input(SessionsComponentPorts.Input, node.Input)
            .Output(SessionsComponentPorts.Output, node.Output)
            .Output(SessionsComponentPorts.Sessions, node.Sessions)
            .Output(SessionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
