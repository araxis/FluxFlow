namespace FluxFlow.Components.State.Options;

public sealed record StateReducerOptions<T>
{
    private string? _keyExpression;
    private string _reducer = string.Empty;
    private string? _expressionId;
    private string? _expressionName;
    private T? _initialState;
    private bool _hasInitialState;
    private int _boundedCapacity = 128;
    private int _maxKeys = 1024;

    public string? KeyExpression
    {
        get => _keyExpression;
        init => _keyExpression = StateOptionValidation.ValidateKeyExpression(value);
    }

    public required string Reducer
    {
        get => _reducer;
        init => _reducer = StateOptionValidation.ValidateReducer(value);
    }

    public string? ExpressionId
    {
        get => _expressionId;
        init => _expressionId = StateOptionValidation.NormalizeOptional(value);
    }

    public string? ExpressionName
    {
        get => _expressionName;
        init => _expressionName = StateOptionValidation.NormalizeOptional(value);
    }

    public T? InitialState
    {
        get => _initialState;
        init
        {
            _initialState = value;
            _hasInitialState = true;
        }
    }

    internal bool HasInitialState => _hasInitialState;

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = StateOptionValidation.ValidateBoundedCapacity(value);
    }

    public int MaxKeys
    {
        get => _maxKeys;
        init => _maxKeys = StateOptionValidation.ValidateMaxKeys(value);
    }
}
