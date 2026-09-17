using FileStorage.Domain.Enums;

namespace FileStorage.Api.Auth;

public sealed record DemoIdentity(string UserId, string DisplayName, string Role);

public static class DemoIdentities
{
    public static readonly DemoIdentity DemoUser = new(
        "11111111-1111-1111-1111-111111111111", "Demo User", UserRole.User);

    public static readonly DemoIdentity DemoAdmin = new(
        "22222222-2222-2222-2222-222222222222", "Demo Admin", UserRole.Admin);

    public static readonly IReadOnlyList<DemoIdentity> All = new[] { DemoUser, DemoAdmin };

    public static DemoIdentity? FindByRole(string role) =>
        All.FirstOrDefault(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase));
}
