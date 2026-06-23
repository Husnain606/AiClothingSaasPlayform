namespace FashionSaaS.Application.Exceptions;

public class ForbiddenException(string message = "Access denied.") : Exception(message) { }
