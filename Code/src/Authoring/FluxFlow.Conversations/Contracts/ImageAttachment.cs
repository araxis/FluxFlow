namespace FluxFlow.Conversations;

/// <summary>Captured image content, independent of the file picker and its host.</summary>
public sealed record ImageAttachment(string Name, string MediaType, string Base64)
{
    public const int MaxCount = 2;
    public const int MaxFileBytes = 2 * 1024 * 1024;

    public static void Validate(IReadOnlyList<ImageAttachment> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count > MaxCount) throw new ArgumentException("Attach at most two images per message.");
        foreach (var image in images)
        {
            if (image is null || string.IsNullOrWhiteSpace(image.Name) || image.Name.Length > 255 ||
                image.Name.IndexOfAny(['/', '\\']) >= 0)
                throw new ArgumentException("Each image needs a file name without a path.");
            var extension = Path.GetExtension(image.Name);
            var png = image.MediaType == "image/png" && extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
            var jpeg = image.MediaType == "image/jpeg" &&
                (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
            if (!png && !jpeg) throw new ArgumentException("Attach PNG or JPEG images only.");
            if (string.IsNullOrEmpty(image.Base64) || image.Base64.Length > ((MaxFileBytes + 2) / 3) * 4)
                throw new ArgumentException("Images must be nonempty and 2 MB or smaller.");
            byte[] data;
            try { data = Convert.FromBase64String(image.Base64); }
            catch (FormatException) { throw new ArgumentException("The image content is not valid base64."); }
            if (data.Length > MaxFileBytes) throw new ArgumentException("Images must be 2 MB or smaller.");
            if (png ? !data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    : !data.AsSpan().StartsWith(new byte[] { 255, 216, 255 }))
                throw new ArgumentException("The image content does not match its PNG or JPEG format.");
        }
    }
}
