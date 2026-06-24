using System.Text.Json;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Exceptions;

namespace FashionSaaS.API.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteResponse(context, 404, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            await WriteResponse(context, 403, ex.Message);
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            await WriteResponse(context, 400, ex.Message, ex.Errors);
        }
        catch (ConflictException ex)
        {
            await WriteResponse(context, 409, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await WriteResponse(context, 500, "An unexpected error occurred.");
        }
    }

    private static async Task WriteResponse(HttpContext context, int statusCode, string message,
        IEnumerable<string>? errors = null)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = ResponseData<string>.Failure(message, statusCode, errors);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
