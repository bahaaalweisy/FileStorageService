namespace FileStorage.Application.Options;

public sealed class UploadPolicyOptions
{
    public const string SectionName = "UploadPolicy";

    public long MaxFileSizeBytes { get; set; } = 200L * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".csv", ".json",
        ".zip", ".docx", ".xlsx", ".pptx", ".mp4", ".mp3", ".bin"
    };

    public string[] PreviewAllowedContentTypes { get; set; } =
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "application/pdf"
    };

    public int MaxOriginalFilenameLength { get; set; } = 255;

    public int MaxTagCount { get; set; } = 20;

    public int MaxTagLength { get; set; } = 64;

    public int MaxTagsFieldLength { get; set; } = 2048;

    public int MultipartHeaderCountLimit { get; set; } = 32;

    public int MultipartHeaderLengthLimit { get; set; } = 16 * 1024;

    public int CopyBufferSizeBytes { get; set; } = 81920;
}
