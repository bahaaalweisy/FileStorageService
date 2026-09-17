namespace FileStorage.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}

public sealed class ObjectAlreadyDeletedException : DomainException
{
    public ObjectAlreadyDeletedException(Guid id) : base($"Stored object '{id}' has already been soft-deleted.")
    {
    }
}
