using System.Security.Claims;
using FileStorage.Application.Abstractions;
using FileStorage.Domain.Enums;

namespace FileStorage.Api.Auth;

public sealed class CurrentUser : ICurrentUser
{
    public string UserId { get; }

    public bool IsAdmin { get; }

    public string Role { get; }

    public CurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        UserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub")
            ?? string.Empty;
        IsAdmin = principal?.IsInRole(UserRole.Admin) ?? false;
        Role = principal?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }
}
