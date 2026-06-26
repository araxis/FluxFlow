using FluxFlow.Components.Secrets.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Secrets.Tests;

public sealed class SecretServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Service_registration_registers_keyed_resolver()
    {
        var resolver = new InMemorySecretResolverBuilder()
            .Add("primary", "runtime-value", kind: "profile")
            .BuildResolver();
        var services = new ServiceCollection()
            .AddFluxFlowSecretResolver("secrets", resolver);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<ISecretResolver>("secrets");
        var result = await resolved.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary"),
            Kind = "profile"
        });

        resolved.ShouldBeSameAs(resolver);
        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Reveal().ShouldBe("runtime-value");
    }

    [Fact]
    public void Service_registration_registers_keyed_descriptor_provider()
    {
        var descriptorProvider = new InMemorySecretResolverBuilder()
            .Add("primary", "runtime-value", kind: "profile")
            .BuildResolver();
        var services = new ServiceCollection()
            .AddFluxFlowSecretDescriptorProvider("declared-secrets", descriptorProvider);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<ISecretDescriptorProvider>("declared-secrets");
        var descriptor = resolved.GetDescriptors().ShouldHaveSingleItem();

        resolved.ShouldBeSameAs(descriptorProvider);
        descriptor.Name.ShouldBe(new SecretName("primary"));
        descriptor.Kind.ShouldBe("profile");
    }

    [Fact]
    public async Task Service_registration_passes_provider_to_factories()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRegistrationDependency("primary"));
        services
            .AddFluxFlowSecretResolver(
                "resolver",
                provider => new InMemorySecretResolverBuilder()
                    .Add(provider.GetRequiredService<SecretRegistrationDependency>().Name, "runtime-value")
                    .BuildResolver())
            .AddFluxFlowSecretDescriptorProvider(
                "provider",
                provider => new InMemorySecretResolverBuilder()
                    .Add(provider.GetRequiredService<SecretRegistrationDependency>().Name, "descriptor-value")
                    .BuildResolver());

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredKeyedService<ISecretResolver>("resolver");
        var descriptors = provider.GetRequiredKeyedService<ISecretDescriptorProvider>("provider")
            .GetDescriptors();
        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary")
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Reveal().ShouldBe("runtime-value");
        descriptors.ShouldHaveSingleItem().Name.ShouldBe(new SecretName("primary"));
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var resolver = new InMemorySecretResolver([]);
        ISecretDescriptorProvider descriptorProvider = resolver;

        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddFluxFlowSecretResolver(null!, "secrets", resolver))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSecretResolver(" ", resolver))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretResolver("secrets", (ISecretResolver)null!))
            .ParamName.ShouldBe("resolver");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretResolver("secrets", (Func<IServiceProvider, ISecretResolver>)null!))
            .ParamName.ShouldBe("resolverFactory");

        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddFluxFlowSecretDescriptorProvider(
                null!,
                "secrets",
                descriptorProvider))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSecretDescriptorProvider(" ", descriptorProvider))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretDescriptorProvider(
                "secrets",
                (ISecretDescriptorProvider)null!))
            .ParamName.ShouldBe("descriptorProvider");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretDescriptorProvider(
                "secrets",
                (Func<IServiceProvider, ISecretDescriptorProvider>)null!))
            .ParamName.ShouldBe("descriptorProviderFactory");
    }

    [Fact]
    public void Service_registration_rejects_null_factory_results()
    {
        var services = new ServiceCollection()
            .AddFluxFlowSecretResolver("resolver", _ => null!)
            .AddFluxFlowSecretDescriptorProvider("provider", _ => null!);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISecretResolver>("resolver"))
            .Message.ShouldContain("Secret resolver factory returned null.");
        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISecretDescriptorProvider>("provider"))
            .Message.ShouldContain("Secret descriptor provider factory returned null.");
    }

    private sealed record SecretRegistrationDependency(string Name);
}
