namespace FluxFlow.Data;

public sealed record FlowError
{
    public FlowError(
        string code,
        string message,
        string category,
        bool isTransient = false,
        FlowValue? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Code = code.Trim();
        Message = message.Trim();
        Category = category.Trim();
        IsTransient = isTransient;
        Details = details ?? FlowValue.FromObject([]);
    }

    public string Code { get; }

    public string Message { get; }

    public string Category { get; }

    public bool IsTransient { get; }

    public FlowValue Details { get; }
}
