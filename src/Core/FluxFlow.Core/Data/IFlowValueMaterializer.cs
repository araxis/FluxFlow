namespace FluxFlow.Data;

/// <summary>
/// Materializes a canonical runtime value into one module-owned input type.
/// Implementations belong to the module that owns <typeparamref name="T"/>.
/// Invalid input shapes are represented by a failed <see cref="FlowValueMaterializationResult{T}"/>
/// with a module-owned error code; they are not exceptions. Implementations should throw only
/// for programming errors such as a null argument and must not delegate to a global domain mapper.
/// </summary>
public interface IFlowValueMaterializer<T>
{
    FlowValueShape InputShape { get; }

    FlowValueMaterializationResult<T> Materialize(FlowValue value);
}

/// <summary>
/// The explicit success-or-error result returned by a component-owned value materializer.
/// </summary>
public sealed class FlowValueMaterializationResult<T>
{
    private readonly T? _value;

    private FlowValueMaterializationResult(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    private FlowValueMaterializationResult(FlowError error)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed materialization has no value.");

    public FlowError? Error { get; }

    public static FlowValueMaterializationResult<T> Success(T value) => new(value);

    public static FlowValueMaterializationResult<T> Failure(FlowError error) => new(error);
}
