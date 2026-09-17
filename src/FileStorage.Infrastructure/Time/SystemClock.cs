using FileStorage.Application.Abstractions;

namespace FileStorage.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
