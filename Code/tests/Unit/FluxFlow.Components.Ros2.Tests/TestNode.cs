using System.Reflection;
using Rcl;
using Rcl.Actions;
using Rcl.Graph;
using Rcl.Logging;
using Rcl.Parameters;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2.Tests;

// DispatchProxy does not preserve the native interface's generic method constraints.
internal sealed class TestNode(Func<MethodInfo, object?[]?, object?> call) : IRclNode
{
    public IRclPublisher<T> CreatePublisher<T>(string topicName, PublisherOptions? options = null) where T : IMessage
        => (IRclPublisher<T>)call(typeof(IRclNode).GetMethod(nameof(CreatePublisher))!, [topicName, options])!;
    public IRclSubscription<T> CreateSubscription<T>(string topicName, SubscriptionOptions? options = null) where T : IMessage
        => (IRclSubscription<T>)call(typeof(IRclNode).GetMethod(nameof(CreateSubscription))!, [topicName, options])!;
    public void Dispose() => call(typeof(IDisposable).GetMethod(nameof(Dispose))!, []);

    public IRclContext Context => throw new NotSupportedException();
    public IRclClock Clock => throw new NotSupportedException();
    public IParameterService Parameters => throw new NotSupportedException();
    public nuint DomaindId => throw new NotSupportedException();
    public string FullyQualifiedName => throw new NotSupportedException();
    public RosGraph Graph => throw new NotSupportedException();
    public ulong InstanceId => throw new NotSupportedException();
    public bool IsValid => throw new NotSupportedException();
    public IRclLogger Logger => throw new NotSupportedException();
    public string Name => throw new NotSupportedException();
    public string Namespace => throw new NotSupportedException();
    public IRclNativeSubscription CreateNativeSubscription<T>(string topicName, SubscriptionOptions? options = null) where T : IMessage
        => throw new NotSupportedException();
    public IRclNativeSubscription CreateNativeSubscription(string topicName, TypeSupportHandle typeSupport, SubscriptionOptions? options = null)
        => throw new NotSupportedException();
    public IRclService CreateService<TService, TRequest, TResponse>(string serviceName, IServiceHandler<TRequest, TResponse> handler, ServerOptions? options = null)
        where TService : IService<TRequest, TResponse> where TRequest : IServiceRequest where TResponse : IServiceResponse
        => throw new NotSupportedException();
    public IRclService CreateService<TService, TRequest, TResponse>(string serviceName, Func<TRequest, object?, TResponse> handler, object? state = null, ServerOptions? options = null)
        where TService : IService<TRequest, TResponse> where TRequest : IServiceRequest where TResponse : IServiceResponse
        => throw new NotSupportedException();
    public IRclService CreateConcurrentService<TService, TRequest, TResponse>(string serviceName, IConcurrentServiceHandler<TRequest, TResponse> handler, ServerOptions? options = null)
        where TService : IService<TRequest, TResponse> where TRequest : IServiceRequest where TResponse : IServiceResponse
        => throw new NotSupportedException();
    public IRclService CreateConcurrentService<TService, TRequest, TResponse>(string serviceName, Func<TRequest, object?, CancellationToken, Task<TResponse>> handler, object? state = null, ServerOptions? options = null)
        where TService : IService<TRequest, TResponse> where TRequest : IServiceRequest where TResponse : IServiceResponse
        => throw new NotSupportedException();
    public IRclService CreateNativeService<TService>(string serviceName, INativeServiceHandler handler, ServerOptions? options = null) where TService : IService
        => throw new NotSupportedException();
    public IRclService CreateNativeService<TService>(string serviceName, Action<RosMessageBuffer, RosMessageBuffer, object?> handler, object? state = null, ServerOptions? options = null) where TService : IService
        => throw new NotSupportedException();
    public IRclService CreateConcurrentNativeService<TService>(string serviceName, IConcurrentNativeServiceHandler handler, ServerOptions? options = null) where TService : IService
        => throw new NotSupportedException();
    public IRclService CreateConcurrentNativeService<TService>(string serviceName, Func<RosMessageBuffer, RosMessageBuffer, object?, CancellationToken, Task> handler, object? state = null, ServerOptions? options = null) where TService : IService
        => throw new NotSupportedException();
    public IRclClient<TRequest, TResponse> CreateClient<TService, TRequest, TResponse>(string serviceName, ClientOptions? options = null)
        where TService : IService<TRequest, TResponse> where TRequest : IServiceRequest where TResponse : IServiceResponse
        => throw new NotSupportedException();
    public IActionClient<TGoal, TResult, TFeedback> CreateActionClient<TAction, TGoal, TResult, TFeedback>(string actionName, ActionClientOptions? options = null)
        where TAction : IAction<TGoal, TResult, TFeedback> where TGoal : IActionGoal where TFeedback : IActionFeedback where TResult : IActionResult
        => throw new NotSupportedException();
    public IActionServer CreateActionServer<TAction>(string actionName, INativeActionGoalHandler handler, ActionServerOptions? options = null) where TAction : IAction
        => throw new NotSupportedException();
    public IActionServer CreateActionServer<TAction, TGoal, TResult, TFeedback>(string actionName, IActionGoalHandler<TGoal, TResult, TFeedback> handler, ActionServerOptions? options = null)
        where TAction : IAction<TGoal, TResult, TFeedback> where TGoal : IActionGoal where TFeedback : IActionFeedback where TResult : IActionResult
        => throw new NotSupportedException();
}
