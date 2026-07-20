using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets.Contracts;
using FluxFlow.Composition.Addressing;
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
            .Add(
                SecretAddress("primary"),
                "runtime-value",
                ResourceOwnership.Host,
                kind: "profile")
            .BuildResolver();
        var address = SecretAddress("resolver");
        var services = new ServiceCollection()
            .AddExternalFluxFlowSecretResolver(address, resolver);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<ISecretResolver>(address.Value);
        var result = await resolved.ResolveAsync(new SecretReference
        {
            Name = Secret("primary"),
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
            .Add(
                SecretAddress("primary"),
                "runtime-value",
                ResourceOwnership.Host,
                kind: "profile")
            .BuildResolver();
        var address = SecretAddress("descriptors");
        var services = new ServiceCollection()
            .AddExternalFluxFlowSecretDescriptorProvider(address, descriptorProvider);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<ISecretDescriptorProvider>(address.Value);
        var descriptor = resolved.GetDescriptors().ShouldHaveSingleItem();

        resolved.ShouldBeSameAs(descriptorProvider);
        descriptor.Name.ShouldBe(Secret("primary"));
        descriptor.Kind.ShouldBe("profile");
    }

    [Fact]
    public void Service_registration_uses_canonical_nested_address_keys()
    {
        var resolver = new InMemorySecretResolverBuilder()
            .Add(SecretAddress("primary"), "runtime-value", ResourceOwnership.Host, kind: "profile")
            .BuildResolver();
        var descriptorProvider = new InMemorySecretResolverBuilder()
            .Add(SecretAddress("secondary"), "descriptor-value", ResourceOwnership.Host, kind: "profile")
            .BuildResolver();
        var resolverAddress = ApplicationAddress.Resource("Providers", "Secrets");
        var descriptorAddress = ApplicationAddress.Resource("Providers", "SecretDescriptors");
        var services = new ServiceCollection()
            .AddExternalFluxFlowSecretResolver(resolverAddress, resolver)
            .AddExternalFluxFlowSecretDescriptorProvider(descriptorAddress, descriptorProvider);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ISecretResolver>(resolverAddress.Value).ShouldBeSameAs(resolver);
        provider.GetRequiredKeyedService<ISecretDescriptorProvider>(descriptorAddress.Value)
            .ShouldBeSameAs(descriptorProvider);
    }

    [Fact]
    public async Task Service_registration_passes_provider_to_factories()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRegistrationDependency("primary"));
        var resolverAddress = SecretAddress("resolver");
        var providerAddress = SecretAddress("provider");
        services
            .AddFluxFlowSecretResolver(
                resolverAddress,
                provider => new InMemorySecretResolverBuilder()
                    .Add(
                        SecretAddress(provider.GetRequiredService<SecretRegistrationDependency>().Name),
                        "runtime-value",
                        ResourceOwnership.Host)
                    .BuildResolver())
            .AddFluxFlowSecretDescriptorProvider(
                providerAddress,
                provider => new InMemorySecretResolverBuilder()
                    .Add(
                        SecretAddress(provider.GetRequiredService<SecretRegistrationDependency>().Name),
                        "descriptor-value",
                        ResourceOwnership.Host)
                    .BuildResolver());

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredKeyedService<ISecretResolver>(resolverAddress.Value);
        var descriptors = provider.GetRequiredKeyedService<ISecretDescriptorProvider>(providerAddress.Value)
            .GetDescriptors();
        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = Secret("primary")
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Reveal().ShouldBe("runtime-value");
        descriptors.ShouldHaveSingleItem().Name.ShouldBe(Secret("primary"));
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var resolver = new InMemorySecretResolver([]);
        ISecretDescriptorProvider descriptorProvider = resolver;
        var address = SecretAddress("provider");
        var componentAddress = ApplicationAddress.WorkflowComponent("Workflow", "Component");

        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddFluxFlowSecretResolver(null!, address, _ => resolver))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddExternalFluxFlowSecretResolver(null!, address, resolver))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddExternalFluxFlowSecretResolver(address, null!))
            .ParamName.ShouldBe("resolver");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretResolver(address, null!))
            .ParamName.ShouldBe("resolverFactory");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSecretResolver(componentAddress, _ => resolver))
            .ParamName.ShouldBe("address");
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowSecretResolver(componentAddress, resolver))
            .ParamName.ShouldBe("address");

        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddFluxFlowSecretDescriptorProvider(
                null!,
                address,
                _ => descriptorProvider))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            SecretServiceCollectionExtensions.AddExternalFluxFlowSecretDescriptorProvider(
                null!,
                address,
                descriptorProvider))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddExternalFluxFlowSecretDescriptorProvider(address, null!))
            .ParamName.ShouldBe("descriptorProvider");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSecretDescriptorProvider(address, null!))
            .ParamName.ShouldBe("descriptorProviderFactory");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSecretDescriptorProvider(componentAddress, _ => descriptorProvider))
            .ParamName.ShouldBe("address");
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowSecretDescriptorProvider(componentAddress, descriptorProvider))
            .ParamName.ShouldBe("address");
    }

    [Fact]
    public void Service_registration_rejects_null_factory_results()
    {
        var resolverAddress = SecretAddress("resolver");
        var providerAddress = SecretAddress("provider");
        var services = new ServiceCollection()
            .AddFluxFlowSecretResolver(resolverAddress, _ => null!)
            .AddFluxFlowSecretDescriptorProvider(providerAddress, _ => null!);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISecretResolver>(resolverAddress.Value))
            .Message.ShouldContain("Secret resolver factory returned null.");
        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISecretDescriptorProvider>(providerAddress.Value))
            .Message.ShouldContain("Secret descriptor provider factory returned null.");
    }

    [Fact]
    public void Provider_created_resolver_is_owned_by_the_provider()
    {
        var address = SecretAddress("owned-resolver");
        DisposableSecretResolver? resolver = null;
        var services = new ServiceCollection()
            .AddFluxFlowSecretResolver(address, _ => resolver = new DisposableSecretResolver());

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredKeyedService<ISecretResolver>(address.Value)
                .ShouldBeSameAs(resolver);
        }

        resolver.ShouldNotBeNull().DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void External_resolver_registration_does_not_transfer_disposal_ownership()
    {
        var address = SecretAddress("external-resolver");
        var resolver = new DisposableSecretResolver();
        var services = new ServiceCollection()
            .AddExternalFluxFlowSecretResolver(address, resolver);

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredKeyedService<ISecretResolver>(address.Value)
                .ShouldBeSameAs(resolver);
        }

        resolver.DisposeCount.ShouldBe(0);
        resolver.Dispose();
        resolver.DisposeCount.ShouldBe(1);
    }

    private static ApplicationAddress SecretAddress(string name)
        => ApplicationAddress.Resource("Secrets", name.Trim());

    private static SecretName Secret(string name) => new(SecretAddress(name));

    private sealed record SecretRegistrationDependency(string Name);

    private sealed class DisposableSecretResolver : ISecretResolver, IDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask<SecretResolveResult> ResolveAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SecretResolveResult.Missing(reference));

        public void Dispose() => DisposeCount++;
    }
}
