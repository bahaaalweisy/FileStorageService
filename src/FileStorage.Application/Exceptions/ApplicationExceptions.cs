namespace FileStorage.Application.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }
}

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(string resource, object key) : base($"{resource} '{key}' was not found.")
    {
    }
}

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(string message) : base(message)
    {
    }
}

public sealed class ValidationAppException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationAppException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = new[] { message } })
    {
    }
}

public sealed class ConflictAppException : AppException
{
    public ConflictAppException(string message) : base(message)
    {
    }
}

public sealed class PayloadTooLargeAppException : AppException
{
    public PayloadTooLargeAppException(string message) : base(message)
    {
    }
}

public sealed class UnsupportedMediaTypeAppException : AppException
{
    public UnsupportedMediaTypeAppException(string message) : base(message)
    {
    }
}
