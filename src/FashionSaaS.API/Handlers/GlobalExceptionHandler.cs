using System.Text.Json;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace FashionSaaS.API.Handlers;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
            return false;

        var (statusCode, message, errors) = exception switch
        {
            NotFoundException ex       => (404, ex.Message,          (IEnumerable<string>?)null),
            ForbiddenException ex      => (403, ex.Message,          null),
            Application.Exceptions.ValidationException ex
                                       => (400, ex.Message,          ex.Errors),
            ConflictException ex       => (409, ex.Message,          null),
            _                          => (500, "An unexpected error occurred.", null)
        };

        if (statusCode == 500)
            logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode  = statusCode;
        httpContext.Response.ContentType = "application/json";

        var response = ResponseData<string>.Failure(message, statusCode, errors);
        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions),
            cancellationToken);

        return true;
    }
}
