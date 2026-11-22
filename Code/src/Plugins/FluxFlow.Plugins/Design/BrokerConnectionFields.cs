using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

public static class BrokerConnectionFields
{
    public static OptionEditorModel Endpoint(string defaultAddress, string help, Action<string> validate) =>
        OptionEditorModel.Create("Endpoint", "Endpoint", help: help, defaultValue: defaultAddress) with
        {
            ValidateText = address =>
            {
                if (string.IsNullOrEmpty(address)) return null; // Clearing restores the runtime default.
                try { validate(address); return null; }
                catch (ArgumentException error) { return error.Message; }
            }
        };

    public static OptionEditorModel Variable(string key, string label, string help) =>
        OptionEditorModel.Create(key, label, help: help) with
        {
            ValidateText = name => !string.IsNullOrEmpty(name) && (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['=', '\0']) >= 0)
                ? "Enter a host environment-variable name." : null
        };

}
