namespace FluxFlow.Components.Timers;

public static class TimerResultKinds
{
    public const string Debounced = "debounced";
    public const string DebounceFailed = "debounce-failed";
    public const string Delayed = "delayed";
    public const string DelayFailed = "delay-failed";
    public const string Throttled = "throttled";
    public const string ThrottleFailed = "throttle-failed";
}
