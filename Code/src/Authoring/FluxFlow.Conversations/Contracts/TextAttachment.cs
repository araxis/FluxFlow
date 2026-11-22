using System.Text;

namespace FluxFlow.Conversations;

/// <summary>Sample text captured with a message, without a file path or host handle.</summary>
public sealed record TextAttachment(string Name, string Content)
{
    public const int MaxCount = 4;
    public const int MaxFileBytes = 32 * 1024;
    public const int MaxTotalBytes = 64 * 1024;

    public static void Validate(IReadOnlyList<TextAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count > MaxCount)
            throw new ArgumentException("Attach up to four JSON or text files.");
        var total = 0;
        foreach (var file in attachments)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 255 ||
                file.Name.IndexOfAny(['/', '\\']) >= 0 ||
                !(file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Choose a .json or .txt file with a file name, not a path.");
            if (file.Content is null || file.Content.Contains('\0'))
                throw new ArgumentException("Attachments must contain text.");
            var size = Encoding.UTF8.GetByteCount(file.Content);
            if (size > MaxFileBytes)
                throw new ArgumentException("Each attachment must be 32 KB or smaller.");
            total += size;
        }
        if (total > MaxTotalBytes)
            throw new ArgumentException("Attachments must total 64 KB or less.");
    }
}
