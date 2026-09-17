namespace FileStorage.Application.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }

    bool IsAdmin { get; }
}

public interface IClock
{
    DateTime UtcNow { get; }
}
