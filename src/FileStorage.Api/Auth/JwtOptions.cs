namespace FileStorage.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "file-storage-service";

    public string Audience { get; set; } = "file-storage-clients";

    public int AccessTokenMinutes { get; set; } = 60;

    public bool EnableMockTokenEndpoint { get; set; }
}
