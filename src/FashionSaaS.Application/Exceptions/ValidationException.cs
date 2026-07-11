namespace FashionSaaS.Application.Exceptions;

public class ValidationException : Exception
{
    public IEnumerable<string> Errors { get; }

    public ValidationException() : base("One or more validation errors occurred.")
    {
        Errors = [];
    }

    public ValidationException(string message) : base(message)
    {
        Errors = [];
    }

    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
        Errors = [];
    }

    public ValidationException(IEnumerable<string> errors) : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
