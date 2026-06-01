using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Validation.Tests;

public sealed class ValidationComponentModuleTests
{
    [Fact]
    public void RegisterValidationComponents_AddsJsonSchemaValidatorFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterValidationComponents();

        registry.TryGetFactory(ValidationComponentTypes.JsonSchemaValidator, out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }
}
