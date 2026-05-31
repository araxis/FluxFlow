using FluxFlow.Engine.Scenarios;
using System.Text.Json;

namespace FluxFlow.Engine.Definitions;

internal static class ScenarioStepDefinitionValidator
{
    public static void Validate(
        string scenarioName,
        string stepName,
        ScenarioStepDefinition step,
        ApplicationDefinition definition,
        List<ApplicationDefinitionValidationError> errors)
    {
        _ = definition;

        switch (step.Type)
        {
            case ScenarioStepTypes.ExpectEvent:
                ValidateExpectEventStep(scenarioName, stepName, step.Configuration, errors);
                break;
        }
    }

    private static void ValidateExpectEventStep(
        string scenarioName,
        string stepName,
        IReadOnlyDictionary<string, JsonElement> configuration,
        List<ApplicationDefinitionValidationError> errors)
    {
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.EventType, errors);
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.TopicStartsWith, errors);
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.SubjectStartsWith, errors);
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.Status, errors);
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.Source, errors);
        ValidateOptionalString(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.PayloadContains, errors);
        ValidateOptionalInt(scenarioName, stepName, configuration, ScenarioStepConfigurationKeys.TimeoutMs, 1, int.MaxValue, errors);
        ValidateAttributes(scenarioName, stepName, configuration, errors);
    }

    private static void ValidateOptionalString(
        string scenarioName,
        string stepName,
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key,
        List<ApplicationDefinitionValidationError> errors)
        => TryReadOptionalString(scenarioName, stepName, configuration, key, errors, out _);

    private static bool TryReadOptionalString(
        string scenarioName,
        string stepName,
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key,
        List<ApplicationDefinitionValidationError> errors,
        out string? value)
    {
        value = null;
        if (!configuration.TryGetValue(key, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(InvalidScenarioConfiguration(scenarioName, stepName, key, "must be a string."));
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static void ValidateOptionalInt(
        string scenarioName,
        string stepName,
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key,
        int minValue,
        int maxValue,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (!configuration.TryGetValue(key, out var element))
        {
            return;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value < minValue ||
            value > maxValue)
        {
            var range = maxValue == int.MaxValue
                ? $"greater than or equal to {minValue}"
                : $"between {minValue} and {maxValue}";
            errors.Add(InvalidScenarioConfiguration(scenarioName, stepName, key, $"must be an integer {range}."));
        }
    }

    private static void ValidateAttributes(
        string scenarioName,
        string stepName,
        IReadOnlyDictionary<string, JsonElement> configuration,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (!configuration.TryGetValue(ScenarioStepConfigurationKeys.Attributes, out var attributes) ||
            attributes.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (attributes.ValueKind != JsonValueKind.Object)
        {
            errors.Add(InvalidScenarioConfiguration(
                scenarioName,
                stepName,
                ScenarioStepConfigurationKeys.Attributes,
                "must be an object."));
            return;
        }

        foreach (var attribute in attributes.EnumerateObject())
        {
            if (attribute.Value.ValueKind is JsonValueKind.String or
                JsonValueKind.True or
                JsonValueKind.False or
                JsonValueKind.Number)
            {
                continue;
            }

            errors.Add(InvalidScenarioConfiguration(
                scenarioName,
                stepName,
                $"{ScenarioStepConfigurationKeys.Attributes}.{attribute.Name}",
                "must be a string, boolean, or number."));
        }
    }

    private static ApplicationDefinitionValidationError InvalidScenarioConfiguration(
        string scenarioName,
        string stepName,
        string key,
        string rule)
        => new(
            ApplicationDefinitionValidationErrorCode.InvalidScenarioStepConfiguration,
            $"Test scenario '{scenarioName}' step '{stepName}' configuration value '{key}' {rule}");
}
