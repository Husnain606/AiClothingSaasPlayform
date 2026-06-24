namespace FashionSaaS.API.Middleware;

/// <summary>
/// Stub — Task 21 implements the full global exception-handling logic.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
    }
}
