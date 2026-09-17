namespace FileStorage.Application.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }

    bool IsAdmin { get; }

    string Role { get; }
}

public interface IClock
{
    DateTime UtcNow { get; }
}
