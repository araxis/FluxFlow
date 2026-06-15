using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Components.Validation.Timing;
using FluxFlow.Engine.Runtime;
using Json.Schema;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace FluxFlow.Components.Validation.Nodes;

internal static class JsonSchemaValidatorNodeFactory
{
    private static readonly MethodInfo CreateTypedMethod =
        typeof(JsonSchemaValidatorNodeFactory).GetMethod(
            nameof(CreateTyped),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find typed validator factory method.");

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        ValidationComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ValidationOptionsReader.ReadJsonSchemaValidatorOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var selector = componentOptions.ResolveValueSelector(
            inputType,
            options.EffectiveValueSelector);
        var nodeContext = new JsonSchemaValidatorContext
        {
            Address = context.Address,
            InputType = inputType,
            ValueSelector = options.EffectiveValueSelector
        };

        try
        {
            var method = CreateTypedMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(
                null,
                [context, options, selector, nodeContext, componentOptions.Clock])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateTyped<TInput>(
        RuntimeNodeFactoryContext context,
        JsonSchemaValidatorOptions options,
        ValidationComponentOptions.IValidationValueSelector selector,
        JsonSchemaValidatorContext nodeContext,
        IValidationClock clock)
    {
        // Read the schema text and compile it once at build time so the node
        // never performs File I/O or schema compilation inside its lifecycle.
        // Schema-missing / schema-load failures surface here as a node-build
        // failure rather than at StartAsync.
        var schema = LoadSchema(options);
        var metadata = new JsonSchemaValidatorMetadata
        {
            InputType = options.InputType,
            ValueSelector = options.EffectiveValueSelector,
            SchemaId = options.SchemaId,
            SchemaPath = options.SchemaPath
        };
        var node = new JsonSchemaValidatorNode<TInput>(
            schema,
            selector,
            nodeContext,
            metadata,
            clock,
            options.BoundedCapacity);

        return context.CreateNode(node)
            .Input(ValidationComponentPorts.Input, node.Input)
            .Output(ValidationComponentPorts.Result, node.Result)
            .Output(ValidationComponentPorts.Valid, node.Valid)
            .Output(ValidationComponentPorts.Invalid, node.Invalid)
            .Output(ValidationComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static JsonSchema LoadSchema(JsonSchemaValidatorOptions options)
    {
        if (IsMissingSchema(options))
        {
            throw new InvalidOperationException(
                "json.schema-validator failed to build: schema or schemaPath is required.");
        }

        try
        {
            var schemaText = ReadSchemaText(options);
            var baseUri = ResolveSchemaBaseUri(options);
            return baseUri is null
                ? JsonSchema.FromText(schemaText)
                : JsonSchema.FromText(schemaText, null, baseUri);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"json.schema-validator failed to build: could not load schema: {exception.Message}",
                exception);
        }
    }

    private static Uri? ResolveSchemaBaseUri(JsonSchemaValidatorOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SchemaId) &&
            Uri.TryCreate(options.SchemaId, UriKind.Absolute, out var schemaIdUri))
        {
            return schemaIdUri;
        }

        return string.IsNullOrWhiteSpace(options.SchemaPath)
            ? null
            : new Uri(Path.GetFullPath(options.SchemaPath));
    }

    private static string ReadSchemaText(JsonSchemaValidatorOptions options)
    {
        if (options.Schema.HasValue)
        {
            var schema = options.Schema.Value;
            return schema.ValueKind == JsonValueKind.String
                ? schema.GetString() ?? throw new InvalidOperationException("Schema text was empty.")
                : schema.GetRawText();
        }

        if (!string.IsNullOrWhiteSpace(options.SchemaPath))
        {
            return File.ReadAllText(options.SchemaPath);
        }

        throw new InvalidOperationException("Schema or schemaPath is required.");
    }

    private static bool IsMissingSchema(JsonSchemaValidatorOptions options)
        => !options.Schema.HasValue && string.IsNullOrWhiteSpace(options.SchemaPath);
}
