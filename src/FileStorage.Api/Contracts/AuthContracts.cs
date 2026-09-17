namespace FileStorage.Api.Contracts;

public sealed record MockTokenRequest(string Role);

public sealed record MockTokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string UserId,
    string Role,
    string DisplayName);

public sealed record DemoIdentityResponse(string UserId, string DisplayName, string Role);
