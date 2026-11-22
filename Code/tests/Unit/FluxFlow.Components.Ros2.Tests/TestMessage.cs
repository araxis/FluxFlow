using Rosidl.Runtime;
namespace FluxFlow.Components.Ros2.Tests;
public sealed class TestMessage : IMessage
{
    public string Data { get; set; } = "";
    public int SequenceNumber { get; set; }
    public static string TypeSupportName => "test_msgs/msg/Test";
    public static TypeSupportHandle GetTypeSupportHandle() => throw new NotSupportedException();
    public static IMessage CreateFrom(nint data, System.Text.Encoding encoding) => throw new NotSupportedException();
    public static nint UnsafeCreate() => throw new NotSupportedException();
    public static void UnsafeDestroy(nint data) => throw new NotSupportedException();
    public static bool UnsafeInitialize(nint data) => throw new NotSupportedException();
    public static void UnsafeFinalize(nint data) => throw new NotSupportedException();
    public static bool UnsafeInitializeSequence(int size, nint data) => throw new NotSupportedException();
    public static void UnsafeFinalizeSequence(nint data) => throw new NotSupportedException();
    public void WriteTo(nint data, System.Text.Encoding encoding) => throw new NotSupportedException();
}
