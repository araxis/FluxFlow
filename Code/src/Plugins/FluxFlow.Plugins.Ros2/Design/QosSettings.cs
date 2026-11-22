namespace FluxFlow.Plugins.Ros2;

// The editor describes the wire settings without loading Rcl.NET. An absent profile and
// an explicitly supplied partial object have different defaults in the native library.
internal sealed class QosSettings
{
    public static QosSettings Default => new() { History = HistoryPolicy.KeepLast, Depth = 10,
        Reliability = ReliabilityPolicy.Reliable, Durability = DurabilityPolicy.Volatile };
    public HistoryPolicy History { get; set; }
    public int Depth { get; set; }
    public ReliabilityPolicy Reliability { get; set; }
    public DurabilityPolicy Durability { get; set; }
    public TimeSpan Deadline { get; set; }
    public TimeSpan LifeSpan { get; set; }
    public LivelinessPolicy Liveliness { get; set; }
    public TimeSpan LivelinessLeaseDuration { get; set; }
    public bool AvoidRosNamespaceConventions { get; set; }
}
internal enum HistoryPolicy { Default, KeepLast, KeepAll, Unknown }
internal enum ReliabilityPolicy { Default, Reliable, BestEffort, Unknown }
internal enum DurabilityPolicy { Default, TransientLocal, Volatile, Unknown }
internal enum LivelinessPolicy { Default, Automatic, ManualByNode, ManualByTopic, Unknown }
