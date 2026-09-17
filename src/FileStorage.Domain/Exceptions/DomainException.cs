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

public sealed class UploadSessionNotUsableException : DomainException
{
    public Guid SessionId { get; }
    public string Status { get; }

    public UploadSessionNotUsableException(Guid sessionId, string status)
        : base($"Upload session '{sessionId}' is not usable (status: {status}).")
    {
        SessionId = sessionId;
        Status = status;
    }
}
