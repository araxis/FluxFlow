using FluxFlow.Engine.DurableInput;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputRegistrationTests
{
    [Fact]
    public void Ordinary_engine_registration_does_not_enable_durable_input()
    {
        var services = new ServiceCollection();

        services.AddFluxFlow(new ApplicationDefinition());

        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(DurableApplicationInputs));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(DurableInputDispatcher));
    }

    [Fact]
    public void Default_options_are_small_bounded_and_deterministic()
    {
        DurableInputOptions.Default.ShouldBe(new DurableInputOptions(
            batchSize: 64,
            leaseDuration: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(2),
            maxDeliveryAttempts: 10));
    }

    [Fact]
    public void Registration_builds_immutable_overrides_and_one_dispatcher()
    {
        var services = new ServiceCollection();
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        DurableInputOptionsBuilder? capturedBuilder = null;
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IDurableInputStore, DurableInputTestStore>();
        services.AddFluxFlowDurableInput(options =>
        {
            capturedBuilder = options;
            options.BatchSize = 7;
            options.LeaseDuration = TimeSpan.FromMinutes(2);
            options.PollInterval = TimeSpan.FromSeconds(3);
            options.RetryDelay = TimeSpan.FromSeconds(4);
            options.StoreFailureDelay = TimeSpan.FromSeconds(5);
            options.MaxDeliveryAttempts = 6;
            options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
            options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(9);
            options.LeaseRenewalInterval = TimeSpan.FromSeconds(15);
        });
        capturedBuilder!.BatchSize = 99;
        capturedBuilder.WorkflowCompletionTimeout = TimeSpan.FromHours(1);
        services.AddFluxFlowDurableInputContract<string>("text-v1");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DurableInputOptions>().ShouldBe(new DurableInputOptions(
            7,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(5),
            6,
            DurableInputAcknowledgementMode.WorkflowCompleted,
            TimeSpan.FromMinutes(9),
            TimeSpan.FromSeconds(15)));
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(clock);
        provider.GetRequiredService<DurableApplicationInputs>().ShouldNotBeNull();
        services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationFactory is not null)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Equivalent_registration_is_idempotent_and_conflicting_registration_is_rejected()
    {
        var services = new ServiceCollection();
        var typeInfo = (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string));

        services.AddFluxFlowDurableInput();
        services.AddFluxFlowDurableInput();
        services.AddFluxFlowDurableInputContract("text-v1", typeInfo);
        services.AddFluxFlowDurableInputContract("text-v1", typeInfo);

        services.Count(descriptor => descriptor.ServiceType == typeof(DurableInputOptions))
            .ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDurableInputContract))
            .ShouldBe(1);
        Should.Throw<InvalidOperationException>(() =>
                services.AddFluxFlowDurableInput(options => options.BatchSize = 65))
            .Message.ShouldContain("different options");
        Should.Throw<InvalidOperationException>(() =>
                services.AddFluxFlowDurableInputContract<int>("text-v1"))
            .Message.ShouldContain("conflicts");
        Should.Throw<InvalidOperationException>(() =>
                services.AddFluxFlowDurableInputContract<string>("text-v2"))
            .Message.ShouldContain("conflicts");
    }

    [Fact]
    public void Equivalent_workflow_completion_registration_is_idempotent_and_changed_timing_conflicts()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowDurableInput(ConfigureWorkflowCompletion);
        var descriptorCount = services.Count;

        services.AddFluxFlowDurableInput(ConfigureWorkflowCompletion);
        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowDurableInput(options =>
            {
                ConfigureWorkflowCompletion(options);
                options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(11);
            }));

        services.Count.ShouldBe(descriptorCount);
        exception.Message.ShouldContain("different options");

        static void ConfigureWorkflowCompletion(DurableInputOptionsBuilder options)
        {
            options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
            options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
            options.LeaseRenewalInterval = TimeSpan.FromSeconds(5);
        }
    }

    [Fact]
    public void Resolving_the_client_requires_an_explicit_store_provider()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowDurableInput();
        services.AddFluxFlowDurableInputContract<string>("text-v1");
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<DurableApplicationInputs>());

        exception.Message.ShouldBe(
            "AddFluxFlowDurableInput requires one IDurableInputStore registration.");
    }

    [Fact]
    public async Task Multiple_store_registrations_are_rejected_by_client_and_dispatcher()
    {
        var services = DispatcherServices(DurableInputAcknowledgementMode.EngineAccepted);
        services.AddSingleton<IDurableInputStore>(new DurableInputTestStore());
        await using var provider = services.BuildServiceProvider();

        var clientException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<DurableApplicationInputs>());
        var dispatcherException = Should.Throw<InvalidOperationException>(() =>
            provider.GetServices<IHostedService>().ToArray());

        const string expected =
            "AddFluxFlowDurableInput supports exactly one IDurableInputStore registration.";
        clientException.Message.ShouldBe(expected);
        dispatcherException.Message.ShouldBe(expected);
    }

    [Theory]
    [InlineData(nameof(DurableInputOptionsBuilder.BatchSize))]
    [InlineData(nameof(DurableInputOptionsBuilder.LeaseDuration))]
    [InlineData(nameof(DurableInputOptionsBuilder.PollInterval))]
    [InlineData(nameof(DurableInputOptionsBuilder.RetryDelay))]
    [InlineData(nameof(DurableInputOptionsBuilder.StoreFailureDelay))]
    [InlineData(nameof(DurableInputOptionsBuilder.MaxDeliveryAttempts))]
    public void Non_positive_options_are_rejected_during_registration(string option)
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowDurableInput(builder => SetNonPositive(builder, option)));

        exception.ParamName.ShouldBe(option);
    }

    [Fact]
    public void Completion_options_are_validated_before_registration_mutates_services()
    {
        var undefinedServices = new ServiceCollection();
        var timeoutServices = new ServiceCollection();
        var renewalServices = new ServiceCollection();

        Should.Throw<ArgumentOutOfRangeException>(() =>
                undefinedServices.AddFluxFlowDurableInput(options =>
                    options.AcknowledgementMode = (DurableInputAcknowledgementMode)42))
            .ParamName.ShouldBe("acknowledgementMode");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                timeoutServices.AddFluxFlowDurableInput(options =>
                    options.WorkflowCompletionTimeout = TimeSpan.Zero))
            .ParamName.ShouldBe("workflowCompletionTimeout");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                renewalServices.AddFluxFlowDurableInput(options =>
                {
                    options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
                    options.LeaseRenewalInterval = options.LeaseDuration;
                }))
            .ParamName.ShouldBe("leaseRenewalInterval");

        undefinedServices.ShouldBeEmpty();
        timeoutServices.ShouldBeEmpty();
        renewalServices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_dispatcher_composition_requires_no_optional_completion_capability()
    {
        var services = DispatcherServices(DurableInputAcknowledgementMode.EngineAccepted);
        services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        await using var provider = services.BuildServiceProvider();

        var dispatcher = ActivatorUtilities.CreateInstance<DurableInputDispatcher>(provider);

        dispatcher.ShouldNotBeNull();
    }

    [Fact]
    public async Task Workflow_dispatcher_composes_with_exactly_one_of_each_optional_capability()
    {
        var services = DispatcherServices(DurableInputAcknowledgementMode.WorkflowCompleted);
        services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        await using var provider = services.BuildServiceProvider();

        var dispatcher = ActivatorUtilities.CreateInstance<DurableInputDispatcher>(provider);

        dispatcher.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(MissingCapability.CompletionSource, "IDurableInputCompletionSource")]
    [InlineData(MissingCapability.RenewalStore, "IDurableInputLeaseRenewalStore")]
    public async Task Workflow_completion_requires_each_optional_capability_exactly_once(
        MissingCapability missing,
        string expectedCapability)
    {
        var services = DispatcherServices(DurableInputAcknowledgementMode.WorkflowCompleted);
        if (missing != MissingCapability.CompletionSource)
            services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        if (missing != MissingCapability.RenewalStore)
            services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        await using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<DurableInputDispatcher>(provider));

        exception.Message.ShouldContain("exactly one");
        exception.Message.ShouldContain(expectedCapability);
        exception.Message.ShouldContain("none");
    }

    [Theory]
    [InlineData(MissingCapability.CompletionSource, "IDurableInputCompletionSource")]
    [InlineData(MissingCapability.RenewalStore, "IDurableInputLeaseRenewalStore")]
    public async Task Workflow_completion_rejects_multiple_optional_capabilities(
        MissingCapability duplicate,
        string expectedCapability)
    {
        var services = DispatcherServices(DurableInputAcknowledgementMode.WorkflowCompleted);
        services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        if (duplicate == MissingCapability.CompletionSource)
            services.AddSingleton<IDurableInputCompletionSource>(new DurableInputCompletionTestSource());
        else
            services.AddSingleton<IDurableInputLeaseRenewalStore>(new DurableInputTestStore());
        await using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<DurableInputDispatcher>(provider));

        exception.Message.ShouldContain("exactly one");
        exception.Message.ShouldContain(expectedCapability);
        exception.Message.ShouldContain("multiple");
    }

    private static ServiceCollection DispatcherServices(
        DurableInputAcknowledgementMode acknowledgementMode)
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition());
        services.AddSingleton<IDurableInputStore>(new DurableInputTestStore());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DurableInputTestData.Now));
        services.AddSingleton<ILogger<DurableInputDispatcher>>(
            NullLogger<DurableInputDispatcher>.Instance);
        services.AddFluxFlowDurableInput(options =>
        {
            options.AcknowledgementMode = acknowledgementMode;
            options.LeaseRenewalInterval = TimeSpan.FromSeconds(5);
        });
        return services;
    }

    private static void SetNonPositive(DurableInputOptionsBuilder builder, string option)
    {
        switch (option)
        {
            case nameof(DurableInputOptionsBuilder.BatchSize):
                builder.BatchSize = 0;
                break;
            case nameof(DurableInputOptionsBuilder.LeaseDuration):
                builder.LeaseDuration = TimeSpan.Zero;
                break;
            case nameof(DurableInputOptionsBuilder.PollInterval):
                builder.PollInterval = TimeSpan.Zero;
                break;
            case nameof(DurableInputOptionsBuilder.RetryDelay):
                builder.RetryDelay = TimeSpan.Zero;
                break;
            case nameof(DurableInputOptionsBuilder.StoreFailureDelay):
                builder.StoreFailureDelay = TimeSpan.Zero;
                break;
            case nameof(DurableInputOptionsBuilder.MaxDeliveryAttempts):
                builder.MaxDeliveryAttempts = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(option));
        }
    }

    public enum MissingCapability
    {
        CompletionSource,
        RenewalStore
    }
}
