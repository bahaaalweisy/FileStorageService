namespace FileStorage.Application.Options;

public sealed class ResumableUploadOptions
{
    public const string SectionName = "ResumableUpload";

    public long MaxChunkSizeBytes { get; set; } = 8L * 1024 * 1024;

    public int SessionTimeToLiveHours { get; set; } = 24;
}
