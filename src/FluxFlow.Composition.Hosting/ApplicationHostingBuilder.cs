using FluxFlow.Composition.Revisions;
using FluxFlow.Composition.Hosting.Revisions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Composition.Hosting;

public sealed class ApplicationHostingBuilder
{
    internal ApplicationHostingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public ApplicationHostingBuilder UseCandidateFactory<TFactory>()
        where TFactory : class, IApplicationRevisionCandidateFactory
    {
        Services.TryAddSingleton<IApplicationRevisionCandidateFactory, TFactory>();
        return this;
    }

    public ApplicationHostingBuilder UseCandidateFactory(
        IApplicationRevisionCandidateFactory candidateFactory)
    {
        ArgumentNullException.ThrowIfNull(candidateFactory);
        Services.TryAddSingleton(candidateFactory);
        return this;
    }

    public ApplicationHostingBuilder UseRevisionEventSink<TSink>()
        where TSink : class, IApplicationRevisionEventSink
    {
        Services.TryAddSingleton<IApplicationRevisionEventSink, TSink>();
        return this;
    }

    public ApplicationHostingBuilder UseRevisionEventSink(
        IApplicationRevisionEventSink eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        Services.TryAddSingleton(eventSink);
        return this;
    }

    public ApplicationHostingBuilder Configure(
        Action<ApplicationRevisionHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }
}
