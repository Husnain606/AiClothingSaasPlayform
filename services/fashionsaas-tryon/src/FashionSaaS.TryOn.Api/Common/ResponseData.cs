namespace FashionSaaS.TryOn.Api.Common;

internal class ResponseData<T>
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public IEnumerable<string>? Errors { get; set; }

    public static ResponseData<T> Success(T data, string message = "Success", int statusCode = 200)
        => new() { IsSuccess = true, StatusCode = statusCode, Message = message, Data = data };

    public static ResponseData<T> Failure(string message, int statusCode = 400, IEnumerable<string>? errors = null)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message, Errors = errors };
}
