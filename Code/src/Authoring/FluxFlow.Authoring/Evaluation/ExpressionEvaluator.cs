using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Authoring.Evaluation;

/// <summary>Evaluates an isolated sample using the host's existing expression registrations.</summary>
public sealed class ExpressionEvaluator(IServiceProvider services) : IExpressionEvaluator
{
    // Engine evaluation is synchronous. Cancellation does not interrupt a running expression.
    public Task<ExpressionEvaluationResult> EvaluateAsync(ExpressionEvaluationRequest request, CancellationToken cancellationToken = default)
        => Task.Run(() => Evaluate(request, cancellationToken), cancellationToken);

    private ExpressionEvaluationResult Evaluate(ExpressionEvaluationRequest request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var stage = "Input";
        string? engineName = request.Engine;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = JsonNode.Parse(request.Input);
            stage = "Headers";
            var headers = JsonNode.Parse(request.Headers) as JsonObject
                ?? throw new JsonException("Headers must be a JSON object.");
            stage = "Context";
            var context = JsonNode.Parse(request.Context) as JsonObject
                ?? throw new JsonException("Context must be a JSON object.");
            foreach (var property in context)
                if (!ExpressionEvaluationRequest.ContextProperties.Contains(property.Key, StringComparer.Ordinal))
                    throw new JsonException($"'{property.Key}' does not belong in the message metadata. Property names are fixed.");
            foreach (var name in ExpressionEvaluationRequest.ContextProperties)
                if (!context.ContainsKey(name)) throw new JsonException($"Context requires '{name}'. Only its value can be changed.");
            // Native envelope deserialization validates IDs, timestamps and header values.
            context["Value"] = request.IsErrorInput ? null : input;
            context["Headers"] = headers;
            context["IsError"] = request.IsErrorInput;
            context["Error"] = request.IsErrorInput ? input : null;
            var message = context.Deserialize(FlowJsonContext.Default.FlowMessage)!;
            var variables = FlowMapContext.ForMessage(message);
            stage = "Engine";
            if (request.PreviewProvider is { } previewProvider)
            {
                if (request.IsCondition || request.ResultKind is not (ExpressionResultKind.Value or ExpressionResultKind.Boolean) || string.IsNullOrEmpty(request.Engine))
                    throw new InvalidOperationException("Plugin previews require an explicit engine and a value or Boolean result.");
                var compile = services.GetKeyedService<PluginExpressionPreview>(previewProvider)
                    ?? throw new InvalidOperationException($"Preview provider '{previewProvider}' is not available in this evaluation host.");
                stage = "Source";
                var source = ReadSource(request.Expression, request.Engine);
                stage = "Compile";
                var evaluate = compile(source, request.PreviewSettings);
                stage = "Evaluate";
                var preview = evaluate(JsonSerializer.SerializeToElement(message, FlowJsonContext.Default.FlowMessage)).Clone();
                if (request.ResultKind == ExpressionResultKind.Boolean && preview.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidOperationException("The preview provider must return a Boolean result.");
                cancellationToken.ThrowIfCancellationRequested();
                return new() { Result = preview, Engine = engineName, ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds };
            }
            if (!request.IsCondition && string.IsNullOrEmpty(request.Engine))
                throw new InvalidOperationException("Component expressions require an explicit engine.");
            var engine = (string.IsNullOrEmpty(request.Engine)
                ? services.GetService<IFlowExpressionEngine>()
                : services.GetKeyedService<IFlowExpressionEngine>(request.Engine))
                ?? throw new InvalidOperationException(string.IsNullOrEmpty(request.Engine)
                    ? "No default expression engine is registered in this evaluation host."
                    : $"Expression engine '{request.Engine}' is not registered in this evaluation host.");
            engineName = engine.Name;
            stage = "Source";
            var expression = request.IsCondition ? request.Expression : ReadSource(request.Expression, engineName);
            stage = "Compile";
            JsonElement result;
            if (request.IsCondition || request.ResultKind == ExpressionResultKind.Boolean)
            {
                var evaluate = engine.Compile<bool>(expression);
                stage = "Evaluate";
                result = JsonSerializer.SerializeToElement(evaluate(variables), FlowJsonContext.Default.Boolean);
            }
            else if (request.ResultKind == ExpressionResultKind.String)
            {
                var evaluate = engine.Compile<string>(expression);
                stage = "Evaluate";
                result = JsonSerializer.SerializeToElement(evaluate(variables), FlowJsonContext.Default.String);
            }
            else if (request.ResultKind == ExpressionResultKind.Value)
            {
                var evaluate = engine.Compile<FlowValue>(expression);
                stage = "Evaluate";
                result = evaluate(variables).Json.Clone();
            }
            else throw new ArgumentOutOfRangeException(nameof(request.ResultKind));
            cancellationToken.ThrowIfCancellationRequested();
            return new() { Result = result, Engine = engineName, ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new() { Engine = engineName, Stage = stage, Error = error.Message,
                Details = error.ToString(), ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds };
        }
    }

    private static string ReadSource(string value, string engine)
    {
        var source = new ExpressionSource(value);
        var text = source.Value.Trim();
        // Admit only the native reader's text branches; never open a host file from an editor request.
        if (ExpressionSource.IsFileReference(text) || text.StartsWith(@"\\", StringComparison.Ordinal)
            || (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '/' or '\\'))
            throw new NotSupportedException("File evaluation is deferred. Use inline text or an explicit base64: source for this sample.");
        return source.Read(AppContext.BaseDirectory, engine);
    }
}
