using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputDeliveryRegistrationTests
{
    [Fact]
    public void Options_preserve_exact_values_defaults_and_strict_renewal_boundaries()
    {
        var options = new DurableOutputDeliveryOptions(
            TimeSpan.FromSeconds(17),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(125),
            maxDeliveryAttempts: 7);

        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(17));
        options.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(5));
        options.RetryDelay.ShouldBe(TimeSpan.FromSeconds(3));
        options.IdleDelay.ShouldBe(TimeSpan.FromMilliseconds(125));
        options.MaxDeliveryAttempts.ShouldBe(7);
        DurableOutputDeliveryOptions.Default.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(30));
        DurableOutputDeliveryOptions.Default.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(10));
        DurableOutputDeliveryOptions.Default.RetryDelay.ShouldBe(TimeSpan.FromSeconds(1));
        DurableOutputDeliveryOptions.Default.IdleDelay.ShouldBe(TimeSpan.FromMilliseconds(250));
        DurableOutputDeliveryOptions.Default.MaxDeliveryAttempts.ShouldBeNull();
        typeof(DurableOutputDeliveryOptions).GetProperties()
            .Select(static property => property.Name)
            .ShouldBe(
                ["Default", "LeaseDuration", "LeaseRenewalInterval", "RetryDelay", "IdleDelay", "MaxDeliveryAttempts"],
                ignoreOrder: true);

        new DurableOutputDeliveryOptions(
            TimeSpan.FromTicks(2),
            TimeSpan.FromTicks(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            maxDeliveryAttempts: 1).MaxDeliveryAttempts.ShouldBe(1);
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryOptions(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            maxDeliveryAttempts: 0)).ParamName.ShouldBe("maxDeliveryAttempts");

        foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1) })
        {
            Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputDeliveryOptions(
                    invalid,
                    TimeSpan.FromTicks(1),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1)))
                .ParamName.ShouldBe("leaseDuration");
            Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputDeliveryOptions(
                    TimeSpan.FromSeconds(2),
                    invalid,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1)))
                .ParamName.ShouldBe("leaseRenewalInterval");
            Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputDeliveryOptions(
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1),
                    invalid,
                    TimeSpan.FromSeconds(1)))
                .ParamName.ShouldBe("retryDelay");
            Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputDeliveryOptions(
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1),
                    invalid))
                .ParamName.ShouldBe("idleDelay");
        }

        foreach (var invalid in new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) })
        {
            Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryOptions(
                TimeSpan.FromSeconds(2),
                invalid,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1))).ParamName.ShouldBe("leaseRenewalInterval");
        }
    }

    [Fact]
    public void Options_expose_only_the_new_five_argument_constructor()
    {
        var constructor = typeof(DurableOutputDeliveryOptions).GetConstructors().ShouldHaveSingleItem();

        constructor.GetParameters().Select(static parameter => (parameter.Name!, parameter.ParameterType))
            .ShouldBe(new[]
            {
                ("leaseDuration", typeof(TimeSpan)),
                ("leaseRenewalInterval", typeof(TimeSpan)),
                ("retryDelay", typeof(TimeSpan)),
                ("idleDelay", typeof(TimeSpan)),
                ("maxDeliveryAttempts", typeof(int?))
            });
        constructor.GetParameters()[4].HasDefaultValue.ShouldBeTrue();
        constructor.GetParameters()[4].DefaultValue.ShouldBeNull();
    }

    [Fact]
    public void Registration_returns_original_collection_invokes_once_and_snapshots_builder()
    {
        var services = new ServiceCollection();
        var calls = 0;
        DurableOutputDeliveryOptionsBuilder? captured = null;

        var returned = services.AddFluxFlowDurableOutputDelivery(builder =>
        {
            calls++;
            captured = builder;
            builder.LeaseDuration = TimeSpan.FromSeconds(17);
            builder.LeaseRenewalInterval = TimeSpan.FromSeconds(5);
            builder.RetryDelay = TimeSpan.FromSeconds(3);
            builder.IdleDelay = TimeSpan.FromMilliseconds(125);
            builder.MaxDeliveryAttempts = 5;
        });
        captured!.LeaseDuration = TimeSpan.FromDays(1);
        captured.LeaseRenewalInterval = TimeSpan.FromHours(12);
        captured.RetryDelay = TimeSpan.FromDays(1);
        captured.IdleDelay = TimeSpan.FromDays(1);
        captured.MaxDeliveryAttempts = 99;

        returned.ShouldBeSameAs(services);
        calls.ShouldBe(1);
        var options = services.Single(static descriptor =>
            descriptor.ServiceType == typeof(DurableOutputDeliveryOptions))
            .ImplementationInstance.ShouldBeOfType<DurableOutputDeliveryOptions>();
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(17));
        options.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(5));
        options.RetryDelay.ShouldBe(TimeSpan.FromSeconds(3));
        options.IdleDelay.ShouldBe(TimeSpan.FromMilliseconds(125));
        options.MaxDeliveryAttempts.ShouldBe(5);
        services.Count(static descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ShouldBe(1);
        services.Count(static descriptor => descriptor.ServiceType == typeof(TimeProvider))
            .ShouldBe(1);
    }

    [Fact]
    public void Equivalent_registration_is_idempotent_and_conflict_is_atomic()
    {
        var services = new ServiceCollection();
        services.AddSingleton<string>("unrelated");
        services.AddFluxFlowDurableOutputDelivery(builder =>
            builder.RetryDelay = TimeSpan.FromSeconds(2));
        var afterFirst = services.ToArray();

        var returned = services.AddFluxFlowDurableOutputDelivery(builder =>
            builder.RetryDelay = TimeSpan.FromSeconds(2));

        returned.ShouldBeSameAs(services);
        services.ShouldBe(afterFirst);
        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowDurableOutputDelivery(builder =>
                builder.RetryDelay = TimeSpan.FromSeconds(3)));
        exception.Message.ShouldContain("different options");
        services.ShouldBe(afterFirst);
        services.Single(static descriptor => descriptor.ServiceType == typeof(string))
            .ImplementationInstance.ShouldBe("unrelated");
    }

    [Fact]
    public void Registration_rejects_null_or_invalid_configuration_before_mutation()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            DurableOutputDeliveryServiceCollectionExtensions.AddFluxFlowDurableOutputDelivery(
                null!, static _ => { })).ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowDurableOutputDelivery(null!)).ParamName.ShouldBe("configure");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowDurableOutputDelivery(builder =>
                builder.LeaseDuration = TimeSpan.Zero)).ParamName.ShouldBe("leaseDuration");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowDurableOutputDelivery(builder =>
                builder.LeaseRenewalInterval = builder.LeaseDuration)).ParamName.ShouldBe("leaseRenewalInterval");
        services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 1, "requires one IDurableOutputDeliveryStore")]
    [InlineData(2, 1, "exactly one IDurableOutputDeliveryStore")]
    [InlineData(1, 0, "requires one IDurableOutputDeliveryHandler")]
    [InlineData(1, 2, "exactly one IDurableOutputDeliveryHandler")]
    public void Dispatcher_resolution_requires_exactly_one_store_and_handler(
        int storeCount,
        int handlerCount,
        string expectedMessage)
    {
        var stores = Enumerable.Range(0, storeCount)
            .Select(static _ => (IDurableOutputDeliveryStore)new NullDeliveryStore())
            .ToArray();
        var handlers = Enumerable.Range(0, handlerCount)
            .Select(static _ => (IDurableOutputDeliveryHandler)new NullDeliveryHandler())
            .ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            new DurableOutputDeliveryDispatcher(
                stores,
                handlers,
                DurableOutputDeliveryOptions.Default,
                new FakeTimeProvider(),
                NullLogger<DurableOutputDeliveryDispatcher>.Instance));

        exception.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Dispatcher_resolution_accepts_exactly_one_store_and_handler()
    {
        var store = new NullDeliveryStore();
        var handler = new NullDeliveryHandler();
        var dispatcher = new DurableOutputDeliveryDispatcher(
            [store],
            [handler],
            DurableOutputDeliveryOptions.Default,
            new FakeTimeProvider(),
            NullLogger<DurableOutputDeliveryDispatcher>.Instance);

        dispatcher.ShouldNotBeNull();
        typeof(DurableOutputDeliveryDispatcher)
            .GetFields(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .Select(static field => field.FieldType)
            .ShouldNotContain(typeof(IServiceProvider));
        typeof(DurableOutputDeliveryDispatcher)
            .GetConstructors()
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .ShouldNotContain(typeof(IServiceProvider));
    }

    private sealed class NullDeliveryStore : IDurableOutputDeliveryStore
    {
        public ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
            DurableOutputDeliveryLeaseRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DurableOutputDeliveryLease?>(null);

        public ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
            DurableOutputDeliveryTransition transition,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                transition.Key,
                DurableOutputDeliveryTransitionStatus.Applied));

        public ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
            DurableOutputDeliveryLeaseRenewal renewal,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                renewal.Key,
                DurableOutputDeliveryTransitionStatus.Applied));

        public ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
            DurableOutputDeliveryRetry retry,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                retry.Key,
                DurableOutputDeliveryTransitionStatus.Applied));

        public ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
            DurableOutputDeliveryDeadLetter deadLetter,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                deadLetter.Key,
                DurableOutputDeliveryTransitionStatus.Applied));
    }

    private sealed class NullDeliveryHandler : IDurableOutputDeliveryHandler
    {
        public ValueTask DeliverAsync(
            DurableOutputEnvelope envelope,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
