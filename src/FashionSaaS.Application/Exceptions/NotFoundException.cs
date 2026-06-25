namespace FashionSaaS.Application.Exceptions;

public class NotFoundException(string message) : Exception(message)
{
    public NotFoundException(string name, object key)
        : this($"{name} with key '{key}' was not found.") { }
}
